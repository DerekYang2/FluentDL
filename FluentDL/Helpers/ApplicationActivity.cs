using FluentDL.Core.Updates;

namespace FluentDL.Helpers;

internal static class ApplicationActivity
{
    internal static OperationGate Gate { get; } = new();

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
