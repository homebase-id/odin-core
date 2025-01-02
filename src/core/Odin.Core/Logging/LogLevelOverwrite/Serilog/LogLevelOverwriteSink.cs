// SEB:TODO delete me when we're sure Kestrel no longer throws "The server mode SSL must use a certificate with the associated private key." on SSLv2 probes

#if false
using System;
using System.Linq;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.LogLevelOverwrite.Serilog;

public static class LogLevelModifierSinkExtensions
{
    public static LoggerConfiguration LogLevelModifier(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        Action<LoggerSinkConfiguration> configure)
    {
        return LoggerSinkConfiguration.Wrap(loggerSinkConfiguration, sink =>
            new LogLevelModifierSink(sink), configure, LevelAlias.Minimum, null);
    }
}

//

public class LogLevelModifierSink : ILogEventSink
{
    private readonly ILogEventSink _targetSink;

    //

    public LogLevelModifierSink(ILogEventSink targetSink)
    {
        _targetSink = targetSink ?? throw new ArgumentNullException(nameof(targetSink));
    }

    //

    public void Emit(LogEvent logEvent)
    {
        var newLogLevel = ChangeLogLevel(logEvent);
        if (newLogLevel == logEvent.Level)
        {
            _targetSink.Emit(logEvent);
        }
        else
        {
            var newLogEvent = new LogEvent(
                logEvent.Timestamp,
                newLogLevel,
                logEvent.Exception,
                logEvent.MessageTemplate,
                logEvent.Properties.Select(p => new LogEventProperty(p.Key, p.Value))
            );
            _targetSink.Emit(newLogEvent);
        }
    }

    //

    private static LogEventLevel ChangeLogLevel(LogEvent logEvent)
    {
        if (logEvent.Exception != null &&
            logEvent.Exception.GetType() == typeof(NotSupportedException) &&
            logEvent.Exception.Message == "The server mode SSL must use a certificate with the associated private key.")
        {
            return LogEventLevel.Warning;
        }

        return logEvent.Level;
    }
}
#endif
