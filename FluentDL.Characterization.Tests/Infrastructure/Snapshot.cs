using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDL.Characterization.Tests.Infrastructure;

/// <summary>
/// Compares a value against a JSON baseline in the project's Baselines folder.
/// Set FLUENTDL_RECORD=1 to write the baselines instead of comparing. Set FLUENTDL_BASELINES to a folder to use
/// baselines from somewhere else, such as fresh ones that tools/live-tests/Run-LiveTests.ps1 -Against just recorded
/// from an older commit.
/// </summary>
internal static class Snapshot
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool IsRecording => Environment.GetEnvironmentVariable("FLUENTDL_RECORD") == "1";

    /// <summary>
    /// For live calls. Produces the value up to <paramref name="attempts"/> times and passes on the first match,
    /// because the services occasionally fail or reorder results for one request. A real regression differs on
    /// every attempt and still fails. Recording takes the first attempt.
    /// </summary>
    public static async Task MatchLive(string name, Func<Task<object?>> produce, int attempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            var value = await produce();
            if (IsRecording || attempt == attempts || Matches(name, value))
            {
                Match(name, value);
                return;
            }
        }
    }

    private static bool Matches(string name, object? value)
    {
        var path = Path.Combine(BaselineDirectory, name + ".json");
        return File.Exists(path) && File.ReadAllText(path).ReplaceLineEndings("\n") == Serialize(value);
    }

    private static string Serialize(object? value) => JsonSerializer.Serialize(value, Options).ReplaceLineEndings("\n") + "\n";

    public static void Match(string name, object? value)
    {
        var json = Serialize(value);
        var path = Path.Combine(BaselineDirectory, name + ".json");
        var actualPath = Path.Combine(BaselineDirectory, name + ".actual.json");

        if (IsRecording)
        {
            Directory.CreateDirectory(BaselineDirectory);
            File.WriteAllText(path, json);
            if (File.Exists(actualPath)) File.Delete(actualPath);
            return;
        }

        if (!File.Exists(path))
        {
            File.WriteAllText(actualPath, json);
            Assert.Fail($"No baseline for '{name}'. Run with FLUENTDL_RECORD=1 to record it. Output saved to {actualPath}");
        }

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (expected == json)
        {
            if (File.Exists(actualPath)) File.Delete(actualPath);
            return;
        }

        File.WriteAllText(actualPath, json);
        Assert.Fail($"'{name}' differs from its baseline. Compare {path} with {actualPath}.\n{FirstDifference(expected, json)}");
    }

    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var el = i < e.Length ? e[i] : "<end>";
            var al = i < a.Length ? a[i] : "<end>";
            if (el != al) return $"First difference at line {i + 1}:\n  baseline: {el}\n  actual:   {al}";
        }
        return string.Empty;
    }

    private static string BaselineDirectory { get; } =
        Environment.GetEnvironmentVariable("FLUENTDL_BASELINES") is { Length: > 0 } dir ? dir : Path.Combine(FindProjectDirectory(), "Baselines");

    private static string FindProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FluentDL.Characterization.Tests.csproj")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the test project directory.");
    }
}
