using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.ViewModels;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class PlaylistDialogTests
{
    [TestMethod]
    public void SyntheticPlaylistResponsesAreNotCompiledIntoTheNormalApp() =>
        Assert.IsNull(typeof(FluentDL.Views.CreatePlaylistDialog).Assembly.GetType("FluentDL.PlaylistDialogTestSupport"));

    [TestMethod]
    public async Task SourceSnapshotExcludesAlbumsAndOtherSourcesAndKeepsQueueOrder()
    {
        var writer = new FakeWriter();
        var first = Track("1", 1);
        var queue = new[] { Track("3", 3), new QueueObject(new AlbumSearchObject { Id = "album", Source = SourceIds.Tidal, TracksCount = 12 }),
            Track("2", 2), first, new QueueObject(new SongSearchObject { Id = "other", Source = SourceIds.Deezer }) };
        var model = new PlaylistDialogViewModel([writer], queue) { Name = " Playlist ", Description = " Description " };
        first.Id = "changed-after-opening";
        await model.CheckSignInAsync();
        Assert.IsTrue(model.CanCreate);
        StringAssert.Contains(model.Summary, "3 TIDAL tracks");
        StringAssert.Contains(model.Summary, "1 album excluded");

        await model.CreateAsync();

        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, writer.Request!.TrackIds.ToArray());
        Assert.AreEqual("Playlist", writer.Request.Name);
        Assert.AreEqual("Description", writer.Request.Description);
        Assert.AreEqual(PlaylistVisibility.Unlisted, writer.Request.Visibility);
        Assert.IsFalse(model.CanCreate, "the same dialog must not submit a second create");
        Assert.IsNotNull(model.PlaylistUrl);
        Assert.AreEqual(5, queue.Length, "export never clears or converts queue rows");
    }

    [TestMethod]
    public async Task AlbumsOnlyAndSignedOutCannotCreate()
    {
        var album = new QueueObject(new AlbumSearchObject { Id = "1", Source = SourceIds.Tidal, TracksCount = 10 });
        var albumsOnly = new PlaylistDialogViewModel([new FakeWriter()], [album]) { Name = "Test" };
        await albumsOnly.CheckSignInAsync();
        Assert.IsFalse(albumsOnly.CanCreate);
        StringAssert.Contains(albumsOnly.Message, "no individual TIDAL tracks");
        var signedOut = new PlaylistDialogViewModel([new FakeWriter { SignedIn = false }], [Track("1", 1)]) { Name = "Test" };
        await signedOut.CheckSignInAsync();
        Assert.IsFalse(signedOut.CanCreate);
        StringAssert.Contains(signedOut.Message, "Sign in to TIDAL");
    }

    [TestMethod]
    public async Task NameChangesImmediatelyControlCreationAndVisibilityIsExplicit()
    {
        var writer = new FakeWriter();
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1)]);
        await model.CheckSignInAsync();
        Assert.IsFalse(model.CanCreate);
        model.Name = "Name";
        Assert.IsTrue(model.CanCreate);
        model.Name = " ";
        Assert.IsFalse(model.CanCreate);
        model.Name = "Name";
        model.VisibilityIndex = 1;
        await model.CreateAsync();
        Assert.AreEqual(PlaylistVisibility.Public, writer.Request!.Visibility);
    }

    [TestMethod]
    public async Task BusyPreventsAnotherSubmissionAndCancellationKeepsThePartialLink()
    {
        var writer = new FakeWriter { WaitForCancellation = true };
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1)]) { Name = "Name" };
        await model.CheckSignInAsync();
        var task = model.CreateAsync();
        Assert.IsTrue(model.IsBusy);
        Assert.IsFalse(model.CanEdit);
        await model.CreateAsync();
        Assert.AreEqual(1, writer.Calls);
        model.Cancel();
        await task;
        Assert.IsFalse(model.IsBusy);
        Assert.IsNotNull(model.PlaylistUrl);
        Assert.AreEqual(PlaylistCreationStatus.Cancelled, model.Result!.Status);
        Assert.IsFalse(model.CanCreate);
    }

    [TestMethod]
    [DataRow(PlaylistCreationStatus.Completed)]
    [DataRow(PlaylistCreationStatus.Partial)]
    [DataRow(PlaylistCreationStatus.Uncertain)]
    public async Task FinalScreenKeepsTheClickableUrlAndReplacesTheUploadForm(PlaylistCreationStatus status)
    {
        var url = new Uri("https://tidal.com/browse/playlist/11111111-2222-3333-4444-555555555555");
        var writer = new FakeWriter { Reply = new(url, status == PlaylistCreationStatus.Completed ? 2 : 1, 2, status, "Check the playlist.") };
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1), Track("2", 2)]) { Name = " My playlist " };
        Assert.AreEqual("Upload", model.PrimaryText);
        await model.CheckSignInAsync();
        await model.CreateAsync();
        Assert.IsTrue(model.HasResult);
        Assert.AreEqual("My playlist", model.ResultName);
        Assert.AreEqual(url, model.PlaylistUrl);
        Assert.AreEqual("Open playlist", model.PrimaryText);
        Assert.IsTrue(model.CanUsePrimary);
        Assert.IsFalse(model.CanCreate, "Opening must not create another playlist.");
        Assert.AreEqual("Close", model.CloseText);
        Assert.AreEqual("Upload playlist", model.DialogTitle);
        Assert.AreEqual(status == PlaylistCreationStatus.Completed ? "Playlist created:" : "Playlist created, upload incomplete:", model.ResultLabel);
        Assert.IsFalse(model.CanEdit);
    }

    [TestMethod]
    public async Task UnconfirmedCreationDoesNotInventAPlaylistLink()
    {
        var writer = new FakeWriter { Reply = new(null, 0, 1, PlaylistCreationStatus.Uncertain, "Check TIDAL before trying again.") };
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1)]) { Name = "Name" };
        await model.CheckSignInAsync();
        await model.CreateAsync();
        Assert.IsTrue(model.HasResult);
        Assert.IsNull(model.PlaylistUrl);
        Assert.AreEqual("", model.PrimaryText);
        Assert.IsFalse(model.CanUsePrimary);
        StringAssert.Contains(model.Message, "Check TIDAL");
    }

    [TestMethod]
    public async Task FailedBrowserLaunchKeepsTheExistingPlaylistAndCanBeRetried()
    {
        var writer = new FakeWriter();
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1)]) { Name = "Name" };
        await model.CheckSignInAsync();
        await model.CreateAsync();
        model.ReportOpenFailure();
        Assert.IsTrue(model.CanUsePrimary);
        Assert.AreEqual("Open playlist", model.PrimaryText);
        Assert.IsFalse(model.CanCreate);
        Assert.IsNotNull(model.PlaylistUrl);
        Assert.AreEqual(1, writer.Calls);
        StringAssert.Contains(model.Message, "default browser");
    }

    [TestMethod]
    public async Task SwitchingSourcesResetsVisibilityAndSendsOnlyThatSourcesTracks()
    {
        var deezer = new FakeWriter { SourceId = SourceIds.Deezer, SupportedPlaylistVisibilities = [PlaylistVisibility.Private, PlaylistVisibility.Public] };
        var qobuz = new FakeWriter { SourceId = SourceIds.Qobuz, SupportedPlaylistVisibilities = [PlaylistVisibility.Private, PlaylistVisibility.Public] };
        var tidal = new FakeWriter();
        var queue = new[] { Track("1", 1), new QueueObject(new SongSearchObject { Id = "2", Source = SourceIds.Deezer, QueueCounter = 2 }),
            new QueueObject(new SongSearchObject { Id = "3", Source = SourceIds.Qobuz, QueueCounter = 3 }) };
        var model = new PlaylistDialogViewModel([deezer, qobuz, tidal], queue) { Name = "Name" };
        model.SelectedDestination = model.Destinations.Single(d => d.Writer == deezer);
        await model.CheckSignInAsync();
        Assert.AreEqual(PlaylistVisibility.Private, model.VisibilityOptions[0].Value);
        model.VisibilityIndex = 1;
        model.SelectedDestination = model.Destinations.Single(d => d.Writer == tidal);
        Assert.AreEqual(0, model.VisibilityIndex);
        Assert.AreEqual(PlaylistVisibility.Unlisted, model.VisibilityOptions[0].Value);
        model.SelectedDestination = model.Destinations.Single(d => d.Writer == qobuz);
        Assert.AreEqual(PlaylistVisibility.Private, model.VisibilityOptions[0].Value);
        model.VisibilityIndex = -1; // The view's transient clear must not erase the non-public default.
        Assert.AreEqual(0, model.VisibilityIndex);
        Assert.IsTrue(model.CanCreate);
        await model.CreateAsync();
        CollectionAssert.AreEqual(new[] { "3" }, qobuz.Request!.TrackIds.ToArray());
        Assert.AreEqual(PlaylistVisibility.Private, qobuz.Request.Visibility);
        Assert.AreEqual(0, deezer.Calls);
        Assert.AreEqual(0, tidal.Calls);
    }

    [TestMethod]
    public async Task DefaultSourcePrefersOneWithQueuedTracksAndHintsAreSourceSpecific()
    {
        var deezer = new FakeWriter { SourceId = SourceIds.Deezer, SignedIn = false, SupportedPlaylistVisibilities = [PlaylistVisibility.Private, PlaylistVisibility.Public] };
        var qobuz = new FakeWriter { SourceId = SourceIds.Qobuz, SignedIn = false, SupportedPlaylistVisibilities = [PlaylistVisibility.Private, PlaylistVisibility.Public] };
        var model = new PlaylistDialogViewModel([deezer, qobuz, new FakeWriter()], [new QueueObject(new SongSearchObject { Id = "1", Source = SourceIds.Qobuz })]);
        Assert.AreSame(qobuz, model.SelectedDestination!.Writer);
        await model.CheckSignInAsync();
        StringAssert.Contains(model.Message, "Qobuz");
        Assert.IsFalse(model.Message.Contains("TIDAL"));
    }

    [TestMethod]
    public async Task SlowSignInCheckForThePreviousSourceCannotOverwriteTheNewOne()
    {
        var delayed = new TaskCompletionSource<bool>();
        var old = new FakeWriter { SourceId = SourceIds.Deezer, Check = _ => delayed.Task };
        var current = new FakeWriter { SourceId = SourceIds.Qobuz };
        var model = new PlaylistDialogViewModel([old, current], [new QueueObject(new SongSearchObject { Id = "1", Source = SourceIds.Qobuz })]) { Name = "Name" };
        model.SelectedDestination = model.Destinations[0];
        model.SelectedDestination = model.Destinations[1];
        delayed.SetResult(false);
        await Task.Yield();
        Assert.AreSame(current, model.SelectedDestination!.Writer);
        Assert.IsTrue(model.CanCreate);
        StringAssert.Contains(model.Message, "Ready");
    }

    private static QueueObject Track(string id, int order) => new(new SongSearchObject { Source = SourceIds.Tidal, Id = id, QueueCounter = order });

    [TestMethod]
    public async Task ExistingSelectionHidesCreationFieldsAndDoesNotRequireAName()
    {
        var target = new OwnedPlaylist("existing", "My existing playlist", new Uri("https://tidal.com/browse/playlist/existing"));
        var writer = new FakeWriter { Owned = [target], OwnedIds = ["1"], Reply = new(target.Url, 1, 2, PlaylistCreationStatus.Completed, Skipped: 1) };
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1), Track("2", 2)]);
        await model.CheckSignInAsync();
        Assert.IsTrue(model.IsNewPlaylist);
        Assert.IsFalse(model.CanCreate);
        model.SelectedPlaylist = model.Playlists[1];
        Assert.IsFalse(model.IsNewPlaylist);
        Assert.IsTrue(model.CanCreate);
        Assert.AreEqual("Add to playlist", model.PrimaryText);
        await model.CreateAsync();
        Assert.AreEqual(target, writer.Request!.Target);
        Assert.AreEqual("My existing playlist", model.ResultName);
        Assert.AreEqual("Upload playlist", model.DialogTitle);
        Assert.AreEqual("Playlist updated:", model.ResultLabel);
        StringAssert.Contains(model.Message, "Skipped 1 duplicate");
        model.SelectedPlaylist = model.Playlists[0];
        Assert.AreEqual(target, model.SelectedPlaylist.Playlist, "The destination cannot change after submission.");
    }

    [TestMethod]
    public async Task ListingFailureStillAllowsNewPlaylistCreation()
    {
        var model = new PlaylistDialogViewModel([new FakeWriter { List = _ => throw new HttpRequestException() }], [Track("1", 1)]) { Name = "Name" };
        await model.CheckSignInAsync();
        Assert.IsTrue(model.CanCreate);
        Assert.IsTrue(model.IsNewPlaylist);
        StringAssert.Contains(model.Message, "Could not load");
    }

    [TestMethod]
    public async Task SlowListingCannotReplaceTheNewSourcesChoices()
    {
        var delayed = new TaskCompletionSource<IReadOnlyList<OwnedPlaylist>>();
        var old = new FakeWriter { SourceId = SourceIds.Deezer, List = _ => delayed.Task };
        var target = new OwnedPlaylist("2", "Qobuz owned", new Uri("https://play.qobuz.com/playlist/2"));
        var current = new FakeWriter { SourceId = SourceIds.Qobuz, Owned = [target] };
        var model = new PlaylistDialogViewModel([old, current], [new QueueObject(new SongSearchObject { Id = "1", Source = SourceIds.Deezer })]);
        var loading = model.CheckSignInAsync();
        model.SelectedDestination = model.Destinations[1];
        delayed.SetResult([new("1", "Old account", new Uri("https://www.deezer.com/playlist/1"))]);
        await loading;
        Assert.AreEqual(2, model.Playlists.Count);
        Assert.AreEqual(target, model.Playlists[1].Playlist);
        Assert.IsTrue(model.IsNewPlaylist);
    }

    [TestMethod]
    public async Task StatisticsUseSeparateLinesAndCountQueuedAndExistingDuplicates()
    {
        var target = new OwnedPlaylist("existing", "Existing", new Uri("https://tidal.com/browse/playlist/existing"));
        var writer = new FakeWriter { Owned = [target], OwnedIds = ["1"] };
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1), Track("2", 2), Track("2", 3),
            new QueueObject(new AlbumSearchObject { Id = "album", Source = SourceIds.Tidal, TracksCount = 5 })]);
        await model.CheckSignInAsync();
        Assert.AreEqual("2 TIDAL tracks will be saved\n1 album excluded\n1 duplicate excluded", model.Summary);
        model.SelectedPlaylist = model.Playlists[1];
        Assert.AreEqual("1 TIDAL track will be saved\n1 album excluded\n2 duplicates excluded", model.Summary);
        Assert.IsFalse(model.Message.Contains("All queue status tabs", StringComparison.Ordinal));
        Assert.IsFalse(model.Message.Contains("No matching", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FailedDuplicateCheckBlocksAppendUntilAnotherTargetIsChosen()
    {
        var writer = new FakeWriter { Owned = [new("existing", "Existing", new Uri("https://tidal.com/browse/playlist/existing"))],
            ReadIds = (_, _) => throw new HttpRequestException() };
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1)]) { Name = "New name" };
        await model.CheckSignInAsync();
        model.SelectedPlaylist = model.Playlists[1];
        Assert.IsFalse(model.CanCreate);
        StringAssert.Contains(model.Summary, "Duplicates excluded: unavailable");
        StringAssert.Contains(model.Message, "Could not check this playlist");
        model.SelectedPlaylist = model.Playlists[0];
        Assert.IsTrue(model.CanCreate);
        Assert.AreEqual("1 TIDAL track will be saved\n0 albums excluded\n0 duplicates excluded", model.Summary);
    }

    [TestMethod]
    public async Task LateDuplicatePreviewCannotOverwriteTheNewPlaylistStatistics()
    {
        var delayed = new TaskCompletionSource<IReadOnlyList<string>>();
        var writer = new FakeWriter { Owned = [new("existing", "Existing", new Uri("https://tidal.com/browse/playlist/existing"))],
            ReadIds = (_, _) => delayed.Task };
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1), Track("2", 2)]) { Name = "New playlist" };
        await model.CheckSignInAsync();
        model.SelectedPlaylist = model.Playlists[1];
        Assert.IsFalse(model.CanCreate);
        StringAssert.Contains(model.Summary, "checking...");
        model.SelectedPlaylist = model.Playlists[0];
        delayed.SetResult(["1", "2"]);
        await Task.Yield();
        Assert.IsTrue(model.CanCreate);
        Assert.AreEqual("2 TIDAL tracks will be saved\n0 albums excluded\n0 duplicates excluded", model.Summary);
    }

    [TestMethod]
    public async Task InlineOpenIsAvailableOnlyForAnExistingEditableSelection()
    {
        var target = new OwnedPlaylist("existing", "Existing", new Uri("https://tidal.com/browse/playlist/existing"));
        var writer = new FakeWriter { Owned = [target] };
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1)]);
        await model.CheckSignInAsync();
        Assert.AreEqual("Create a new playlist", model.Playlists[0].DisplayName);
        Assert.IsFalse(model.HasSelectedPlaylist);
        Assert.IsFalse(model.CanOpenSelectedPlaylist);
        model.SelectedPlaylist = model.Playlists[1];
        Assert.IsTrue(model.HasSelectedPlaylist);
        Assert.IsTrue(model.CanOpenSelectedPlaylist);
        Assert.AreEqual(target.Url, model.SelectedPlaylist.Playlist!.Url);
        Assert.AreEqual(0, writer.Calls);
        model.SelectedPlaylist = model.Playlists[0];
        Assert.IsFalse(model.CanOpenSelectedPlaylist);
    }

    [TestMethod]
    public async Task UploadUsesAnExistingUserSessionWithoutOpeningBrowserConsent()
    {
        var authorized = true;
        var lists = 0;
        var writer = new AuthorizingWriter
        {
            SourceId = SourceIds.Spotify,
            Check = _ => Task.FromResult(authorized),
            Authorize = _ => throw new AssertFailedException("Consent belongs in Settings."),
            List = _ =>
            {
                Assert.IsTrue(authorized, "Listing owned playlists needs user consent first.");
                lists++;
                return Task.FromResult<IReadOnlyList<OwnedPlaylist>>([]);
            },
        };
        var model = new PlaylistDialogViewModel([writer], [new QueueObject(new SongSearchObject { Id = "spotify-track", Source = SourceIds.Spotify })]) { Name = "Test" };
        await model.CheckSignInAsync();
        Assert.IsTrue(model.CanCreate);
        Assert.AreEqual(0, writer.AuthorizationCalls);
        Assert.AreEqual(1, lists);
        Assert.AreEqual(0, writer.Calls, "Authorization must not create a playlist.");
        await model.CheckSignInAsync();
        Assert.AreEqual(0, writer.AuthorizationCalls, "Readiness checks must not open a consent prompt.");
        Assert.AreEqual(2, lists);
    }

    [TestMethod]
    [DataRow(SourceIds.Spotify)]
    [DataRow(SourceIds.Tidal)]
    public async Task SignedOutDestinationsOfferSettingsInsteadOfOpeningBrowserConsent(string source)
    {
        var writer = new AuthorizingWriter { SourceId = source, Check = _ => Task.FromResult(false),
            Authorize = _ => throw new AssertFailedException("Never open consent from Upload playlist."),
            List = _ => throw new AssertFailedException("Do not list playlists without authorization.") };
        var model = new PlaylistDialogViewModel([writer], [new QueueObject(new SongSearchObject { Id = "1", Source = source })]) { Name = "Test" };
        await model.CheckSignInAsync();
        await model.CreateAsync();
        Assert.IsFalse(model.CanCreate);
        Assert.AreEqual(0, writer.AuthorizationCalls);
        Assert.IsTrue(model.NeedsSignIn);
        Assert.AreEqual(source, model.SignInSourceId);
        Assert.AreEqual(0, writer.Calls);
        Assert.IsFalse(model.HasResult);
    }

    [TestMethod]
    public async Task EmptyQueueDoesNotTriggerUserConsent()
    {
        var writer = new AuthorizingWriter { SignedIn = false };
        var model = new PlaylistDialogViewModel([writer], []);
        await model.CheckSignInAsync();
        Assert.AreEqual(0, writer.AuthorizationCalls);
        Assert.IsFalse(model.CanCreate);
    }

    [TestMethod]
    public async Task ClosingTheDialogCancelsTheReadinessCheckWithoutOpeningBrowserConsent()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var writer = new AuthorizingWriter
        {
            SignedIn = false,
            Check = async token =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelled = true; throw; }
                return true;
            },
        };
        var model = new PlaylistDialogViewModel([writer], [Track("1", 1)]) { Name = "Test" };
        var pending = model.CheckSignInAsync();
        await started.Task;
        Assert.IsFalse(model.CanCreate);
        model.Cancel();
        await pending;
        Assert.IsTrue(cancelled);
        Assert.IsFalse(model.IsChecking);
        Assert.IsFalse(model.CanCreate);
        Assert.AreEqual(0, writer.Calls);
        Assert.AreEqual(0, writer.AuthorizationCalls);
    }

    private sealed class AuthorizingWriter : FakeWriter, IPlaylistAuthorization
    {
        public Func<CancellationToken, Task<bool>> Authorize { get; init; } = _ => Task.FromResult(false);
        public int AuthorizationCalls { get; private set; }
        public Task<bool> AuthorizePlaylistAsync(CancellationToken token)
        {
            AuthorizationCalls++;
            return Authorize(token);
        }
    }

    private class FakeWriter : IPlaylistWriter
    {
        public string SourceId { get; init; } = SourceIds.Tidal;
        public SourceDescriptor Descriptor => SourceCatalog.Find(SourceId)!;
        public IReadOnlyList<PlaylistVisibility> SupportedPlaylistVisibilities { get; init; } = [PlaylistVisibility.Unlisted, PlaylistVisibility.Public];
        public string PlaylistSignInHint => $"Sign in to {Descriptor.DisplayName} in Settings first.";
        public int PlaylistNameMaxLength => SourceId == SourceIds.Deezer ? 50 : 250;
        public Func<CancellationToken, Task<bool>>? Check { get; init; }
        public bool SignedIn { get; init; } = true;
        public bool WaitForCancellation { get; init; }
        public PlaylistCreationResult? Reply { get; init; }
        public PlaylistCreationRequest? Request { get; private set; }
        public int Calls { get; private set; }
        public Task<bool> CanCreatePlaylistAsync(CancellationToken token) => Check?.Invoke(token) ?? Task.FromResult(SignedIn);
        public IReadOnlyList<OwnedPlaylist> Owned { get; init; } = [];
        public Func<CancellationToken, Task<IReadOnlyList<OwnedPlaylist>>>? List { get; init; }
        public Task<IReadOnlyList<OwnedPlaylist>> GetOwnedPlaylistsAsync(CancellationToken token) => List?.Invoke(token) ?? Task.FromResult(Owned);
        public IReadOnlyList<string> OwnedIds { get; init; } = [];
        public Func<string, CancellationToken, Task<IReadOnlyList<string>>>? ReadIds { get; init; }
        public Task<IReadOnlyList<string>> GetPlaylistTrackIdsAsync(string id, CancellationToken token) => ReadIds?.Invoke(id, token) ?? Task.FromResult(OwnedIds);
        public async Task<PlaylistCreationResult> CreatePlaylistAsync(PlaylistCreationRequest request, IProgress<int>? progress, CancellationToken token)
        {
            Calls++;
            Request = request;
            if (WaitForCancellation)
            {
                try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { }
                return new(new Uri("https://tidal.com/browse/playlist/test"), 0, request.TrackIds.Count, PlaylistCreationStatus.Cancelled, "Stopped.");
            }
            return Reply ?? new(new Uri("https://tidal.com/browse/playlist/test"), request.TrackIds.Count, request.TrackIds.Count, PlaylistCreationStatus.Completed);
        }
    }
}
