using FluentDL.Models;
using FluentDL.ViewModels;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QueueArtworkStateTests
{
    [TestMethod]
    public void ArtworkStatusNotifiesOncePerChangeAndIsNotPersisted()
    {
        var row = new QueueObject(new SongSearchObject { Id = "1", Source = "deezer" });
        Assert.AreEqual(QueueArtworkState.Unavailable, row.ArtworkState);
        var changes = new List<string?>();
        row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        row.ArtworkState = QueueArtworkState.Loading;
        row.ArtworkState = QueueArtworkState.Loading;
        row.ArtworkState = QueueArtworkState.Ready;
        CollectionAssert.AreEqual(new[] { nameof(QueueObject.ArtworkState), nameof(QueueObject.ArtworkState) }, changes.ToArray());
        Assert.IsFalse(row.ToString().Contains(nameof(QueueObject.ArtworkState)));
    }

    [TestMethod]
    public void AnExistingBitmapStartsReadyWithoutWaitingForAnArtworkSave()
    {
        var cached = new object();
        var row = new QueueObject(new SongSearchObject { Id = "1", Source = "deezer", LocalBitmapImage = cached });
        Assert.AreEqual(QueueArtworkState.Ready, row.ArtworkState);
        Assert.AreSame(cached, row.LocalBitmapImage);
    }
}
