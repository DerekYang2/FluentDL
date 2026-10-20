#nullable enable
namespace FluentDL.Core.Updates;

public sealed class OperationGate
{
    private readonly object sync = new();
    private int active;
    private bool installing;
    private bool exclusiveWork;

    public bool IsInstalling { get { lock (sync) return installing; } }
    public int ActiveCount { get { lock (sync) return active; } }
    public bool IsExclusiveWork { get { lock (sync) return exclusiveWork; } }

    public IDisposable EnterWork()
    {
        lock (sync)
        {
            if (installing) throw new InvalidOperationException("An app update is being installed. Wait for it to finish.");
            if (exclusiveWork) throw new InvalidOperationException("The queue is being changed. Wait for it to finish.");
            active++;
            return new Lease(() => { lock (sync) active--; });
        }
    }

    public IDisposable? TryEnterInstallation()
    {
        lock (sync)
        {
            if (installing || active != 0) return null;
            installing = true;
            return new Lease(() => { lock (sync) installing = false; });
        }
    }

    public IDisposable? TryEnterExclusiveWork()
    {
        lock (sync)
        {
            if (installing || active != 0) return null;
            exclusiveWork = true;
            active = 1;
            return new Lease(() => { lock (sync) { exclusiveWork = false; active = 0; } });
        }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? action = release;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
