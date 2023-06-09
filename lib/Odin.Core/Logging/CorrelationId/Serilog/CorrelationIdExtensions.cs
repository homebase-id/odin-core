using System;
using Serilog;
using Serilog.Configuration;

namespace Odin.Core.Logging.CorrelationId.Serilog
{
    public static class CorrelationIdExtensions
    {
        public static LoggerConfiguration WithCorrelationId(
            this LoggerEnrichmentConfiguration enrichmentConfiguration,
            ICorrelationIdGenerator correlationIdGenerator)
        {
            if (enrichmentConfiguration == null)
            {
                throw new ArgumentNullException(nameof(enrichmentConfiguration));
            }

            var correlationContext = new CorrelationContext(correlationIdGenerator);
            var correlationEnricher = new CorrelationIdEnricher(correlationContext);
            return enrichmentConfiguration.With(correlationEnricher);
        }
    }
}
