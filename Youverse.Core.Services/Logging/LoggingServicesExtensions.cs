using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Logging.CorrelationId;
using Youverse.Core.Logging.Hostname;

namespace Youverse.Core.Services.Logging
{
    public static class LoggingServicesExtensions
    {
        public static IServiceCollection AddLoggingServices(this IServiceCollection services)
        {
            services.AddSingleton<ICorrelationIdGenerator, CorrelationUniqueIdGenerator>();
            services.AddSingleton<ICorrelationContext, CorrelationContext>();
            services.AddSingleton<IStickyHostnameGenerator, StickyHostnameGenerator>();
            services.AddSingleton<IStickyHostname, StickyHostname>();
            return services;
        }
    }
}