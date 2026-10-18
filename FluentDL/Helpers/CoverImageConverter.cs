using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Data;

namespace FluentDL.Helpers;

internal sealed class CoverImageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        if (value is ImageSource image) return image;
        var uri = Uri.TryCreate(value as string, UriKind.Absolute, out var location)
            ? location : new Uri("ms-appx:///Assets/Unloaded.jpg");
        return new BitmapImage(uri);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
