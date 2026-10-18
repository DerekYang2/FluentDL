using System.Text.Json;
using FluentDL.Contracts.Sources;

namespace FluentDL.Services.Playlists;

internal sealed class PlaylistWriteRejected(string message, bool uncertain = false) : Exception(message)
{
    public bool Uncertain { get; } = uncertain;
}

internal sealed record PlaylistBatchConfirmation(int Added, bool Matches, string? Message = null, IReadOnlyList<string>? ConfirmedTrackIds = null);
internal sealed record PlaylistUploadTarget(Uri Url, IReadOnlyList<string> TrackIds);

/// <summary>Sequential new-playlist writes. Never retries mutations or removes a partial playlist.</summary>
internal static class PlaylistUpload
{
    internal static async Task<PlaylistCreationResult> RunAsync(string source, PlaylistCreationRequest request,
        IReadOnlyList<PlaylistVisibility> supported,
        Func<Action<Uri>, CancellationToken, Task> create,
        Func<IReadOnlyList<string>, IReadOnlyList<string>, CancellationToken, Task<PlaylistBatchConfirmation>> append,
        IProgress<int>? progress, CancellationToken token,
        Func<CancellationToken, Task<PlaylistUploadTarget>>? prepareExisting = null, int nameLimit = 250,
        Func<string, bool>? validTrackId = null, Func<string, string>? normalizeTrackId = null, int batchSize = 50)
    {
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        validTrackId ??= id => long.TryParse(id, out var number) && number > 0 && id.All(char.IsAsciiDigit);
        normalizeTrackId ??= id => long.Parse(id).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ids = request.TrackIds.ToArray();
        if ((request.Target == null && (request.Name.Trim().Length < 1 || request.Name.Trim().Length > nameLimit || request.Description.Trim().Length > 500
                || !supported.Contains(request.Visibility))) || ids.Length == 0
            || ids.Any(id => !validTrackId(id))
            || (request.Target != null && prepareExisting == null))
            return new(null, 0, ids.Length, PlaylistCreationStatus.Failed, $"Enter a name and include valid {source} tracks.");

        Uri? url = null;
        var added = 0;
        var inFlight = false;
        var skipped = 0;
        var notAdded = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            var expected = new List<string>();
            if (request.Target != null)
            {
                var existing = await prepareExisting!(token);
                url = existing.Url;
                expected.AddRange(existing.TrackIds);
            }
            var seen = expected.ToHashSet(StringComparer.Ordinal);
            var pending = ids.Select(normalizeTrackId).Where(id => seen.Add(id)).ToArray();
            skipped = ids.Length - pending.Length;
            if (request.Target == null)
            {
                inFlight = true;
                await create(created => url = created, token);
            }
            if (url == null) throw new JsonException("Creation did not return a playlist link.");
            inFlight = false;
            foreach (IReadOnlyList<string> batch in pending.Chunk(batchSize))
            {
                token.ThrowIfCancellationRequested();
                expected.AddRange(batch);
                inFlight = true;
                var confirmation = await append(batch, expected.ToArray(), token);
                if (confirmation.Added < 0 || confirmation.Added > batch.Count) throw new JsonException("Invalid acknowledgement count.");
                added += confirmation.Added;
                inFlight = false;
                progress?.Report(added);
                if (!confirmation.Matches) return new(url, added, ids.Length, PlaylistCreationStatus.Partial,
                    confirmation.Message ?? $"{source} skipped or changed tracks. Check the playlist; it has been kept.", skipped);
                if (confirmation.ConfirmedTrackIds is { } confirmed)
                {
                    notAdded += batch.Count - confirmation.Added;
                    expected.Clear();
                    expected.AddRange(confirmed);
                }
            }
            if (notAdded > 0)
                return new(url, added, ids.Length, PlaylistCreationStatus.Partial,
                    $"{source} did not add {notAdded} requested tracks. All remaining batches were processed. These omissions are separate from duplicates; the playlist has been kept.", skipped);
            return new(url, added, ids.Length, PlaylistCreationStatus.Completed, Skipped: skipped);
        }
        catch (PlaylistWriteRejected error)
        {
            return new(url, added, ids.Length, error.Uncertain ? PlaylistCreationStatus.Uncertain
                : url == null ? PlaylistCreationStatus.Failed : PlaylistCreationStatus.Partial, error.Message, skipped);
        }
        catch (OperationCanceledException)
        {
            return new(url, added, ids.Length, inFlight ? PlaylistCreationStatus.Uncertain : PlaylistCreationStatus.Cancelled,
                inFlight ? $"Stopped waiting. The last request may still finish. Check {source} before trying again."
                    : "Upload stopped. Any playlist already created has been kept.", skipped);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            Serilog.Log.Warning("{Source} playlist operation failed: {ExceptionType}", source, error.GetType().Name);
            return new(url, added, ids.Length, inFlight ? PlaylistCreationStatus.Uncertain
                : url == null ? PlaylistCreationStatus.Failed : PlaylistCreationStatus.Partial,
                $"The result could not be confirmed. Check {source} before trying again. Any created playlist has been kept.", skipped);
        }
    }

    internal static PlaylistBatchConfirmation Confirm(IReadOnlyList<string> actual, IReadOnlyList<string> expected, int batchSize)
    {
        var prefix = 0;
        while (prefix < Math.Min(actual.Count, expected.Count) && actual[prefix] == expected[prefix]) prefix++;
        return new(Math.Clamp(prefix - (expected.Count - batchSize), 0, batchSize), actual.SequenceEqual(expected));
    }

    internal static void EnsureUnchanged(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        if (!actual.SequenceEqual(expected))
            throw new PlaylistWriteRejected("The playlist changed during upload. No further tracks were sent. Refresh its contents before trying again.");
    }

    internal static PlaylistBatchConfirmation ConfirmAllowingOmissions(IReadOnlyList<string> actual, IReadOnlyList<string> expected, int batchSize)
    {
        var previousCount = expected.Count - batchSize;
        if (actual.Count < previousCount || !actual.Take(previousCount).SequenceEqual(expected.Take(previousCount)))
            return new(0, false, "The existing playlist contents changed. No further tracks were sent. Check the playlist before trying again.");

        var pending = expected.Skip(previousCount).ToHashSet(StringComparer.Ordinal);
        var appended = actual.Skip(previousCount).ToArray();
        var acknowledged = appended.Where(pending.Contains).Distinct(StringComparer.Ordinal).Count();
        var next = previousCount;
        foreach (var id in appended)
        {
            while (next < expected.Count && expected[next] != id) next++;
            if (next == expected.Count)
                return new(acknowledged, false, "The playlist contains unexpected or reordered tracks. No further tracks were sent. Check the playlist before trying again.");
            next++;
        }
        return new(appended.Length, true, ConfirmedTrackIds: actual.ToArray());
    }
}
