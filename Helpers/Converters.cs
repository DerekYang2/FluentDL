using System;
using System.Drawing;
using System.Globalization;
using AngleSharp.Dom;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Color = Windows.UI.Color;

namespace FluentDL.Helpers;

internal class DateToYearConverter : IValueConverter
{
    // Converts YYYY-MM-DD to YYYY
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        if (value == null) return "";
        return value.ToString().Substring(0, 4);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal class DateVerboseConverter : IValueConverter
{
    // Converts YYYY-MM-DD to Month DD, YYYY
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        if (value == null) return "";
        return DateTime.Parse(value.ToString()).ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal class DurationConverter : IValueConverter
{
    // Converts seconds to H hr, M min, S sec
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        if (value == null) return "";
        int seconds = int.Parse(value.ToString());
        int sec = seconds % 60;
        seconds /= 60;
        int min = seconds % 60;
        seconds /= 60;
        int hr = seconds;
        return (hr > 0 ? hr + " hr, " : "") + (min > 0 ? min + " min, " : "") + sec + " sec";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal class VisibilityConverter : IValueConverter
{
    // Converts bool to Visibility
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (bool)value ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal class NullVisibilityConverter : IValueConverter
{
    // Converts imagelocation path to Visibility
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        return (value != null) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal class InverseNullVisibilityConverter : IValueConverter
{
    // Converts imagelocation path to Visibility (inverted)
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        return (value == null) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal class SourceToColorConverter : IValueConverter
{
    // Converts source to color
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        return ((string)value) switch
        {
            "spotify" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 213, 101)),
            "deezer" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 55, 250)),
            "qobuz" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0)),
            "youtube" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)), // Local source or anything else
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}