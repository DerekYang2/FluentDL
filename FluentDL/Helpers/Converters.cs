using ABI.Microsoft.UI.Xaml;
using AngleSharp.Dom;
using FluentDL.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.WindowsAppSDK.Runtime.Packages;
using System;
using System.Drawing;
using System.Globalization;
using Color = Windows.UI.Color;

namespace FluentDL.Helpers;

internal class DateToYearConverter : IValueConverter
{
    // Converts YYYY-MM-DD to YYYY
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        if (string.IsNullOrWhiteSpace((string?)value)) return "";
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
        if (string.IsNullOrWhiteSpace((string?)value)) return "";
        if (value.ToString().Length == 4) return value.ToString();
        return DateTime.Parse(value.ToString()).ToString("MMMM %d, yyyy", CultureInfo.InvariantCulture);
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
        int? seconds = null;
        if (value is string valstr)
        {
            if (string.IsNullOrWhiteSpace(valstr)) return "";

            if (int.TryParse(valstr, out int result))
            {
                seconds = result;
            }
        }
        else if (value is int valint)
        {
            seconds = valint;
        }

        if (seconds != null)
        {
            TimeSpan ts = TimeSpan.FromSeconds((int)seconds);
            return (ts.Hours > 0 ? ts.Hours + " hr " : "") + (ts.Minutes > 0 ? ts.Minutes + " min " : "") + ts.Seconds + " sec";
        }

        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal class DurationConverterShort : IValueConverter
{
    // Converts seconds to H hr, M min, S sec
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        int? seconds = null;
        if (value is string valstr)
        {
            if (string.IsNullOrWhiteSpace(valstr)) return "";

            if (int.TryParse(valstr, out int result))
            {
                seconds = result;
            }
        }
        else if (value is int valint)
        {
            seconds = valint;
        }

        if (seconds != null)
        {
            TimeSpan ts = TimeSpan.FromSeconds((int)seconds);
            if (ts.Hours > 0)
                return $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            else if (ts.Minutes > 0)
                return $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        return "";
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

internal class CollapsedConverter : IValueConverter
{
    // Converts bool to Visibility (inverted)
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (bool)value ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
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

internal class LocalSourceToVisibilityConverter : IValueConverter 
{
    // Converts source to Visibility
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        return ((string)value) switch
        {
            "local" => Microsoft.UI.Xaml.Visibility.Visible,
            _ => Microsoft.UI.Xaml.Visibility.Collapsed,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal class AlbumCountConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        if (value is AlbumSearchObject album)
        {
            string retStr = $"{(char)160}{(char)0x2022}{(char)160}{(char)0xfeff}{album.TracksCount} Track";
            if (album.TracksCount != 1) retStr += "s";
            return retStr;
        }
        else
        {
            return "";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}