using System.Reactive.Concurrency;
using System.Reactive.Disposables;

namespace SvcSystems.Avalonia.DynamicTableView.Benchmarks;

internal sealed class QueuedScheduler : IScheduler
{
    private readonly Queue<Action> _actions = new();

    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
        => Enqueue(state, action);

    public IDisposable Schedule<TState>(
        TState state,
        TimeSpan dueTime,
        Func<IScheduler, TState, IDisposable> action)
        => Enqueue(state, action);

    public IDisposable Schedule<TState>(
        TState state,
        DateTimeOffset dueTime,
        Func<IScheduler, TState, IDisposable> action)
        => Enqueue(state, action);

    public void RunUntilIdle()
    {
        while (_actions.TryDequeue(out var scheduledAction))
        {
            scheduledAction();
        }
    }

    private IDisposable Enqueue<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
    {
        _actions.Enqueue(() => action(this, state).Dispose());
        return Disposable.Empty;
    }
}
