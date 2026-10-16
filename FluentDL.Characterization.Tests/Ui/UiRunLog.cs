namespace FluentDL.Characterization.Tests.Ui;

/// <summary>Checks only log bytes written during this app session. Never prints account data from a log.</summary>
internal sealed class UiRunLog
{
    private readonly string directory;
    private readonly Dictionary<string, long> offsets;

    public UiRunLog(string directory)
    {
        this.directory = directory;
        offsets = Files().ToDictionary(path => path, path => new FileInfo(path).Length);
    }

    private IEnumerable<string> Files() => Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.log") : [];

    public bool HasNewFatal()
    {
        foreach (var path in Files())
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var offset = offsets.GetValueOrDefault(path);
            file.Position = offset <= file.Length ? offset : 0;
            using var reader = new StreamReader(file);
            while (reader.ReadLine() is { } line)
                if (line.Contains("[FTL]", StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
