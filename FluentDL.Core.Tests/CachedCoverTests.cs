using System.ComponentModel;
using FluentDL.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

/// <summary>
/// The cover image the app caches on a song (a WinUI BitmapImage, typed object since the models moved to Core). The
/// queue and Local Explorer keep one image per song so covers don't flicker while lists scroll; any object stands in
/// for the image here.
/// </summary>
[TestClass]
public class CachedCoverTests
{
    [TestMethod]
    public void KeepsTheSameImageAndNotifiesOnlyOnChange()
    {
        var song = new SongSearchObject { Source = "deezer", Id = "67238735" };
        var image = new object();
        var changes = 0;
        song.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SongSearchObject.LocalBitmapImage)) changes++; };

        song.LocalBitmapImage = image;
        song.LocalBitmapImage = image;

        Assert.AreSame(image, song.LocalBitmapImage);
        Assert.AreEqual(1, changes);
    }

    // Saved queues are JSON; the image must stay out of them, as it did when the property was a BitmapImage.
    [TestMethod]
    public void IsNotSavedWithTheQueue()
    {
        var song = new SongSearchObject { Source = "deezer", Id = "67238735", Title = "Get Lucky", LocalBitmapImage = new object() };

        var json = song.ToString();

        StringAssert.Contains(json, "\"Title\":\"Get Lucky\"");
        Assert.IsFalse(json.Contains(nameof(SongSearchObject.LocalBitmapImage)), json);
        Assert.IsFalse(json.Contains(nameof(SongSearchObject.CoverSource)), json);
    }

    [TestMethod]
    public void CoverSourcePrefersTheCachedImageAndFallsBackWhenItIsCleared()
    {
        var song = new SongSearchObject { Source = "local", Id = "test", ImageLocation = "https://example.test/cover.jpg" };
        var image = new object();
        var changes = 0;
        song.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(SongSearchObject.CoverSource)) changes++; };
        Assert.AreEqual(song.ImageLocation, song.CoverSource);
        song.LocalBitmapImage = image;
        Assert.AreSame(image, song.CoverSource);
        song.ImageLocation = "https://example.test/new.jpg";
        Assert.AreSame(image, song.CoverSource);
        song.LocalBitmapImage = null;
        Assert.AreEqual(song.ImageLocation, song.CoverSource);
        Assert.AreEqual(3, changes);
    }
}
