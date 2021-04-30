using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;

namespace DotYou.TenantHost.Logging
{
    public static class MultiTenantLoggerExtensions
    {
        public static ILoggingBuilder AddMultiTenantLogger(
            this ILoggingBuilder builder)
        {
            builder.AddConfiguration();

            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ILoggerProvider, MultiTenantLoggingProvider>());

            LoggerProviderOptions.RegisterProviderOptions
                <MultiTenantLoggerConfiguration, MultiTenantLoggingProvider>(builder.Services);

            return builder;
        }

        public static ILoggingBuilder AddMultiTenantLogger(
            this ILoggingBuilder builder,
            Action<MultiTenantLoggerConfiguration> configure)
        {
            builder.AddMultiTenantLogger();
            builder.Services.Configure(configure);

            return builder;
        }
    }
}
