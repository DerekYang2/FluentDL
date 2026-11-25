using System.Collections.Concurrent;
using FluentDL.Models;
using FluentDL.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QueueConversionResultOrderTests
{
    [TestMethod]
    public void ResultsFollowQueuePositionsRatherThanCompletionOrderOrTitles()
    {
        var first = new SongSearchObject { Title = "Z first" };
        var last = new SongSearchObject { Title = "A last" };
        var results = new ConcurrentDictionary<SongSearchObject, (int Queue, int Track)>();
        results.TryAdd(last, (9, 0));
        results.TryAdd(first, (2, 0));

        CollectionAssert.AreEqual(new[] { first, last }, QueuePage.OrderedConversionResults(results).ToArray());
    }

    [TestMethod]
    public void DownloadedAlbumTracksStayInAlbumOrderAtTheirQueuePosition()
    {
        var first = new SongSearchObject();
        var albumFirst = new SongSearchObject();
        var albumLast = new SongSearchObject();
        var last = new SongSearchObject();
        var results = new Dictionary<SongSearchObject, (int Queue, int Track)>
        {
            [last] = (8, 0), [albumLast] = (3, 11), [first] = (1, 0), [albumFirst] = (3, 0),
        };

        CollectionAssert.AreEqual(new[] { first, albumFirst, albumLast, last },
            QueuePage.OrderedConversionResults(results).ToArray());
        Assert.AreEqual(0, QueuePage.OrderedConversionResults(
            new Dictionary<SongSearchObject, (int Queue, int Track)>()).Count);
    }

    // A downloaded row replaces the online row the download reported, so the summary can show the file's cover.
    [TestMethod]
    public void ADownloadedFileTakesTheReportedRowsTabAndPosition()
    {
        var online = new SongSearchObject { Title = "online" };
        var other = new SongSearchObject { Title = "other" };
        var local = new SongSearchObject { Title = "local" };
        var success = new ConcurrentDictionary<SongSearchObject, (int Queue, int Track)>();
        var warning = new ConcurrentDictionary<SongSearchObject, (int Queue, int Track)>();
        success[other] = (1, 0);
        warning[online] = (4, 2);

        Assert.IsTrue(QueuePage.ReplaceConversionResult([success, warning], online, local));

        Assert.IsFalse(warning.ContainsKey(online));
        Assert.AreEqual((4, 2), warning[local]);
        Assert.AreEqual(1, success.Count);
        Assert.IsFalse(QueuePage.ReplaceConversionResult([success, warning], new SongSearchObject(), local));
    }
}
