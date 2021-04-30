using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace DotYou.TenantHost.Logging
{
    public class MultiTenantLoggerConfiguration
    {
        public int EventId { get; set; }

        public Dictionary<LogLevel, ConsoleColor> LogLevels { get; set; } = new()
        {
            [LogLevel.Debug] = ConsoleColor.Green
        };
    }

}
