namespace FluentDL.Contracts.Sources;

/// <summary>An action needs a user account. The UI can offer a provider-specific Settings shortcut.</summary>
public sealed class SourceSignInRequiredException(string sourceId, string message) : Exception(message)
{
    public string SourceId { get; } = sourceId;
}
