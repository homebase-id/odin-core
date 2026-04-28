using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Configuration;
using StackExchange.Redis;

namespace Odin.Hosting.Cli.Commands;

public static class RedisPing
{
    internal static async Task ExecuteAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<OdinConfiguration>();

        if (!config.Redis.Enabled)
        {
            throw new OdinSystemException("Redis is disabled in configuration");
        }

        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var multiplexer = services.GetRequiredService<IConnectionMultiplexer>();
        var subscriber = multiplexer.GetSubscriber();
        var responseTime = await subscriber.PingAsync();
        logger.LogInformation("Redis is up, ping: {ms}ms", responseTime.TotalMilliseconds);
    }
}
