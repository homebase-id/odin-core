using System;
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
        if (logEvent.Exception != null &&
            logEvent.Exception.GetType() == typeof(NotSupportedException) &&
            logEvent.Exception.Message == "The server mode SSL must use a certificate with the associated private key.")
        {
            return new LogEvent(
                logEvent.Timestamp,
                LogEventLevel.Warning,
                logEvent.Exception,
                logEvent.MessageTemplate,
                logEvent.Properties.Select(p => new LogEventProperty(p.Key, p.Value))
            );
        }

        return logEvent;
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

