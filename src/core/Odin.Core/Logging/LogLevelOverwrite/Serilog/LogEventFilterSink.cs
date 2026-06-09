using System;
using System.IO;
using System.Linq;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.LogLevelOverwrite.Serilog;

#nullable enable

public class LogEventFilterSink(ILogEventSink nextSink) : ILogEventSink
{
    private readonly ILogEventSink _nextSink = nextSink ?? throw new ArgumentNullException(nameof(nextSink));

    //

    public void Emit(LogEvent? logEvent)
    {
        LogEvent? modifiedEvent = null;

        if (logEvent != null)
        {
            modifiedEvent = ModifyLogEvent(logEvent);
        }

        if (modifiedEvent != null)
        {
            _nextSink.Emit(modifiedEvent);
        }
    }

    //

    private static LogEvent? ModifyLogEvent(LogEvent logEvent)
    {
        var exception = logEvent.Exception;

        // Kestrel logs a cert/private-key misconfiguration at Error; surface it as a Warning instead.
        if (exception is NotSupportedException &&
            exception.Message == "The server mode SSL must use a certificate with the associated private key.")
        {
            return WithLevel(logEvent, LogEventLevel.Warning);
        }

        // A remote client dropped its TLS connection mid-write (TCP reset / broken pipe). OpenSSL
        // surfaces this as SSL_ERROR_SYSCALL, which .NET wraps in an IOException, and Kestrel logs it
        // at Error from the GOAWAY/flush and connection-teardown paths (TimingPipeFlusher.FlushAsync,
        // "Unhandled exception while processing {ConnectionId}"). It is benign and not actionable, so
        // demote it to Verbose: hidden under the default Debug level, but recoverable on demand.
        if (exception is IOException && HasOpenSslSyscallError(exception))
        {
            return WithLevel(logEvent, LogEventLevel.Verbose);
        }

        return logEvent;
    }

    //

    private static bool HasOpenSslSyscallError(System.Exception? exception)
    {
        for (; exception != null; exception = exception.InnerException)
        {
            if (exception.Message.Contains("SSL_ERROR_SYSCALL", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    //

    private static LogEvent WithLevel(LogEvent logEvent, LogEventLevel level)
    {
        return new LogEvent(
            logEvent.Timestamp,
            level,
            logEvent.Exception,
            logEvent.MessageTemplate,
            logEvent.Properties.Select(p => new LogEventProperty(p.Key, p.Value))
        );
    }
}

//

public static class LogEventFilterExtensions
{
    public static LoggerConfiguration Filter(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        Action<LoggerSinkConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(loggerSinkConfiguration);
        ArgumentNullException.ThrowIfNull(configure);

        var logEventSink = LoggerSinkConfiguration.Wrap(
            wrappedSink => new LogEventFilterSink(wrappedSink),
            configure);

        return loggerSinkConfiguration.Sink(logEventSink);
    }
}

