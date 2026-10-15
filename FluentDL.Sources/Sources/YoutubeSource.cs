using FluentDL.Contracts.Sources;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Helpers;
using FluentDL.Core.Sources;
using FluentDL.Models;
using System.Text.RegularExpressions;
using YoutubeExplode;
using YouTubeMusicAPI.Client;

namespace FluentDL.Services.Sources;

/// <summary>YouTube needs no sign-in, so it has no session.</summary>
internal sealed partial class YoutubeSource : ISearchSource, ILinkSource, IConversionTarget, ICatalogSource, IPreviewSource, IDownloadSource, ITagSource
{
    public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.YouTube)!;

    private readonly ILocalSettingsService settings;
    private readonly IAudioConverter audioConverter;

    public YoutubeSource(ILocalSettingsService settings, IAudioConverter audioConverter)
        : this(settings, audioConverter, new YouTubeMusicClient(), new YoutubeClient())
    {
    }

    internal YoutubeSource(ILocalSettingsService settings, IAudioConverter audioConverter,
        YouTubeMusicClient music, YoutubeClient videos)
    {
        this.settings = settings;
        this.audioConverter = audioConverter;
        ytm = music;
        youtube = videos;
    }

    public Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token) =>
        GeneralSearch(results, query, token, page.Offset, page.Limit, albumMode);

    public Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token) =>
        AdvancedSearch(results, query.Artist, query.Track, query.Album, token, page.Offset, page.Limit, clearResults: false);

    public bool CanHandle(string url) =>
        url.StartsWith("https://www.youtube.com/") || url.StartsWith("https://youtube.com/") || url.StartsWith("https://music.youtube.com/");

    public Task AddFromLinkAsync(ICollection<SongSearchObject> results, string url, LinkRequest request, UrlStatusUpdateCallback? status, CancellationToken token) =>
        request.IsFirstPage ? AddTracksFromLink(results, url, token, status, request.AlbumMode) : Task.CompletedTask;

    // YouTube matches by title and artist only, so the ISRC is not used.
    public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token, bool isrcOnly = false) =>
        CallbackResults.Match(callback => GetYoutubeTrack(song, token, callback));

    // AsServed: the raw opus stream (the Spotify equivalent chain). Otherwise the youtube_quality setting picks opus,
    // FLAC (from opus) or M4A (AAC), through yt-dlp when use_ytdlp is on, and FFmpeg. Moved unchanged from the old
    // download pipeline.
    public async Task<string> DownloadAsync(SongSearchObject track, string pathWithoutExtension, DownloadRequest request, IProgress<ProgressData>? progress)
    {
        if (request.Quality == DownloadQuality.AsServed)
        {
            var opusPath = pathWithoutExtension + ".opus";
            await DownloadAudio(opusPath, track.Id, progress!);
            return opusPath;
        }

        var converter = audioConverter;
        var flacLocation = pathWithoutExtension + ".flac";
        var opusLocation = pathWithoutExtension + ".opus";
        var m4aLocation = pathWithoutExtension + ".m4a";

        // 0 - opus
        // 1 - flac (opus)
        // 2 - m4a (aac)
        var settingIdx = await settings.ReadSettingAsync<int?>(SettingKeys.YoutubeQuality) ?? 0;
        var useYtdlp = await settings.ReadSettingAsync<bool>(SettingKeys.UseYtdlp);
        var ytdlpPath = useYtdlp ? await settings.ReadSettingAsync<string?>(SettingKeys.YtdlpPath) : null;

        // Ensure the base FluentDL temp directory exists
        var tempBaseDir = Path.Combine(Path.GetTempPath(), "FluentDL");
        Directory.CreateDirectory(tempBaseDir);

        // --- AAC / M4A PATH ---
        if (settingIdx == 2)
        {
            if (File.Exists(m4aLocation) && await settings.ReadSettingAsync<bool>(SettingKeys.Overwrite) == false)
            {
                throw new Exception("File already exists.");
            }

            var tempGuid = Guid.NewGuid().ToString("N");
            var tempM4a = Path.Combine(tempBaseDir, $"download-{tempGuid}.m4a");
            var tempMp4 = Path.Combine(tempBaseDir, $"download-{tempGuid}.mp4");

            try
            {
                if (useYtdlp)
                {
                    // yt-dlp extracts bestaudio[acodec=mp4a.40.2] which natively saves as .m4a
                    await YtDlp.DownloadAudioAACYTDLP(tempM4a, $"https://www.youtube.com/watch?v={track.Id}", ytdlpPath);
                    // Move the temp file to the final destination
                    File.Move(tempM4a, m4aLocation, overwrite: true);
                }
                else
                {
                    if (!converter.IsInitialized) throw new Exception("FFmpeg is not initialized.");
                    await DownloadAudioAAC(tempMp4, track.Id);
                    await converter.ConvertMp4ToM4aAsync(tempMp4, m4aLocation);
                }
                return m4aLocation;
            }
            finally
            {
                // Clean up possible temp files
                if (File.Exists(tempM4a)) File.Delete(tempM4a);
                if (File.Exists(tempMp4)) File.Delete(tempMp4);
            }
        }

        // --- OPUS / FLAC PATH ---
        var finalLocation = settingIdx == 1 ? flacLocation : opusLocation;

        if (File.Exists(finalLocation) && await settings.ReadSettingAsync<bool>(SettingKeys.Overwrite) == false)
        {
            throw new Exception("File already exists.");
        }

        // Generate a single unique temporary path for the WebM download
        var tempWebmLocation = Path.Combine(tempBaseDir, $"download-{Guid.NewGuid():N}.webm");

        try
        {
            // Both YT-DLP and Native download a .webm file
            if (useYtdlp)
            {
                await YtDlp.DownloadAudioYTDLP(tempWebmLocation, $"https://www.youtube.com/watch?v={track.Id}", ytdlpPath);
            }
            else
            {
                await DownloadAudio(tempWebmLocation, track.Id, progress!);
            }

            if (!converter.IsInitialized) throw new Exception("FFmpeg is not initialized.");

            if (settingIdx == 1) // FLAC
            {
                await converter.ConvertToFlacAsync(tempWebmLocation, flacLocation);
            }
            else // OPUS
            {
                await converter.RemuxOpusAsync(tempWebmLocation, opusLocation);
            }
            return finalLocation;
        }
        finally
        {
            // Always clean up the temporary .webm file
            if (File.Exists(tempWebmLocation))
            {
                File.Delete(tempWebmLocation);
            }
        }
    }

    public Task<SongSearchObject?> GetTrackAsync(string id) => GetTrack(id);

    public async Task<string?> GetLargestCoverUrlAsync(AlbumSearchObject album) => await GetMaxResThumbnail(album);

    public Task WriteTagsAsync(string filePath, SongSearchObject track) => UpdateMetadata(filePath, track.Id);

    // YouTube album results already hold full tracks.
    public Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album) => Task.FromResult(album.TrackList ?? []);

    // The preview pane loads YouTube audio itself, because it may use yt-dlp (an app setting) and shows progress.
    public async Task<PreviewInfo> GetPreviewAsync(SongSearchObject song)
    {
        if (song is AlbumSearchObject album)
        {
            var cover = await GetMaxResThumbnail(song) ?? "";
            album.TrackList ??= [];
            return new PreviewInfo { CoverUrl = cover, AlbumTracks = album.TrackList, AlbumTrackCount = album.TracksCount };
        }
        var releaseDate = song.ReleaseDate;
        var views = song.Rank;
        string? trackCover;
        if (string.IsNullOrWhiteSpace(releaseDate))
        {
            // Search rows already have Music metadata. Only the selected row needs video views and upload date.
            try
            {
                var video = await youtube.Videos.GetAsync(song.Id);
                releaseDate = DisplayFormat.FormatDateTimeOffset(video.UploadDate);
                views = FormatLargeValue(video.Engagement.ViewCount);
                trackCover = Regex.IsMatch(song.ImageLocation, @"=w\d+-h\d+[^&]*")
                    ? Regex.Replace(song.ImageLocation, @"=w\d+-h\d+[^&]*", "=w544-h544-l90-rj")
                    : GetMaxResThumbnail(null, video);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Serilog.Log.Warning("YouTube preview metadata unavailable; error type {ErrorType}", e.GetType().Name);
                trackCover = song.ImageLocation;
            }
        }
        else trackCover = await GetMaxResThumbnail(song);
        // The audio loads after the details: through yt-dlp when that setting is on (the pane does that), otherwise
        // through GetPreviewAudioAsync.
        return new PreviewInfo { CoverUrl = trackCover, ReleaseDate = releaseDate, Rows = [("Views", views)], LoadAudioSeparately = true };
    }

    // Used when yt-dlp is off.
    public async Task<string?> GetPreviewAudioAsync(SongSearchObject track) =>
        await AudioStreamWorstUrl("https://www.youtube.com/watch?v=" + track.Id);
}
