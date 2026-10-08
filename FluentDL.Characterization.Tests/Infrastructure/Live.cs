using System.Collections.ObjectModel;
using FluentDL.Models;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Characterization.Tests.Infrastructure;

internal static class Live
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Runs a call and returns its shaped result, or "throws X" so an exception is part of the baseline too.</summary>
    public static async Task<object?> Capture(Func<Task<object?>> call)
    {
        try
        {
            return await call().WaitAsync(CallTimeout);
        }
        catch (TimeoutException)
        {
            return "timed out";
        }
        catch (Exception e)
        {
            return $"throws {e.GetType().Name}";
        }
    }

    /// <summary>Calls a method that fills an ObservableCollection, the way the Search page does, and shapes the items.</summary>
    public static async Task<object?> Collect(Func<ObservableCollection<SongSearchObject>, Task> fill)
    {
        var items = new ObservableCollection<SongSearchObject>();
        object? error = null;
        try
        {
            await fill(items).WaitAsync(CallTimeout);
        }
        catch (TimeoutException)
        {
            error = "timed out";
        }
        catch (Exception e)
        {
            error = $"throws {e.GetType().Name}";
        }
        return new { Items = items.ToShapes(), Error = error };
    }

    /// <summary>Records every message a link import reports to the Search page's info bar.</summary>
    public sealed class StatusLog
    {
        public List<Reported> Messages { get; } = [];
        public Search.UrlStatusUpdateCallback Callback => (severity, message, _) => Messages.Add(new(SeverityName(severity), message));
    }

    /// <summary>Records the badges a conversion reports to the Queue page.</summary>
    public sealed class ConversionLog
    {
        public List<object> Calls { get; } = [];
        public ConversionUpdateCallback Callback => (severity, song, location) => Calls.Add(new { Severity = SeverityName(severity), Song = song.ToShape(), Location = location });
    }

    public static string SeverityName(InfoBarSeverity severity) => severity switch
    {
        InfoBarSeverity.Informational => "Informational",
        InfoBarSeverity.Success => "Success",
        InfoBarSeverity.Warning => "Warning",
        InfoBarSeverity.Error => "Error",
        _ => ((int)severity).ToString(),
    };
}
