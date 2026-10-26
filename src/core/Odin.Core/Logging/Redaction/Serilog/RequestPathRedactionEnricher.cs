using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.Redaction.Serilog;

/// <summary>
/// ASP.NET Core puts the request's path on every log line of the request as RequestPath, and the log shipper sends it
/// along as an attribute. Some paths carry a credential (.../is-valid-invitation-code/{code}); this replaces it.
/// </summary>
public class RequestPathRedactionEnricher : ILogEventEnricher
{
    private const string RequestPath = "RequestPath";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.TryGetValue(RequestPath, out var value) && value is ScalarValue { Value: string path })
        {
            var redacted = LogRedaction.Url(path);
            if (redacted != path)
            {
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(RequestPath, redacted));
            }
        }
    }
}

public static class RequestPathRedactionEnricherExtensions
{
    public static LoggerConfiguration WithRequestPathRedaction(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        return enrichmentConfiguration.With(new RequestPathRedactionEnricher());
    }
}
