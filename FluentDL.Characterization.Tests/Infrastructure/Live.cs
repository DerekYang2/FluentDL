using System.Collections.ObjectModel;
using FluentDL.Core.Sources;
using FluentDL.Models;

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
    public static async Task<Collected> Collect(Func<ObservableCollection<SongSearchObject>, Task> fill)
    {
        var (items, error) = await CollectItems(fill);
        return new Collected(items.ToShapes(), error);
    }

    /// <summary>
    /// For a second search page, where the services reorder lower-ranked results from day to day. Pins the item count
    /// and whether the page repeats anything from the first page, which is the pagination behavior that matters.
    /// </summary>
    public static async Task<object?> CollectNextPage(Func<ObservableCollection<SongSearchObject>, Task> fill, Collected firstPage)
    {
        var firstIds = firstPage.Items.OfType<TrackShape>().Select(s => s.Id).ToHashSet();
        var (items, error) = await CollectItems(fill);
        return new { Count = items.Count, RepeatsFirstPage = items.Any(i => firstIds.Contains(i.Id)), Error = error };
    }

    /// <summary>
    /// For a search that ignores its limit (bug 17: Spotify official search reads every page). Pins the first
    /// <paramref name="requested"/> items exactly and whether more than that were loaded, but not the total, which
    /// changes with the catalog.
    /// </summary>
    public static async Task<object?> CollectHead(Func<ObservableCollection<SongSearchObject>, Task> fill, int requested)
    {
        var (items, error) = await CollectItems(fill);
        return new { Head = items.Take(requested).ToShapes(), LoadedMoreThanRequested = items.Count > requested, Error = error };
    }

    /// <summary>For lists that grow over time, such as a label's newest albums: pins the count and the field layout only.</summary>
    public static async Task<object?> CollectShapeOnly(Func<ObservableCollection<SongSearchObject>, Task> fill)
    {
        var (items, error) = await CollectItems(fill);
        return new
        {
            Count = items.Count,
            Sources = items.Select(i => i.Source).Distinct().ToArray(),
            AllHaveIdAndTitle = items.All(i => !string.IsNullOrEmpty(i.Id) && !string.IsNullOrEmpty(i.Title)),
            Types = items.Select(i => i.GetType().Name).Distinct().ToArray(),
            Error = error,
        };
    }

    private static async Task<(List<SongSearchObject> Items, string? Error)> CollectItems(Func<ObservableCollection<SongSearchObject>, Task> fill)
    {
        var items = new ObservableCollection<SongSearchObject>();
        string? error = null;
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
        return (items.ToList(), error);
    }

    /// <summary>Records every message a link import reports to the Search page's info bar.</summary>
    public sealed class StatusLog
    {
        public List<Reported> Messages { get; } = [];
        public UrlStatusUpdateCallback Callback => (level, message, _) => Messages.Add(new(SeverityName(level), message));
    }

    /// <summary>Records the badges a conversion reports to the Queue page.</summary>
    public sealed class ConversionLog
    {
        public List<object> Calls { get; } = [];
        public ConversionUpdateCallback Callback => (level, song, location) => Calls.Add(new { Severity = SeverityName(level), Song = song.ToShape(), Location = location });
    }

    public static string SeverityName(StatusLevel level) => level switch
    {
        StatusLevel.Informational => "Informational",
        StatusLevel.Success => "Success",
        StatusLevel.Warning => "Warning",
        StatusLevel.Error => "Error",
        _ => ((int)level).ToString(),
    };
}
