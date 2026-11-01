using FluentDL.Core.Updates;

namespace FluentDL.Helpers;

internal static class ApplicationActivity
{
    internal static OperationGate Gate { get; } = new();
    private static readonly FileWriteTracker fileWrites = new(Gate);

    internal static Task RunFileWriteAsync(Action write) => fileWrites.RunAsync(write);
    internal static Task StopFileWritesAsync() => fileWrites.StopAndDrainAsync();

    internal static Task RunBackground(Func<Task> action)
    {
        var lease = Gate.EnterWork();
        return Task.Run(async () =>
        {
            using (lease)
            {
                try { await action(); }
                catch (Exception error) { Serilog.Log.Warning(error, "Background work failed"); }
            }
        });
    }
}
