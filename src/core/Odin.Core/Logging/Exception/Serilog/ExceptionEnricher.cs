using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.Exception.Serilog;

public class ExceptionMessageEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Exception != null)
        {
            var exceptionMessage = logEvent.Exception.Message;
            var logMessage = logEvent.RenderMessage();

            if (!logMessage.Contains(exceptionMessage))
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                    "ExceptionMessage", $" ({logEvent.Exception.GetType().Name}: {exceptionMessage})"));
            }
        }
    }
}

public static class ExceptionMessageEnricherExtensions
{
    public static LoggerConfiguration WithExceptionMessage(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        return enrichmentConfiguration.With(new ExceptionMessageEnricher());
    }
}
