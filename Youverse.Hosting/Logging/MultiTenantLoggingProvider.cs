using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotYou.TenantHost.Logging
{
    public sealed class MultiTenantLoggingProvider : ILoggerProvider
    {
        private readonly IDisposable _onChangeToken;
        private MultiTenantLoggerConfiguration _currentConfig;
        private readonly ConcurrentDictionary<string, MultiTenantLogger> _loggers = new();
        IHttpContextAccessor _contextAccessor;

        public MultiTenantLoggingProvider(IHttpContextAccessor ca, IOptionsMonitor<MultiTenantLoggerConfiguration> config)
        {
            _currentConfig = config.CurrentValue;
            _onChangeToken = config.OnChange(updatedConfig => _currentConfig = updatedConfig);
            _contextAccessor = ca;
        }

        public ILogger CreateLogger(string categoryName)
        {

            return _loggers.GetOrAdd(categoryName, name => new MultiTenantLogger(_contextAccessor, name, _currentConfig));
        }

        public void Dispose()
        {
            _loggers.Clear();
            _onChangeToken.Dispose();
        }
    }

}
