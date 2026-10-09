using FluentDL.Core.Sources;

namespace FluentDL.Models;

/// <summary>
/// Reports how a track was converted or downloaded: Success, Warning (close match) or Error. Declared next to
/// SongSearchObject and moves to Core with it.
/// </summary>
public delegate void ConversionUpdateCallback(StatusLevel level, SongSearchObject song, string? location = null);
