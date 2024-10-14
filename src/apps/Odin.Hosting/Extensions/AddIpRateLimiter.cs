using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Odin.Hosting.Extensions;

public static class RateLimiterExtensions
{
    public static IServiceCollection AddIpRateLimiter(this IServiceCollection services, int requestsPerSecond, int queueLimit = 0)
    {
        // https://blog.maartenballiauw.be/post/2022/09/26/aspnet-core-rate-limiting-middleware.html
        services.AddRateLimiter(options =>
        {
            // NOTE: we use the remote IP address as the partition key.
            // Make sure this is available if you're behind a reverse proxy.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = requestsPerSecond,
                        QueueLimit = queueLimit,
                        Window = TimeSpan.FromSeconds(1)
                    }));
            
            options.OnRejected = async (context, token) =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Startup>>();
                logger.LogDebug("Rate limit exceeded for {ip}", context.HttpContext.Connection.RemoteIpAddress);

                context.HttpContext.Response.StatusCode = 429;
                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Please try again later.", cancellationToken: token);
            };            
        });

        return services;
    }
}
