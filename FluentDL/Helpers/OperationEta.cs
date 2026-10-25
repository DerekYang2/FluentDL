namespace FluentDL.Helpers;

internal sealed class OperationEta(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private EtaEstimator estimator = new();
    private long? started;

    internal void Reset()
    {
        started = null;
        estimator = new EtaEstimator();
    }

    internal string? Update(int completed, int total)
    {
        if (total <= 0) return null;
        // Start only when the actual work count is known, not during validation or tag discovery.
        started ??= time.GetTimestamp();
        return estimator.Update(Math.Clamp((double)completed / total, 0, 1), time.GetElapsedTime(started.Value));
    }
}
