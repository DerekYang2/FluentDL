namespace FluentDL.Core.Sources;

/// <summary>
/// Severity of a message from a source. Same members and values as WinUI's InfoBarSeverity; the app maps one to the
/// other when it shows the message.
/// </summary>
public enum StatusLevel
{
    Informational,
    Success,
    Warning,
    Error,
}

/// <summary>Status messages while a link loads. The message is HTML. A duration of -1 keeps the message until the next one.</summary>
public delegate void UrlStatusUpdateCallback(StatusLevel level, string message, int duration = 2);

/// <summary>The result of signing in to a source, shown on the Settings page.</summary>
public delegate void AuthenticationCallback(StatusLevel level, string message);
