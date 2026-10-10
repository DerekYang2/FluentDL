using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Services.Sources;

/// <summary>
/// Turns the static source classes' callbacks into the return values the role interfaces use. The static methods
/// run unchanged; this only records what they report.
/// </summary>
internal static class CallbackResults
{
    public static async Task<ConnectResult> Connect(Func<AuthenticationCallback, Task> connect, Func<bool> isConnected)
    {
        StatusLevel? level = null;
        string? message = null;
        await connect((l, m) => (level, message) = (l, m));
        var connected = isConnected();
        return new ConnectResult(connected, level ?? (connected ? StatusLevel.Success : StatusLevel.Error), message);
    }

    public static async Task<MatchResult> Match(Func<ConversionUpdateCallback, Task<SongSearchObject?>> find)
    {
        StatusLevel? level = null;
        var track = await find((l, _, _) => level = l);
        var kind = level switch
        {
            StatusLevel.Success => MatchKind.Exact,
            StatusLevel.Warning => MatchKind.Close,
            StatusLevel.Error => MatchKind.NotFound,
            _ => MatchKind.NotReported,
        };
        return new MatchResult(track, kind);
    }
}
