using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Youverse.Hosting.Logging
{
    public class MultiTenantLogger : ILogger
    {
        private readonly string _name;
        private readonly MultiTenantLoggerConfiguration _config;
        private IHttpContextAccessor _contextAccessor;

        public MultiTenantLogger(IHttpContextAccessor contextAccessor, string name, MultiTenantLoggerConfiguration currentConfig)
        {
            _contextAccessor = contextAccessor;
            _name = name;
            _config = currentConfig;
        }

        public IDisposable BeginScope<TState>(TState state) => default;

        public bool IsEnabled(LogLevel logLevel) => _config.LogLevels.ContainsKey(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (_config.EventId == 0 || _config.EventId == eventId.Id)
            {

                WriteTenantName();

                ConsoleColor fg = Console.ForegroundColor;

                Console.ForegroundColor = _config.LogLevels[logLevel];
                Console.Write($"\t[{eventId.Id,2}: {logLevel,-12}]");

                Console.ForegroundColor = fg;
                Console.WriteLine($"     {_name} - {formatter(state, exception)}");
            }
        }

        private void WriteTenantName()
        {

            var fg = Console.ForegroundColor;
            var bg = Console.BackgroundColor;

            var ctx = _contextAccessor?.HttpContext;
            string tenantInfo = "No Tenant Info";

            //if (ctx != null)
            //{
            //    var tenantCtx = (ITenantContext)ctx.RequestServices.GetService(typeof(ITenantContext));
            //    tenantInfo = tenantCtx.Identifier;
            //}

            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"[{tenantInfo}] -> ");

            Console.BackgroundColor = bg;
            Console.ForegroundColor = fg;
        }
    }
}
