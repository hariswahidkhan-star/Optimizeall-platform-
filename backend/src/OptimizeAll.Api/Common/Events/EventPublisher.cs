using OptimizeAll.Domain.Events;

namespace OptimizeAll.Api.Common.Events;

/// <summary>Handles an in-process domain event. Implementations must be idempotent and must not throw for expected cases.</summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

public interface IEventPublisher
{
    /// <summary>
    /// Invokes every registered handler in its own DI scope (so a handler failure cannot corrupt the caller's
    /// DbContext). Call only after the originating transaction has committed. Handler failures are logged and
    /// do not fail the caller; handlers that must not be lost should also be covered by a reconciliation job.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent;
}

public sealed class EventPublisher(IServiceScopeFactory scopeFactory, ILogger<EventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : IDomainEvent
    {
        List<Type> handlerTypes;
        using (var probe = scopeFactory.CreateScope())
        {
            handlerTypes = probe.ServiceProvider.GetServices<IEventHandler<TEvent>>().Select(h => h.GetType()).ToList();
        }

        foreach (var handlerType in handlerTypes)
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IEventHandler<TEvent>>().First(h => h.GetType() == handlerType);
            try
            {
                await handler.HandleAsync(domainEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Event handler {Handler} failed for {Event}", handlerType.Name, typeof(TEvent).Name);
            }
        }
    }
}
