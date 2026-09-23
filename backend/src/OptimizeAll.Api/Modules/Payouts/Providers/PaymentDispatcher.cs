using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Payouts.Providers;

/// <summary>
/// Hands AwaitingPayment items to their payment provider. One <see cref="PaymentAttempt"/> per item with the
/// idempotency key <c>payout-item:{itemId}:dispatch</c> (unique index): a retried dispatch reuses the existing attempt
/// and never calls the provider twice for an attempt that already has an outcome. A provider-confirmed success is
/// recorded through <see cref="PayoutPaymentService.RecordPaymentAsync"/> — the same atomic path as a manual payment.
/// </summary>
public sealed class PaymentDispatcher(
    AppDbContext db,
    IPaymentProviderRegistry providers,
    IServiceProvider services,
    TimeProvider clock,
    ILogger<PaymentDispatcher> logger)
{
    public static string DispatchKey(Guid itemId) => $"payout-item:{itemId}:dispatch";

    public async Task<IReadOnlyList<DispatchResultDto>> DispatchAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        var results = new List<DispatchResultDto>();
        foreach (var itemId in itemIds)
        {
            try
            {
                results.Add(await DispatchOneAsync(itemId, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The attempt stays "Created"; re-dispatching retries it with the same idempotency key.
                logger.LogError(ex, "Dispatch of payout item {Item} failed", itemId);
                results.Add(new DispatchResultDto(itemId, PaymentAttemptStatus.Created, null, "Dispatch failed; retry later.", false));
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        return results;
    }

    private async Task<DispatchResultDto> DispatchOneAsync(Guid itemId, CancellationToken ct)
    {
        var item = await db.Set<PayoutItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct)
                   ?? throw DomainException.NotFound("PayoutItem");
        var key = DispatchKey(itemId);
        var now = clock.GetUtcNow().UtcDateTime;

        var attempt = await db.Set<PaymentAttempt>().FirstOrDefaultAsync(a => a.IdempotencyKey == key, ct);
        if (attempt is not null && attempt.Status != PaymentAttemptStatus.Created)
            return new DispatchResultDto(itemId, attempt.Status, attempt.ProviderReference, attempt.Message ?? string.Empty, true);
        if (item.Status != PayoutItemStatus.AwaitingPayment)
            return new DispatchResultDto(itemId, attempt?.Status ?? PaymentAttemptStatus.Created, null,
                $"Item is {item.Status}; nothing to dispatch.", attempt is not null);

        var reused = attempt is not null;
        if (attempt is null)
        {
            attempt = new PaymentAttempt
            {
                PayoutItemId = itemId,
                Provider = item.PaymentProvider,
                IdempotencyKey = key,
                Status = PaymentAttemptStatus.Created,
                CreatedAt = now,
            };
            db.Set<PaymentAttempt>().Add(attempt);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
            {
                // A concurrent dispatcher owns this attempt.
                db.ChangeTracker.Clear();
                var existing = await db.Set<PaymentAttempt>().AsNoTracking().FirstAsync(a => a.IdempotencyKey == key, ct);
                return new DispatchResultDto(itemId, existing.Status, existing.ProviderReference, existing.Message ?? string.Empty, true);
            }
        }

        var profile = await db.Set<PayoutProfile>().AsNoTracking().Where(p => p.UserId == item.UserId)
            .OrderByDescending(p => p.UpdatedAt).Select(p => new { p.Method, p.MaskedDestination }).FirstOrDefaultAsync(ct);
        var provider = providers.Get(item.PaymentProvider);
        var result = await provider.DispatchAsync(new PaymentDispatchRequest(
            itemId, item.UserId, item.Amount, item.Currency, key, profile?.Method, profile?.MaskedDestination), ct);

        attempt.Status = result.Status == PaymentAttemptStatus.Succeeded ? PaymentAttemptStatus.Submitted : result.Status;
        attempt.ProviderReference = result.ProviderReference;
        attempt.Message = result.Message.Length > 1000 ? result.Message[..1000] : result.Message;
        attempt.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var payments = services.GetRequiredService<PayoutPaymentService>();
        switch (result.Status)
        {
            case PaymentAttemptStatus.Succeeded:
                // Provider confirmed the transfer synchronously: record it through the atomic payment path.
                await payments.RecordPaymentAsync(item.BatchId, itemId,
                    result.ProviderReference ?? throw new InvalidOperationException($"Provider {provider.Key} reported success without a reference."),
                    clock.GetUtcNow().UtcDateTime, null, null, ct);
                break;
            case PaymentAttemptStatus.Failed:
                await payments.MarkFailedAsync(item.BatchId, itemId, $"Provider {provider.Key}: {result.Message}", null, ct);
                break;
        }
        return new DispatchResultDto(itemId, result.Status, result.ProviderReference, result.Message, reused);
    }
}
