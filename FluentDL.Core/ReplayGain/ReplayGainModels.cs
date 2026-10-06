#nullable enable
namespace FluentDL.Core.ReplayGain;

public sealed record ReplayGainTrack(string Path, string? Album = null, string? AlbumArtist = null);

// SkipTaggedTracks leaves out tracks that already have every tag the scan would write. With album gain, an
// album is left out only if all of its tracks have them, because album gain needs the whole album measured.
public sealed record ReplayGainOptions(
    bool TrackGain = true,
    bool AlbumGain = true,
    bool SingleAlbum = false,
    double Offset = 0,
    bool PreventClipping = false,
    bool SkipTaggedTracks = false);

// Gains are in dB and peaks are linear, where 1.0 is full scale. Loudness is in LUFS and is
// negative infinity for a silent track. The clip flags report whether the written gain still
// pushes the peak past full scale; the adjusted flags report whether clipping protection lowered it.
// Existing holds the ReplayGain tags the file had before the scan, and is null if they couldn't be read.
public sealed record ReplayGainResult(
    string Path,
    double Loudness,
    double TrackGain,
    double TrackPeak,
    bool TrackClips,
    bool TrackAdjusted,
    double? AlbumGain,
    double? AlbumPeak,
    bool AlbumClips,
    bool AlbumAdjusted,
    ReplayGainTagValues? Existing = null);

public sealed record ReplayGainFailure(string Path, string Message);

// ReplayGain values already in a file's tags. A value the file doesn't have is null.
public sealed record ReplayGainTagValues(double? TrackGain, double? TrackPeak, double? AlbumGain, double? AlbumPeak);

public enum ReplayGainValue { TrackGain, TrackPeak, AlbumGain, AlbumPeak }

// A value that writing a result would add or change. Current is the file's value before the scan, or null if the
// file doesn't have it.
public sealed record ReplayGainChange(ReplayGainValue Value, double? Current, double New);

// Sent once Total is known, then each time a track finishes. Total leaves out skipped tracks. Track holds that
// track's own values, without album values, which only exist once the whole scan is done. Failure is set
// instead if the track couldn't be measured.
public sealed record ReplayGainProgress(int Completed, int Total, ReplayGainResult? Track, ReplayGainFailure? Failure);

// Skipped lists the paths SkipTaggedTracks left out.
public sealed record ReplayGainScan(IReadOnlyList<ReplayGainResult> Results, IReadOnlyList<ReplayGainFailure> Failures, IReadOnlyList<string> Skipped);
