using System.Collections.Generic;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.Exception.Serilog;

public class ExceptionMessageEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Exception == null)
        {
            return;
        }

        var exception = logEvent.Exception;
        var logMessage = logEvent.RenderMessage();

        // We assume that if the top exception message is already in the log message,
        // the user is not interested in the rest of the exception messages.
        if (logMessage.Contains(exception.Message))
        {
            return;
        }

        var errors = new List<string>();
        while (exception != null)
        {
            errors.Add(exception.Message.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " "));
            exception = exception.InnerException;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "ExceptionMessage", $" ({string.Join(" -> ", errors)})"));
    }
}

public static class ExceptionMessageEnricherExtensions
{
    public static LoggerConfiguration WithExceptionMessage(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        return enrichmentConfiguration.With(new ExceptionMessageEnricher());
    }
}
