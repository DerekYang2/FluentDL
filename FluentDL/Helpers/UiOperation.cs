namespace FluentDL.Helpers;

internal static class UiOperation
{
    internal static async Task<bool> RunAsync(Func<Task> action, Action<Exception> onFailure, CancellationToken token = default)
    {
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            Serilog.Log.Warning(error, "UI operation could not complete");
            onFailure(error);
            return false;
        }
    }
}
