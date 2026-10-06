using FluentDL.Core.ReplayGain;
using FluentDL.ViewModels;
using Serilog;

namespace FluentDL.Services;

// Writes ReplayGain tags to finished downloads when Settings > ReplayGain > Tag Downloads is on.
// Downloads get track gain only, except whole albums from Search when Album Gain for Album Downloads is on.
// The queue holds single tracks, so it can't tell when an album is complete.
internal static class ReplayGainDownloads
{
    public static async Task TagTrackAsync(string path)
    {
        try
        {
            if (await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.ReplayGainAutoApply) != true) return;
            var (offset, preventClipping) = await ReadOptionsAsync();
            var result = await ReplayGainScanner.ApplyTrackGainAsync(path, offset, preventClipping);
            Log.Debug("Wrote ReplayGain track gain {TrackGain:0.00} dB to a downloaded track", result.TrackGain);
        }
        catch (Exception e)
        {
            // The download itself succeeded, so a tagging failure is only logged.
            Log.Warning(e, "Could not write ReplayGain tags to a downloaded track");
        }
    }

    // Tags an album download once all of its tracks have finished. The download is one album whatever its tags
    // say, so its tracks share one album gain. Album gain needs every track, so an album with a track missing
    // gets track gain only, and the scanner does the same when a downloaded track can't be decoded.
    public static async Task TagAlbumAsync(IReadOnlyList<string> paths, bool complete)
    {
        if (paths.Count == 0) return;
        try
        {
            if (await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.ReplayGainAutoApply) != true) return;
            var albumGainOn = await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.ReplayGainAlbumDownloads) == true;
            var (offset, preventClipping) = await ReadOptionsAsync();
            var options = new ReplayGainOptions(TrackGain: true, AlbumGain: albumGainOn && complete, SingleAlbum: true,
                Offset: offset, PreventClipping: preventClipping);
            var scan = await ReplayGainScanner.ScanAsync(paths.Select(path => new ReplayGainTrack(path)).ToList(), options);
            var writeFailures = await Task.Run(() => ReplayGainScanner.WriteTags(scan.Results, options));

            var failed = scan.Failures.Count + writeFailures.Count;
            if (failed > 0) Log.Warning("Could not write ReplayGain tags to {FailedCount} of {TrackCount} album tracks", failed, paths.Count);
            if (albumGainOn && scan.Results.FirstOrDefault()?.AlbumGain is { } albumGain)
                Log.Debug("Wrote ReplayGain album gain {AlbumGain:0.00} dB to a downloaded album", albumGain);
            else if (albumGainOn)
                Log.Information("A downloaded album was missing a track, so its tracks got ReplayGain track gain only");
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not write ReplayGain tags to a downloaded album");
        }
    }

    private static async Task<(double Offset, bool PreventClipping)> ReadOptionsAsync() => (
        await SettingsViewModel.GetSetting<double?>(SettingsViewModel.ReplayGainOffset) ?? 0,
        await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.ReplayGainPreventClipping) ?? false);
}
