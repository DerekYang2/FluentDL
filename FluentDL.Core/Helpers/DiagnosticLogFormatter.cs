using System.Diagnostics;
using Serilog.Events;
using Serilog.Formatting;

namespace FluentDL.Core.Helpers;

public sealed class DiagnosticLogFormatter : ITextFormatter
{
    private readonly ITextFormatter _eventFormatter;

    public DiagnosticLogFormatter(ITextFormatter eventFormatter)
    {
        ArgumentNullException.ThrowIfNull(eventFormatter);
        _eventFormatter = eventFormatter;
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);
        _eventFormatter.Format(logEvent, output);
        if (logEvent.Exception is not null)
        {
            WriteException(logEvent.Exception, output, 0);
        }
    }

    private static void WriteException(Exception exception, TextWriter output, int depth)
    {
        if (depth >= 10)
        {
            output.WriteLine("Further inner exceptions omitted.");
            return;
        }

        // SDK exception messages/Data can contain tokens, raw responses, command lines or private paths.
        output.WriteLine($"{exception.GetType().FullName} (HResult: 0x{exception.HResult:X8}; message omitted for privacy)");
        if (exception is HttpRequestException { StatusCode: not null } httpException)
        {
            output.WriteLine($"HTTP status: {(int)httpException.StatusCode.Value}");
        }
        output.Write(new StackTrace(exception, false).ToString());

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                WriteException(inner, output, depth + 1);
            }
        }
        else if (exception.InnerException is not null)
        {
            WriteException(exception.InnerException, output, depth + 1);
        }
    }
}
