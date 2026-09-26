using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace FluentDL.Core.Helpers;

public static class DiagnosticLogging
{
    public const string VerboseSettingKey = "verbose_logging";
    public const long FileSizeLimitBytes = 5 * 1024 * 1024;
    public const int RetainedFileCountLimit = 10;

    public static Logger CreateLogger(string directory, LoggingLevelSwitch levelSwitch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(levelSwitch);
        Directory.CreateDirectory(directory);

        // File sinks report I/O errors via SelfLog, so check access before claiming logging is available.
        using (new FileStream(Path.Combine(directory, $"write-check-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
        }

        var formatter = new DiagnosticLogFormatter(new MessageTemplateTextFormatter(
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] " +
            "[session:{SessionId} operation:{OperationId}] {Message:lj}{NewLine}"));

        return new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            // HttpClientFactory diagnostics can contain query strings with authentication tokens.
            .Filter.ByExcluding(e => e.Properties.TryGetValue("SourceContext", out var source)
                && source is ScalarValue { Value: string category }
                && (category.StartsWith("System.Net.Http", StringComparison.Ordinal)
                    || category.StartsWith("Microsoft.Extensions.Http", StringComparison.Ordinal)))
            .Enrich.FromLogContext()
            .Enrich.WithProperty("SessionId", Guid.NewGuid().ToString("N"))
            .WriteTo.File(formatter, Path.Combine(directory, "fluentdl-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCountLimit,
                buffered: false,
                shared: true)
            .CreateLogger();
    }
}
