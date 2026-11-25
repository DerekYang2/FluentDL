using System.Collections.ObjectModel;
using System.Collections.Specialized;
using FluentDL.Core.Matching;
using FluentDL.ViewModels;

namespace FluentDL.Helpers;

/// <summary>What the Queue filter shows: rows whose title, artists or album contain every word of the text, from one
/// of the chosen sources. An empty filter shows everything.</summary>
internal sealed record QueueFilter(string Text, IReadOnlySet<string> Sources)
{
    public static readonly QueueFilter None = new("", new HashSet<string>());

    private readonly string[] words = Normalize(Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public bool IsEmpty => words.Length == 0 && Sources.Count == 0;

    /// <summary>The number shown on the Filter button's badge: one for the text and one for the source choice.</summary>
    public int ActiveCount => (words.Length > 0 ? 1 : 0) + (Sources.Count > 0 ? 1 : 0);

    public bool Matches(QueueObject row)
    {
        if (Sources.Count > 0 && !Sources.Contains(row.Source)) return false;
        if (words.Length == 0) return true;
        var haystack = Normalize($"{row.Title} {row.Artists} {row.AlbumName}");
        return words.All(haystack.Contains);
    }

    // Case, accents and Cyrillic spelling don't matter: "beyonce" finds "Beyoncé" and "kino" finds "Кино".
    internal static string Normalize(string? text) =>
        TextNormalization.TransliterateCyrillic(TextNormalization.RemoveDiacritics(text ?? "")).ToLowerInvariant();
}

/// <summary>
/// The rows of the queue that the filter shows, in queue order. The queue itself is never changed, so its order,
/// counters, duplicate checks and saving don't depend on the filter. While pinned (during a download or conversion),
/// rows don't appear or disappear because of the filter: a shown row stays shown when it's replaced, and rows added
/// in the meantime are shown. Unpinning applies the filter again.
/// </summary>
internal sealed class QueueFilterView : IDisposable
{
    private readonly ObservableCollection<QueueObject> source;
    private readonly HashSet<QueueObject> shown = new(ReferenceEqualityComparer.Instance);
    private QueueFilter filter = QueueFilter.None;
    private bool pinned;

    public QueueFilterView(ObservableCollection<QueueObject> source)
    {
        this.source = source;
        source.CollectionChanged += Source_CollectionChanged;
        Rebuild();
    }

    public ObservableCollection<QueueObject> Visible { get; } = new();
    public QueueFilter Filter => filter;

    public bool Pinned
    {
        get => pinned;
        set
        {
            if (pinned == value) return;
            pinned = value;
            if (!pinned) Rebuild();
        }
    }

    public void SetFilter(QueueFilter value)
    {
        filter = value;
        Rebuild();
    }

    public bool IsShown(QueueObject row) => shown.Contains(row);

    public void Dispose() => source.CollectionChanged -= Source_CollectionChanged;

    private void Rebuild()
    {
        shown.Clear();
        Visible.Clear();
        foreach (var row in source)
        {
            if (!filter.Matches(row)) continue;
            shown.Add(row);
            Visible.Add(row);
        }
    }

    private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add when args.NewItems != null:
                for (var i = 0; i < args.NewItems.Count; i++)
                {
                    var row = (QueueObject)args.NewItems[i]!;
                    if (pinned || filter.Matches(row)) Show(row, args.NewStartingIndex + i);
                }
                break;
            case NotifyCollectionChangedAction.Remove when args.OldItems != null:
                foreach (QueueObject row in args.OldItems) Hide(row);
                break;
            case NotifyCollectionChangedAction.Replace when args.OldItems is [QueueObject old] && args.NewItems is [QueueObject replacement]:
                if (shown.Contains(old) && (pinned || filter.Matches(replacement)))
                {
                    var at = Visible.IndexOf(old);
                    shown.Remove(old);
                    shown.Add(replacement);
                    Visible[at] = replacement;
                }
                else
                {
                    var wasShown = shown.Contains(old);
                    Hide(old);
                    if (!pinned && !wasShown && filter.Matches(replacement)) Show(replacement, args.NewStartingIndex);
                }
                break;
            default:
                // Reset (clear or restore), Move (not used) and multi-row replaces.
                Rebuild();
                break;
        }
    }

    private void Hide(QueueObject row)
    {
        if (shown.Remove(row)) Visible.Remove(row);
    }

    // Puts a row at the queue position it has in the source: after the nearest shown row before it.
    private void Show(QueueObject row, int sourceIndex)
    {
        if (!shown.Add(row)) return;
        if (sourceIndex < 0 || sourceIndex >= source.Count || !ReferenceEquals(source[sourceIndex], row))
        {
            sourceIndex = source.IndexOf(row);
        }
        if (sourceIndex == source.Count - 1)
        {
            Visible.Add(row);
            return;
        }
        for (var i = sourceIndex - 1; i >= 0; i--)
        {
            if (shown.Contains(source[i]))
            {
                Visible.Insert(Visible.IndexOf(source[i]) + 1, row);
                return;
            }
        }
        Visible.Insert(0, row);
    }
}
