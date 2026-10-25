using FluentDL.Models;
using FluentDL.ViewModels;

namespace FluentDL.Services;

internal static class QueueTrackReplacement
{
    internal sealed record Result(QueueObject Row, string? RemovedHash);

    internal static string KeyOf(SongSearchObject song) =>
        (QueueObject.IsAlbumItem(song) ? "album:" : "") + song.Source + song.Id;

    // Apply without awaiting so another conversion cannot observe a partially merged queue.
    internal static Result? Apply(IList<QueueObject> rows, ISet<string> keys, int index, QueueObject replacement)
    {
        if (index < 0 || index >= rows.Count) return null;
        var oldKey = KeyOf(rows[index]);
        var newKey = KeyOf(replacement);
        var keepIndex = index;
        for (var i = 0; i < index; i++)
        {
            if (KeyOf(rows[i]) != newKey) continue;
            keepIndex = i;
            break;
        }

        if (keepIndex == index) rows[index] = replacement;
        else rows.RemoveAt(index);
        var retained = rows[keepIndex];
        for (var i = rows.Count - 1; i > keepIndex; i--)
            if (KeyOf(rows[i]) == newKey) rows.RemoveAt(i);

        var oldKeyRemains = rows.Any(row => KeyOf(row) == oldKey);
        if (!oldKeyRemains) keys.Remove(oldKey);
        keys.Add(newKey);
        return new(retained, oldKeyRemains ? null : oldKey);
    }
}
