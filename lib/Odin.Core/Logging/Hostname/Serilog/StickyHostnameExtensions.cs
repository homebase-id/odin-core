using System;
using Serilog;
using Serilog.Configuration;
using Youverse.Core.Logging.CorrelationId;
using Youverse.Core.Logging.CorrelationId.Serilog;

namespace Youverse.Core.Logging.Hostname.Serilog
{
    public static class StickyHostnameExtensions
    {
        public static LoggerConfiguration WithHostname(
            this LoggerEnrichmentConfiguration enrichmentConfiguration,
            IStickyHostnameGenerator hostnameGenerator)
        {
            if (enrichmentConfiguration == null)
            {
                throw new ArgumentNullException(nameof(enrichmentConfiguration));
            }

            var stickyHostname = new StickyHostname(hostnameGenerator);
            var stickyHostnameEnricher = new StickyHostnameEnricher(stickyHostname);
            return enrichmentConfiguration.With(stickyHostnameEnricher);
        }
        
    }
}