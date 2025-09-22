using System;
using Serilog;
using Serilog.Configuration;

namespace Odin.Core.Logging.Caller.Serilog;

public static class CallerLogExtensions
{
    public static LoggerConfiguration WithCaller(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        if (enrichmentConfiguration == null)
        {
            throw new ArgumentNullException(nameof(enrichmentConfiguration));
        }

        var callerLogExtension = new CallerLogContext();
        var callerLogEnricher = new CallerLogEnricher(callerLogExtension);
        return enrichmentConfiguration.With(callerLogEnricher);
    }
}