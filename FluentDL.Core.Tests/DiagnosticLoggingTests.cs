using FluentDL.Core.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace FluentDL.Core.Tests;

[TestClass]
public class DiagnosticLoggingTests
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FluentDL.Logging.Tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static string ReadLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [TestMethod]
    public void CreateLogger_WritesImportantEventsImmediately_AndSwitchesVerbosityForBothApis()
    {
        // Arrange
        var level = new LoggingLevelSwitch(LogEventLevel.Information);
        using var logger = DiagnosticLogging.CreateLogger(_directory, level);
        using var factory = LoggerFactory.Create(builder => builder.AddSerilog(logger, dispose: false));
        var injectedLogger = factory.CreateLogger("FluentDL.Example");

        // Act
        logger.Debug("hidden direct debug");
        injectedLogger.LogDebug("hidden injected debug");
        logger.Information("important information");
        injectedLogger.LogError("important failure");
        level.MinimumLevel = LogEventLevel.Debug;
        logger.Debug("visible direct debug");
        injectedLogger.LogDebug("visible injected debug");
        level.MinimumLevel = LogEventLevel.Information;
        injectedLogger.LogDebug("hidden after disabling");
        var text = ReadLog(Directory.GetFiles(_directory, "*.log").Single());

        // Assert
        StringAssert.Contains(text, "important information");
        StringAssert.Contains(text, "important failure");
        StringAssert.Contains(text, "visible direct debug");
        StringAssert.Contains(text, "visible injected debug");
        Assert.IsFalse(text.Contains("hidden", StringComparison.Ordinal));
        StringAssert.Contains(text, "session:");
    }

    [TestMethod]
    public async Task CreateLogger_PreservesOperationScopeAcrossBackgroundWork()
    {
        // Arrange
        using var logger = DiagnosticLogging.CreateLogger(_directory, new LoggingLevelSwitch());

        // Act
        using (LogContext.PushProperty("OperationId", "test-operation"))
        {
            await Task.Run(() => logger.Information("background work"));
        }
        var text = ReadLog(Directory.GetFiles(_directory, "*.log").Single());

        // Assert
        StringAssert.Contains(text, "operation:test-operation");
        StringAssert.Contains(text, "background work");
    }

    [DataTestMethod]
    [DataRow("System.Net.Http.HttpClient.TokenClient.LogicalHandler")]
    [DataRow("Microsoft.Extensions.Http.DefaultHttpClientFactory")]
    public void CreateLogger_ExcludesHttpDiagnosticsEvenInVerboseMode(string category)
    {
        // Arrange
        using var logger = DiagnosticLogging.CreateLogger(_directory, new LoggingLevelSwitch(LogEventLevel.Debug));

        // Act
        logger.ForContext("SourceContext", category).Error("https://example.invalid?token=SECRET");
        logger.Information("safe diagnostic");
        var text = ReadLog(Directory.GetFiles(_directory, "*.log").Single());

        // Assert
        StringAssert.Contains(text, "safe diagnostic");
        Assert.IsFalse(text.Contains("SECRET", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreateLogger_RollsAtConfiguredSize_AndKeepsTenFilesWithoutRemovingOtherFiles()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        for (var day = 1; day <= 12; day++)
        {
            File.WriteAllText(Path.Combine(_directory, $"fluentdl-200001{day:00}.log"), "old log");
        }
        var unrelated = Path.Combine(_directory, "keep.txt");
        File.WriteAllText(unrelated, "keep");
        using var logger = DiagnosticLogging.CreateLogger(_directory, new LoggingLevelSwitch());
        var payload = new string('x', 1024 * 1024);

        // Act
        for (var entry = 0; entry < 12; entry++) logger.Information("{Payload}", payload);
        logger.Information("final marker");
        var files = Directory.GetFiles(_directory, "*.log").Select(path => new FileInfo(path)).ToArray();

        // Assert
        Assert.AreEqual(DiagnosticLogging.RetainedFileCountLimit, files.Length);
        Assert.IsTrue(files.Count(file => file.Length > 1024 * 1024) >= 2, "The 5 MB size limit must cause rolling.");
        Assert.IsTrue(files.All(file => file.Length < DiagnosticLogging.FileSizeLimitBytes + payload.Length + 1024));
        Assert.IsTrue(files.Any(file => ReadLog(file.FullName).Contains("final marker", StringComparison.Ordinal)));
        Assert.AreEqual("keep", File.ReadAllText(unrelated));
        Assert.IsFalse(Directory.GetFiles(_directory, "*.tmp").Any());
    }

    [TestMethod]
    public void CreateLogger_AllowsConcurrentAppInstances()
    {
        // Arrange
        using var first = DiagnosticLogging.CreateLogger(_directory, new LoggingLevelSwitch());
        using var second = DiagnosticLogging.CreateLogger(_directory, new LoggingLevelSwitch());

        // Act
        first.Warning("first instance");
        second.Warning("second instance");
        var text = ReadLog(Directory.GetFiles(_directory, "*.log").Single());

        // Assert
        StringAssert.Contains(text, "first instance");
        StringAssert.Contains(text, "second instance");
    }

    [TestMethod]
    public void CreateLogger_PreservesFatalEventAndExceptionWhenDisposed()
    {
        // Arrange
        var logger = DiagnosticLogging.CreateLogger(_directory, new LoggingLevelSwitch());
        var failure = new InvalidOperationException("SECRET", new IOException("SECRET"));

        // Act
        logger.Fatal(failure, "Unhandled UI exception; application terminating");
        logger.Dispose();
        var text = File.ReadAllText(Directory.GetFiles(_directory, "*.log").Single());

        // Assert
        StringAssert.Contains(text, "[FTL]");
        StringAssert.Contains(text, "Unhandled UI exception");
        StringAssert.Contains(text, "System.InvalidOperationException");
        StringAssert.Contains(text, "System.IO.IOException");
        Assert.IsFalse(text.Contains("SECRET", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreateLogger_RejectsAnUnwritableDirectory()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        var blocked = Path.Combine(_directory, "file-not-directory");
        File.WriteAllText(blocked, "occupied");

        // Act
        var error = Assert.ThrowsException<IOException>(() =>
            DiagnosticLogging.CreateLogger(blocked, new LoggingLevelSwitch()));

        // Assert
        Assert.IsNotNull(error);
        Assert.AreEqual("occupied", File.ReadAllText(blocked));
    }

    [TestMethod]
    public void CreateLogger_RejectsInvalidArguments()
    {
        // Arrange
        var level = new LoggingLevelSwitch();

        // Act
        var directoryError = Assert.ThrowsException<ArgumentException>(() => DiagnosticLogging.CreateLogger(" ", level));
        var levelError = Assert.ThrowsException<ArgumentNullException>(() => DiagnosticLogging.CreateLogger(_directory, null!));

        // Assert
        Assert.AreEqual("directory", directoryError.ParamName);
        Assert.AreEqual("levelSwitch", levelError.ParamName);
    }
}
