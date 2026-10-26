#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.Redaction.Serilog;

/// <summary>
/// Applies <see cref="LogRedaction.Url"/> to every URL a log line carries, so no call site has to remember to.
/// That covers the request path ASP.NET Core puts on every line of a request (RequestPath), and the URLs our own lines
/// name by these properties: the request logger's Path, a link preview's Url, a YouAuth redirect.
/// </summary>
public class UrlRedactionEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> UrlProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "RequestPath", "Path", "Url", "Uri", "Redirect", "RedirectUrl", "ReturnUrl"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<(string Name, string Value)>? redacted = null;
        foreach (var (name, value) in logEvent.Properties)
        {
            if (!UrlProperties.Contains(name) || value is not ScalarValue { Value: string or Uri } scalar)
            {
                continue;
            }

            var url = scalar.Value!.ToString()!;
            var safe = LogRedaction.Url(url);
            if (safe != url)
            {
                (redacted ??= []).Add((name, safe));
            }
        }

        foreach (var (name, safe) in redacted ?? Enumerable.Empty<(string, string)>())
        {
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(name, safe));
        }
    }
}

public static class UrlRedactionEnricherExtensions
{
    public static LoggerConfiguration WithUrlRedaction(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        return enrichmentConfiguration.With(new UrlRedactionEnricher());
    }
}
