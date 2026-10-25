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
}
