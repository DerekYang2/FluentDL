using System.Net;
using FluentDL.Core.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;

namespace FluentDL.Core.Tests;

[TestClass]
public class DiagnosticLogFormatterTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Format_DelegatesEventFormatting_AndOmitsUntrustedExceptionData(bool aggregate)
    {
        // Arrange
        var renderer = new Mock<ITextFormatter>(MockBehavior.Strict);
        using var output = new StringWriter();
        Exception failure;
        try
        {
            throw new HttpRequestException("SECRET response with https://example.invalid?token=SECRET",
                new IOException(@"SECRET C:\Users\private\download.flac"), HttpStatusCode.TooManyRequests);
        }
        catch (HttpRequestException ex)
        {
            failure = aggregate ? new AggregateException("SECRET", ex, new FormatException("SECRET")) : ex;
        }
        failure.Data["SECRET"] = "SECRET";
        using var logger = new LoggerConfiguration().CreateLogger();
        logger.BindMessageTemplate("Controlled failure", [], out var template, out var properties);
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Error, failure, template!, properties!);
        renderer.Setup(value => value.Format(logEvent, output)).Callback<LogEvent, TextWriter>((_, writer) => writer.WriteLine("Controlled failure"));
        var formatter = new DiagnosticLogFormatter(renderer.Object);

        // Act
        formatter.Format(logEvent, output);

        // Assert
        renderer.Verify(value => value.Format(logEvent, output), Times.Once);
        var text = output.ToString();
        StringAssert.Contains(text, "Controlled failure");
        StringAssert.Contains(text, "System.Net.Http.HttpRequestException");
        StringAssert.Contains(text, "System.IO.IOException");
        StringAssert.Contains(text, "HTTP status: 429");
        StringAssert.Contains(text, "HResult:");
        StringAssert.Contains(text, nameof(Format_DelegatesEventFormatting_AndOmitsUntrustedExceptionData));
        Assert.IsFalse(text.Contains("SECRET", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(@"C:\Users", StringComparison.Ordinal));
        if (aggregate) StringAssert.Contains(text, "System.FormatException");
    }

    [TestMethod]
    public void Format_HandlesAnEventWithoutAnException()
    {
        // Arrange
        var renderer = new Mock<ITextFormatter>();
        var formatter = new DiagnosticLogFormatter(renderer.Object);
        using var output = new StringWriter();
        using var logger = new LoggerConfiguration().CreateLogger();
        logger.BindMessageTemplate("Started", [], out var template, out var properties);
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, template!, properties!);

        // Act
        formatter.Format(logEvent, output);

        // Assert
        renderer.Verify(value => value.Format(logEvent, output), Times.Once);
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public void Format_BoundsDeepExceptionChains()
    {
        // Arrange
        var renderer = new Mock<ITextFormatter>();
        var formatter = new DiagnosticLogFormatter(renderer.Object);
        Exception error = new Exception("SECRET");
        for (var index = 0; index < 20; index++) error = new Exception("SECRET", error);
        using var output = new StringWriter();
        using var logger = new LoggerConfiguration().CreateLogger();
        logger.BindMessageTemplate("Failure", [], out var template, out var properties);
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Error, error, template!, properties!);

        // Act
        formatter.Format(logEvent, output);

        // Assert
        StringAssert.Contains(output.ToString(), "Further inner exceptions omitted.");
        Assert.AreEqual(10, output.ToString().Split("HResult:").Length - 1);
        Assert.IsFalse(output.ToString().Contains("SECRET", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ConstructorAndFormat_RejectInvalidArguments()
    {
        // Arrange
        var renderer = new Mock<ITextFormatter>();
        var formatter = new DiagnosticLogFormatter(renderer.Object);
        using var logger = new LoggerConfiguration().CreateLogger();
        logger.BindMessageTemplate("Failure", [], out var template, out var properties);
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Error, null, template!, properties!);

        // Act
        var constructorError = Assert.ThrowsException<ArgumentNullException>(() => new DiagnosticLogFormatter(null!));
        var eventError = Assert.ThrowsException<ArgumentNullException>(() => formatter.Format(null!, TextWriter.Null));
        var outputError = Assert.ThrowsException<ArgumentNullException>(() => formatter.Format(logEvent, null!));

        // Assert
        Assert.AreEqual("eventFormatter", constructorError.ParamName);
        Assert.AreEqual("logEvent", eventError.ParamName);
        Assert.AreEqual("output", outputError.ParamName);
    }
}
