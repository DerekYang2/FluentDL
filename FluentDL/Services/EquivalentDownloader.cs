using System.Diagnostics;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services.Sources;

namespace FluentDL.Services;

/// <summary>
/// Downloads a track from a catalog-only source (Spotify) by finding the same track on a source that serves audio.
/// The order and qualities are the old SpotifyApi.DownloadEquivalentTrack's, step by step:
/// 1. Deezer, matched by ISRC only when strict, at MP3 320 or FLAC (spotify_quality).
/// 2. Qobuz, matched the same way, at format 5 or " 6" (bug 2).
/// 3. Deezer's match again, at MP3 128 or FLAC with the 128 kbps fallback.
/// 4. When not strict, YouTube's opus stream, converted to FLAC or MP3 with FFmpeg.
/// </summary>
public sealed class EquivalentDownloader(SourceRegistry sources)
{
    public async Task<string?> DownloadAsync(string filePath, SongSearchObject song, IProgress<ProgressData> progress, bool strict = true, ConversionUpdateCallback? callback = default)
    {
        if (sources.TryGet<ISourceSession>(song.Source, out var session) && !session.IsConnected)
        {
            throw new Exception($"{session.Descriptor.DisplayName} API not initialized");
        }
        // Remove extension if it exists
        filePath = ApiHelper.RemoveExtension(filePath);
        // 0 - mp3, 1 - flac
        var settingIdx = await SourceServices.Settings.ReadSettingAsync<int?>(SettingKeys.SpotifyQuality);
        var quality = settingIdx == 0 ? DownloadQuality.Mp3 : DownloadQuality.Lossless;

        // Web player results carry no ISRC, so look it up before each match, as the matchers used to.
        async Task<string?> Isrc() =>
            string.IsNullOrWhiteSpace(song.Isrc) && sources.TryGet<IIsrcSource>(song.Source, out var origin) ? await origin.GetIsrcAsync(song.Id) : song.Isrc;

        // Try Deezer first
        SongSearchObject? deezerEquivalent = null;
        try
        {
            deezerEquivalent = (await Target(SourceIds.Deezer).FindEquivalentAsync(song, await Isrc(), CancellationToken.None, isrcOnly: strict)).Track;
            if (deezerEquivalent != null) // Found on Deezer
            {
                var resultPath = await Download(SourceIds.Deezer).DownloadAsync(deezerEquivalent, filePath, new(quality), progress);
                callback?.Invoke(StatusLevel.Success, song, resultPath);
                return resultPath;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }

        // Not found on Deezer, try Qobuz
        try
        {
            var qobuzEquivalent = (await Target(SourceIds.Qobuz).FindEquivalentAsync(song, await Isrc(), CancellationToken.None, isrcOnly: strict)).Track;
            if (qobuzEquivalent != null) // Found on Qobuz
            {
                var resultPath = await Download(SourceIds.Qobuz).DownloadAsync(qobuzEquivalent, filePath, new(quality), progress);
                callback?.Invoke(StatusLevel.Success, song, resultPath);
                return resultPath;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }

        // Reattempt deezer with 128 kbps fallback
        if (deezerEquivalent != null) // Found on Deezer
        {
            try
            {
                var resultPath = await Download(SourceIds.Deezer).DownloadAsync(deezerEquivalent, filePath, new(quality, Allow128Fallback: true), progress);
                callback?.Invoke(StatusLevel.Success, song, resultPath);
                return resultPath;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }

        if (strict) // If strict, do not try youtube
        {
            throw new Exception("Equivalent not found in other sources");
        }

        var opusLocation = ApiHelper.RemoveExtension(filePath) + ".opus";
        // A missing setting checks for .flac here but converts to MP3 below; kept as it was.
        var actualLocation = ApiHelper.RemoveExtension(filePath) + (settingIdx == 0 ? ".mp3" : ".flac");

        if (File.Exists(actualLocation) && await SourceServices.Settings.ReadSettingAsync<bool>(SettingKeys.Overwrite) == false) // If file exists and overwrite is false
        {
            throw new Exception("File already exists");
        }

        if (!SourceServices.AudioConverter.IsInitialized)
        {
            throw new Exception("FFmpeg not initialized");
        }

        var equivalent = (await Target(SourceIds.YouTube).FindEquivalentAsync(song, null, CancellationToken.None)).Track
            ?? throw new Exception("Equivalent not found in other sources");
        opusLocation = await Download(SourceIds.YouTube).DownloadAsync(equivalent, ApiHelper.RemoveExtension(filePath), new(DownloadQuality.AsServed), progress); // Download audio as opus

        string? convertedLocation = "";

        if (settingIdx == 1)
        {
            await SourceServices.AudioConverter.ConvertToFlacAsync(opusLocation); // Convert opus to flac
            convertedLocation = opusLocation.Replace(".opus", ".flac");
        }
        else
        {
            convertedLocation = await SourceServices.AudioConverter.CreateMp3Async(opusLocation);
            await Task.Run(() => File.Delete(opusLocation));
        }
        callback?.Invoke(StatusLevel.Warning, song, convertedLocation); // Not perfect match
        return convertedLocation; // Return the converted location
    }

    private IConversionTarget Target(string id) =>
        sources.TryGet<IConversionTarget>(id, out var target) ? target : throw new Exception($"No conversion target for {id}");

    private IDownloadSource Download(string id) =>
        sources.TryGet<IDownloadSource>(id, out var source) ? source : throw new Exception($"No download source for {id}");
}
