using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Logging.CorrelationId;

namespace Youverse.Core.Services.Logging
{
    public static class LoggingServicesExtensions
    {
        public static IServiceCollection AddLoggingServices(this IServiceCollection services)
        {
            services.AddSingleton<ICorrelationIdGenerator, CorrelationUniqueIdGenerator>();
            services.AddSingleton<ICorrelationContext, CorrelationContext>();
            return services;
        }
    }
}