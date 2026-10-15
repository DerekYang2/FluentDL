using FluentDL.Models;

namespace FluentDL.Characterization.Tests.Infrastructure;

/// <summary>
/// The parts of a SongSearchObject that should stay the same between runs on the same day.
/// Rank (popularity) and AdditionalFields values (preview URLs carry expiring tokens) are left out;
/// AdditionalFields keys are kept so a missing or renamed field still shows up.
/// </summary>
internal sealed record TrackShape(
    string? Source,
    string? Id,
    string Title,
    string Artists,
    string AlbumName,
    string? Isrc,
    string Duration,
    string ReleaseDate,
    bool Explicit,
    string TrackPosition,
    string ImageLocation,
    string[] AdditionalFieldKeys)
{
    public static TrackShape? From(SongSearchObject? song) => song == null ? null : new(
        song.Source,
        song.Id,
        song.Title,
        song.Artists,
        song.AlbumName,
        song.Isrc,
        song.Duration,
        song.ReleaseDate,
        song.Explicit,
        song.TrackPosition,
        song.ImageLocation,
        song.AdditionalFields?.Keys.Order(StringComparer.Ordinal).ToArray() ?? []);
}

internal sealed record AlbumShape(TrackShape Album, int TracksCount, TrackShape?[]? TrackList)
{
    public static AlbumShape? From(AlbumSearchObject? album) => album == null ? null : new(
        TrackShape.From(album)!,
        album.TracksCount,
        album.TrackList?.Select(TrackShape.From).ToArray());
}

internal static class ShapeExtensions
{
    /// <summary>Albums keep their track list; everything else becomes a TrackShape.</summary>
    public static object? ToShape(this SongSearchObject? item) => item is AlbumSearchObject album
        ? AlbumShape.From(album)
        : TrackShape.From(item);

    public static object?[] ToShapes(this IEnumerable<SongSearchObject> items) => items.Select(ToShape).ToArray();
}

/// <summary>One item the source code reported, such as an info bar message or a conversion badge.</summary>
internal sealed record Reported(string Severity, string Message);

/// <summary>The items a search or link import added, or the exception it stopped with.</summary>
internal sealed record Collected(object?[] Items, string? Error);
