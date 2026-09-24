using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OptimizeAll.IntegrationTests.Infrastructure;

/// <summary>
/// Counts the SQL commands one <see cref="DbContext"/> instance executes (through EF Core's diagnostic events), so a test
/// can assert that a bulk action does not run queries per row. Commands of other contexts (parallel tests) are ignored.
/// </summary>
public sealed class SqlCommandCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly DbContext _context;
    private readonly Action _increment;
    private readonly List<IDisposable> _subscriptions = new();

    public SqlCommandCounter(DbContext context, Action increment)
    {
        _context = context;
        _increment = increment;
        var all = DiagnosticListener.AllListeners.Subscribe(this);
        lock (_subscriptions) _subscriptions.Add(all);
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name != DbLoggerCategory.Name) return;
        var subscription = listener.Subscribe(this);
        lock (_subscriptions) _subscriptions.Add(subscription);
    }

    public void OnNext(KeyValuePair<string, object?> e)
    {
        if (e.Key == RelationalEventId.CommandExecuted.Name && e.Value is CommandExecutedEventData data && ReferenceEquals(data.Context, _context))
            _increment();
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        lock (_subscriptions)
        {
            foreach (var s in _subscriptions) s.Dispose();
            _subscriptions.Clear();
        }
    }
}
