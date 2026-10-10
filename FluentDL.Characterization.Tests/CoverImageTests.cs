using FluentDL.Helpers;
using Microsoft.UI.Xaml.Media;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// SongSearchObject.LocalBitmapImage is typed object since the models moved to Core (Phase 7). The queue, the compact
/// queue, Local Explorer and the ReplayGain dialog bind it through ObjectToImageSourceConverter; the XAML compiler
/// rejects any of those bindings without the converter (checked by removing one). A real BitmapImage can't be created
/// in this unpackaged test host (Class not registered), so the converter's pass-through of the cached image is covered
/// by the UI tests that show the queue; this checks that anything else becomes no image. No network, no window.
/// </summary>
[TestClass]
public class CoverImageTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("ms-appx:///Assets/Unloaded.jpg")]
    public void AnythingButAnImageBecomesNoImage(object? value)
    {
        Assert.IsNull(new ObjectToImageSourceConverter().Convert(value, typeof(ImageSource), null!, ""));
    }
}
