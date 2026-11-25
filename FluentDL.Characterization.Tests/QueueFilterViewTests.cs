using System.Collections.ObjectModel;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.ViewModels;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QueueFilterViewTests
{
    private static QueueObject Row(string title, string source = SourceIds.Deezer, string artists = "Artist", string album = "Album") =>
        new(new SongSearchObject { Id = title, Title = title, Source = source, Artists = artists, AlbumName = album });

    private static QueueFilter Text(string text) => new(text, new HashSet<string>());
    private static QueueFilter From(params string[] sources) => new("", sources.ToHashSet());
    private static string[] Titles(QueueFilterView view) => view.Visible.Select(row => row.Title).ToArray();

    [TestMethod]
    public void TextMatchesEveryWordInTitleArtistsOrAlbumIgnoringCaseAccentsAndScript()
    {
        Assert.IsTrue(Text("beyonce halo").Matches(Row("Halo", artists: "Beyoncé")));
        Assert.IsTrue(Text("KINO").Matches(Row("Gruppa krovi", artists: "Кино")));
        Assert.IsTrue(Text("memories").Matches(Row("Get Lucky", album: "Random Access Memories")));
        Assert.IsFalse(Text("halo lucky").Matches(Row("Halo")));
        Assert.IsTrue(QueueFilter.None.IsEmpty);
        Assert.AreEqual(2, new QueueFilter("x", new HashSet<string> { SourceIds.Tidal }).ActiveCount);
        Assert.AreEqual(0, new QueueFilter("   ", new HashSet<string>()).ActiveCount);
    }

    [TestMethod]
    public void TheViewKeepsQueueOrderThroughAppendsMiddleInsertsAndRemovals()
    {
        var queue = new ObservableCollection<QueueObject> { Row("a"), Row("b", SourceIds.Tidal), Row("c") };
        using var view = new QueueFilterView(queue);
        view.SetFilter(From(SourceIds.Deezer));
        CollectionAssert.AreEqual(new[] { "a", "c" }, Titles(view));

        queue.Add(Row("d"));
        queue.Add(Row("e", SourceIds.Tidal));
        queue.Insert(2, Row("b2"));          // between the hidden "b" and "c", like an expanded album
        queue.Insert(0, Row("first"));
        CollectionAssert.AreEqual(new[] { "first", "a", "b2", "c", "d" }, Titles(view));

        queue.RemoveAt(queue.IndexOf(queue.First(r => r.Title == "c")));
        queue.Remove(queue.First(r => r.Title == "b"));  // hidden: no change
        CollectionAssert.AreEqual(new[] { "first", "a", "b2", "d" }, Titles(view));

        queue.Clear();
        Assert.AreEqual(0, view.Visible.Count);
    }

    [TestMethod]
    public void ReplacingARowAppliesTheFilterToTheReplacement()
    {
        var queue = new ObservableCollection<QueueObject> { Row("a"), Row("b"), Row("c", SourceIds.Tidal) };
        using var view = new QueueFilterView(queue);
        view.SetFilter(From(SourceIds.Deezer));

        queue[1] = Row("b-tidal", SourceIds.Tidal);   // a shown row converted away from the filter
        queue[2] = Row("c-deezer");                   // a hidden row converted into it
        CollectionAssert.AreEqual(new[] { "a", "c-deezer" }, Titles(view));
    }

    [TestMethod]
    public void WhilePinnedShownRowsStayShownAndTheFilterAppliesAgainAfterwards()
    {
        var queue = new ObservableCollection<QueueObject> { Row("a"), Row("b"), Row("hidden", SourceIds.Tidal) };
        using var view = new QueueFilterView(queue);
        view.SetFilter(From(SourceIds.Deezer));
        view.Pinned = true;

        queue[0] = Row("a-tidal", SourceIds.Tidal);   // converted mid-run: stays where it was
        queue[2] = Row("hidden-deezer");              // a row that wasn't shown doesn't appear mid-run
        queue.Insert(2, Row("album-track", SourceIds.Tidal)); // rows added mid-run are shown
        CollectionAssert.AreEqual(new[] { "a-tidal", "b", "album-track" }, Titles(view));

        view.Pinned = false;
        CollectionAssert.AreEqual(new[] { "b", "hidden-deezer" }, Titles(view));
    }

    [TestMethod]
    public void QueueReplacementThatDropsADuplicateUpdatesTheView()
    {
        // QueueTrackReplacement removes the row instead when its replacement is already in the queue.
        var duplicate = Row("same");
        var queue = new ObservableCollection<QueueObject> { duplicate, Row("x"), Row("y") };
        var keys = queue.Select(QueueTrackReplacement.KeyOf).ToHashSet();
        using var view = new QueueFilterView(queue);
        view.SetFilter(From(SourceIds.Deezer));

        QueueTrackReplacement.Apply(queue, keys, 1, Row("same"));
        Assert.AreEqual(2, queue.Count);
        CollectionAssert.AreEqual(queue.Select(r => r.Title).ToArray(), Titles(view));
    }
}
