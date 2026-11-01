using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Helpers;

namespace FluentDL.ViewModels;

public enum PlaylistNoticeLevel { Information, Warning, Success }

public sealed class PlaylistChoice(OwnedPlaylist? playlist = null, string? displayName = null)
{
    public OwnedPlaylist? Playlist { get; } = playlist;
    public string DisplayName => displayName ?? Playlist?.Name ?? "Create a new playlist";
    public string Details => Playlist == null ? DisplayName : $"{Playlist.Name} [{Playlist.Id}]";
}

public sealed class PlaylistVisibilityOption(PlaylistVisibility value, bool spotify = false)
{
    public PlaylistVisibility Value { get; } = value;
    public string DisplayName => spotify ? Value == PlaylistVisibility.Public ? "Shown on profile" : "Not shown on profile" : Value switch
    {
        PlaylistVisibility.Private => "Private, only you",
        PlaylistVisibility.Unlisted => "Unlisted, anyone with the link",
        _ => "Public",
    };
}

public sealed class PlaylistDestination(IPlaylistWriter writer, IReadOnlyList<QueueObject> queue)
{
    public IPlaylistWriter Writer { get; } = writer;
    public string DisplayName => Writer.Descriptor.DisplayName;
    public IReadOnlyList<PlaylistVisibilityOption> Visibilities { get; } = writer.SupportedPlaylistVisibilities.Select(value => new PlaylistVisibilityOption(value, writer.Descriptor.Id == SourceIds.Spotify)).ToArray();
    // Capture immutable IDs, not live queue rows that a background task could replace.
    public IReadOnlyList<string> TrackIds { get; } = queue
        .Where(row => row.Source == writer.Descriptor.Id && !row.IsAlbum)
        .OrderBy(row => row.QueueCounter)
        .Select(row => row.Id).ToArray();
    public int ExcludedAlbums { get; } = queue.Count(row => row.Source == writer.Descriptor.Id && row.IsAlbum);
}

public sealed class PlaylistDialogViewModel : ObservableObject
{
    private string name = "";
    private string description = "";
    private int visibilityIndex;
    private PlaylistDestination? selectedDestination;
    private bool isChecking;
    private bool isBusy;
    private bool signedIn;
    private string message = "Checking sign-in...";
    private PlaylistNoticeLevel severity = PlaylistNoticeLevel.Information;
    private int added;
    private PlaylistCreationResult? result;
    private CancellationTokenSource? operation;
    private CancellationTokenSource? check;
    private PlaylistChoice selectedPlaylist;
    private bool loadingPlaylists;
    private string? playlistListError;
    private CancellationTokenSource? trackCheck;
    private bool checkingTracks;
    private string? statisticsError;
    private int? tracksToSave;
    private int? duplicatesExcluded;

    public PlaylistDialogViewModel(IEnumerable<IPlaylistWriter> writers, IReadOnlyList<QueueObject> queue)
    {
        selectedPlaylist = new PlaylistChoice();
        Playlists.Add(selectedPlaylist);
        Destinations = writers.Select(writer => new PlaylistDestination(writer, queue)).ToArray();
        var firstSource = queue.Where(row => !row.IsAlbum && Destinations.Any(destination => destination.Writer.Descriptor.Id == row.Source))
            .OrderBy(row => row.QueueCounter).FirstOrDefault()?.Source;
        firstSource ??= queue.OrderBy(row => row.QueueCounter).FirstOrDefault(row => Destinations.Any(destination => destination.Writer.Descriptor.Id == row.Source))?.Source;
        selectedDestination = Destinations.FirstOrDefault(destination => destination.Writer.Descriptor.Id == firstSource) ?? Destinations.FirstOrDefault();
        isChecking = selectedDestination != null;
        if (selectedDestination == null) message = "No playlist destinations are available.";
        SetStatistics([]);
    }

    public IReadOnlyList<PlaylistDestination> Destinations { get; }
    public PlaylistDestination? SelectedDestination
    {
        get => selectedDestination;
        set
        {
            if (isBusy || HasResult || !SetProperty(ref selectedDestination, value)) return;
            signedIn = false;
            ResetPlaylists();
            visibilityIndex = 0; // Switching services starts at that service's non-public default.
            OnPropertyChanged(nameof(VisibilityOptions));
            OnPropertyChanged(nameof(VisibilityIndex));
            Changed();
            _ = CheckSignInAsync(); // This method catches failures; selection must not depend on view event order.
        }
    }
    public ObservableCollection<PlaylistChoice> Playlists { get; } = [];
    public PlaylistChoice SelectedPlaylist
    {
        get => selectedPlaylist;
        set
        {
            if (value == null || !CanEdit || !Playlists.Contains(value) || !SetProperty(ref selectedPlaylist, value)) return;
            _ = CheckStatisticsAsync();
            Changed();
        }
    }
    public bool IsNewPlaylist => SelectedPlaylist.Playlist == null;
    public bool CanChoosePlaylist => CanEdit && signedIn && !isChecking && !loadingPlaylists;
    public bool CanRefreshPlaylists => CanEdit && signedIn && !isChecking && !loadingPlaylists;
    public bool HasSelectedPlaylist => SelectedPlaylist.Playlist != null;
    public bool CanOpenSelectedPlaylist => CanRefreshPlaylists && HasSelectedPlaylist;
    public string Name { get => name; set { if (SetProperty(ref name, value)) Changed(); } }
    public string Description { get => description; set { if (SetProperty(ref description, value)) Changed(); } }
    public IReadOnlyList<PlaylistVisibilityOption> VisibilityOptions => SelectedDestination?.Visibilities ?? [];
    public int NameMaxLength => SelectedDestination?.Writer.PlaylistNameMaxLength ?? 250;
    public int VisibilityIndex
    {
        get => visibilityIndex;
        set
        {
            if (value < 0 || value >= VisibilityOptions.Count) return; // ComboBox briefly clears selection when its items change.
            if (SetProperty(ref visibilityIndex, value)) Changed();
        }
    }
    public bool IsChecking => isChecking;
    public bool IsBusy => isBusy;
    public bool CanEdit => !isBusy && result == null;
    public bool CanChooseSource => CanEdit && !isChecking;
    public bool CanCreate => CanEdit && !isChecking && !loadingPlaylists && !checkingTracks && statisticsError == null
        && tracksToSave != null && duplicatesExcluded != null && signedIn && SelectedDestination?.TrackIds.Count > 0
        && (!IsNewPlaylist || (Name.Trim().Length > 0 && Name.Trim().Length <= NameMaxLength && Description.Trim().Length <= 500 && VisibilityIndex >= 0 && VisibilityIndex < VisibilityOptions.Count));
    public string Summary => SelectedDestination is { } destination
        ? $"{(tracksToSave is { } tracks ? $"{tracks} {destination.DisplayName} {(tracks == 1 ? "track" : "tracks")} will be saved" : checkingTracks ? "Tracks to save: checking..." : "Tracks to save: unavailable")}\n"
            + $"{destination.ExcludedAlbums} {(destination.ExcludedAlbums == 1 ? "album" : "albums")} excluded\n"
            + (duplicatesExcluded is { } duplicates ? $"{duplicates} {(duplicates == 1 ? "duplicate" : "duplicates")} excluded" : checkingTracks ? "Duplicates excluded: checking..." : "Duplicates excluded: unavailable")
        : "No tracks selected.";
    public string Message => HasResult ? message : string.Join(" ", new[]
    {
        statisticsError ?? playlistListError ?? (checkingTracks ? "Checking the selected playlist for duplicates." : loadingPlaylists ? "Loading your playlists." : message),
        IsNewPlaylist && SelectedDestination?.Writer.Descriptor.Id == SourceIds.Spotify
            ? "FluentDL only controls whether this playlist appears on your profile. Change access/privacy in Spotify." : null,
    });
    public PlaylistNoticeLevel Severity => !HasResult && (statisticsError != null || playlistListError != null) ? PlaylistNoticeLevel.Warning : severity;
    public string CloseText => isBusy ? "Cancel" : result == null ? "Cancel" : "Close";
    public double Percent => SelectedDestination?.TrackIds.Count > 0 ? 100d * (added + (result?.Skipped ?? 0)) / SelectedDestination.TrackIds.Count : 0;
    public bool IsIndeterminate => isBusy && added == 0;
    public Uri? PlaylistUrl => result?.Url;
    public PlaylistCreationResult? Result => result;
    public bool HasResult => result != null;
    public string DialogTitle => "Upload playlist";
    public string ResultLabel => result?.Status == PlaylistCreationStatus.Completed ? IsNewPlaylist ? "Playlist created:" : "Playlist updated:"
        : PlaylistUrl != null ? IsNewPlaylist ? "Playlist created, upload incomplete:" : "Playlist update incomplete:"
        : "Playlist upload not completed:";
    public string PrimaryText => HasResult ? PlaylistUrl != null ? "Open playlist" : "" : IsNewPlaylist ? "Upload" : "Add to playlist";
    public bool CanUsePrimary => HasResult ? !isBusy && PlaylistUrl != null : CanCreate;
    public string ResultName => SelectedPlaylist.Playlist?.Name ?? Name.Trim();
    public string? SignInSourceId => !HasResult && !isChecking && !isBusy && !signedIn && SelectedDestination?.TrackIds.Count > 0
        && SettingsNavigation.ForSource(SelectedDestination.Writer.Descriptor.Id) != null ? SelectedDestination.Writer.Descriptor.Id : null;
    public bool NeedsSignIn => SignInSourceId != null;

    public async Task CheckSignInAsync()
    {
        check?.Cancel();
        trackCheck?.Cancel();
        var selected = SelectedDestination;
        using var current = check = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        isChecking = true;
        SetMessage(PlaylistNoticeLevel.Information, selected == null ? "No playlist destinations are available." : $"Checking {selected.DisplayName} sign-in...");
        Changed();
        try
        {
            var ready = selected != null && await selected.Writer.CanCreatePlaylistAsync(current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (current.IsCancellationRequested || !ReferenceEquals(check, current) || !ReferenceEquals(SelectedDestination, selected)) return;
            signedIn = ready;
            if (!ready) ResetPlaylists();
            isChecking = false;
            if (signedIn && selected?.TrackIds.Count == 0)
                SetMessage(PlaylistNoticeLevel.Warning, $"There are no individual {selected.DisplayName} tracks in this queue snapshot. Album rows are excluded.");
            else
                SetMessage(signedIn ? PlaylistNoticeLevel.Information : PlaylistNoticeLevel.Warning, signedIn
                    ? "Ready."
                    : selected?.Writer.PlaylistSignInHint ?? "Choose a playlist destination.");
            if (ready && selected != null)
            {
                loadingPlaylists = true;
                playlistListError = null;
                Changed();
                try
                {
                    var playlists = await selected.Writer.GetOwnedPlaylistsAsync(current.Token);
                    if (current.IsCancellationRequested || !ReferenceEquals(check, current) || !ReferenceEquals(SelectedDestination, selected) || !CanEdit) return;
                    var selectedId = SelectedPlaylist.Playlist?.Id;
                    ResetPlaylists();
                    var repeatedNames = playlists.GroupBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                    foreach (var playlist in playlists)
                        Playlists.Add(new PlaylistChoice(playlist, repeatedNames.Contains(playlist.Name) ? $"{playlist.Name} · {playlist.Id[^Math.Min(6, playlist.Id.Length)..]}" : null));
                    SelectedPlaylist = Playlists.FirstOrDefault(p => p.Playlist?.Id == selectedId) ?? Playlists[0];
                    playlistListError = null;
                }
                catch (Exception error)
                {
                    if (ReferenceEquals(check, current))
                    {
                        Serilog.Log.Warning("Playlist listing stopped: {ExceptionType}", error.GetType().Name);
                        if (CanEdit) ResetPlaylists();
                        playlistListError = "Could not load existing playlists. You can still create a new one, or refresh the list.";
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (ReferenceEquals(check, current))
            {
                signedIn = false;
                Serilog.Log.Warning("Playlist sign-in check stopped: {ExceptionType}", e.GetType().Name);
                SetMessage(PlaylistNoticeLevel.Warning, $"Could not check {selected?.DisplayName ?? "the source"} sign-in. Close this dialog and check Settings.");
            }
        }
        finally
        {
            if (ReferenceEquals(check, current)) { check = null; isChecking = false; loadingPlaylists = false; Changed(); }
        }
    }

    public async Task CreateAsync()
    {
        if (!CanCreate || SelectedDestination is not { } selected) return;
        if (ApplicationActivity.Gate.IsInstalling) { SetMessage(PlaylistNoticeLevel.Warning, "Wait for the app update to finish."); return; }
        using var activity = ApplicationActivity.Gate.EnterWork();
        using var current = operation = new CancellationTokenSource();
        isBusy = true;
        Changed();
        SetMessage(PlaylistNoticeLevel.Information, IsNewPlaylist ? "Creating playlist..." : "Checking the existing playlist and skipping duplicates...");
        var progress = new Progress<int>(count =>
        {
            if (!isBusy || !ReferenceEquals(operation, current)) return;
            added = count;
            SetMessage(PlaylistNoticeLevel.Information, $"Added {count} of {selected.TrackIds.Count} tracks...");
            Changed();
        });
        try
        {
            result = await selected.Writer.CreatePlaylistAsync(new(Name.Trim(), Description.Trim(),
                selected.Visibilities[VisibilityIndex].Value, selected.TrackIds, SelectedPlaylist.Playlist), progress, current.Token);
            added = result.Added;
            var summary = $"Added {result.Added} of {result.Total} tracks. Skipped {result.Skipped} {(result.Skipped == 1 ? "duplicate" : "duplicates")}.";
            SetMessage(result.Status == PlaylistCreationStatus.Completed ? PlaylistNoticeLevel.Success : PlaylistNoticeLevel.Warning,
                result.Status == PlaylistCreationStatus.Completed ? summary : $"{summary} {result.Message}");
        }
        catch (Exception e)
        {
            // A writer should return an outcome for network failures. Don't invite a duplicate create if it throws.
            Serilog.Log.Warning("Playlist creation stopped: {ExceptionType}", e.GetType().Name);
            result = new(null, added, selected.TrackIds.Count, PlaylistCreationStatus.Uncertain);
            SetMessage(PlaylistNoticeLevel.Warning, $"The result could not be confirmed. Check {selected.DisplayName} before trying again.");
        }
        finally { operation = null; isBusy = false; Changed(); }
    }

    public void Cancel()
    {
        operation?.Cancel();
        check?.Cancel();
        trackCheck?.Cancel();
        if (isBusy) SetMessage(PlaylistNoticeLevel.Information, "Stopping. Waiting for the current request to finish...");
    }

    public void ReportOpenFailure() => SetMessage(PlaylistNoticeLevel.Warning,
        "Could not open the playlist. Check your default browser and try again.");

    private void ResetPlaylists()
    {
        Playlists.Clear();
        selectedPlaylist = new PlaylistChoice();
        Playlists.Add(selectedPlaylist);
        loadingPlaylists = false;
        playlistListError = null;
        trackCheck?.Cancel();
        checkingTracks = false;
        statisticsError = null;
        SetStatistics([]);
        OnPropertyChanged(nameof(SelectedPlaylist));
    }

    private void SetStatistics(IReadOnlyList<string> existing)
    {
        var seen = existing.ToHashSet(StringComparer.Ordinal);
        var count = 0;
        foreach (var id in SelectedDestination?.TrackIds ?? [])
        {
            var canonical = SelectedDestination!.Writer.NormalizePlaylistTrackId(id);
            if (seen.Add(canonical)) count++;
        }
        tracksToSave = count;
        duplicatesExcluded = (SelectedDestination?.TrackIds.Count ?? 0) - count;
    }

    private async Task CheckStatisticsAsync()
    {
        trackCheck?.Cancel();
        var selected = SelectedDestination;
        var choice = SelectedPlaylist;
        statisticsError = null;
        if (choice.Playlist == null || selected == null)
        {
            checkingTracks = false;
            SetStatistics([]);
            Changed();
            return;
        }
        using var current = trackCheck = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        checkingTracks = true;
        tracksToSave = duplicatesExcluded = null;
        Changed();
        try
        {
            var ids = await selected.Writer.GetPlaylistTrackIdsAsync(choice.Playlist.Id, current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (current.IsCancellationRequested || !ReferenceEquals(trackCheck, current) || !ReferenceEquals(SelectedDestination, selected)
                || !ReferenceEquals(SelectedPlaylist, choice) || !CanEdit) return;
            SetStatistics(ids);
        }
        catch (Exception error)
        {
            if (ReferenceEquals(trackCheck, current) && ReferenceEquals(SelectedPlaylist, choice))
            {
                Serilog.Log.Warning("Playlist duplicate check stopped: {ExceptionType}", error.GetType().Name);
                statisticsError = "Could not check this playlist for duplicates. Refresh the list or select the playlist again.";
            }
        }
        finally
        {
            if (ReferenceEquals(trackCheck, current)) { trackCheck = null; checkingTracks = false; Changed(); }
        }
    }

    private void SetMessage(PlaylistNoticeLevel value, string text)
    {
        severity = value;
        message = text;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(Severity));
    }

    private void Changed()
    {
        foreach (var property in new[] { nameof(CanCreate), nameof(CanEdit), nameof(CanChooseSource), nameof(IsChecking),
            nameof(IsBusy), nameof(Summary), nameof(CloseText), nameof(Percent), nameof(IsIndeterminate), nameof(PlaylistUrl), nameof(Result),
            nameof(HasResult), nameof(DialogTitle), nameof(PrimaryText), nameof(CanUsePrimary), nameof(ResultName), nameof(ResultLabel), nameof(IsNewPlaylist),
            nameof(CanChoosePlaylist), nameof(CanRefreshPlaylists), nameof(HasSelectedPlaylist), nameof(CanOpenSelectedPlaylist),
            nameof(Message), nameof(Severity), nameof(NameMaxLength), nameof(NeedsSignIn), nameof(SignInSourceId) })
            OnPropertyChanged(property);
    }
}
