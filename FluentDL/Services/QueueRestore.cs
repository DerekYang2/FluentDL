using FluentDL.Models;

namespace FluentDL.Services;

internal static class QueueRestore
{
    /// <summary>
    /// A saved local row's current metadata: the file is read again, so tag edits made after queueing it show up, and
    /// it keeps its saved position. Null when the file can't be read (an unplugged drive), so the saved row is used.
    /// </summary>
    internal static SongSearchObject? CurrentLocalFile(SongSearchObject saved, Func<string, SongSearchObject?> parseFile)
    {
        var current = parseFile(saved.Id);
        if (current != null) current.QueueCounter = saved.QueueCounter;
        return current;
    }
}
