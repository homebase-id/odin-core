using System;
using Serilog;
using Serilog.Configuration;

namespace Youverse.Core.Logging.CorrelationId.Serilog
{
    public static class CorrelationIdExtensions
    {
        public static LoggerConfiguration WithCorrelationId(this LoggerEnrichmentConfiguration enrichmentConfiguration)
        {
            if (enrichmentConfiguration == null)
            {
                throw new ArgumentNullException(nameof(enrichmentConfiguration));
            }

            var correlationContext = new CorrelationContext();
            var correlationEnricher = new CorrelationIdEnricher(correlationContext);
            return enrichmentConfiguration.With(correlationEnricher);
        }
    }
}
