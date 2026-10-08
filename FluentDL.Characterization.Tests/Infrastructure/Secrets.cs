namespace FluentDL.Characterization.Tests.Infrastructure;

/// <summary>
/// Reads credentials from the hurl.secrets file (NAME=VALUE lines) outside the repo.
/// Values are never printed. Tests that need a missing value end as Inconclusive.
/// </summary>
internal static class Secrets
{
    private static readonly Lazy<Dictionary<string, string>> Values = new(Load);

    public static string Path { get; } = Environment.GetEnvironmentVariable("FLUENTDL_SECRETS")
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fluentdl-secrets", "hurl.secrets");

    public static string? Get(string name) => Values.Value.TryGetValue(name, out var value) ? value : null;

    public static string Require(string name)
    {
        var value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            Assert.Inconclusive($"Secret '{name}' is not set in {Path}.");
        }
        return value!;
    }

    private static Dictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(Path)) return result;

        foreach (var line in File.ReadAllLines(Path))
        {
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            result[line[..index].Trim()] = line[(index + 1)..].Trim();
        }
        return result;
    }
}
