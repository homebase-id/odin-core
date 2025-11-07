using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Odin.Hosting.Middleware.Logging;

#nullable enable

public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly string[] LoggablePaths =
    {
        "/api",
        "/capi",
        "/home/login",
        "/home/youauth",
        "/owner/login",
        "/owner/youauth",
        "/.well-known/acme-challenge"
    };

    //

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path + context.Request.QueryString;

        if (!logger.IsEnabled(LogLevel.Trace) && !IsLoggable(path))
        {
            await next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "";
        var method = context.Request.Method;

        if (context.WebSockets.IsWebSocketRequest)
        {
            logger.LogInformation(
                "{RemoteIp} websock handshake {Path}",
                remoteIp,
                path
            );
            await next(context);
        }
        else
        {
            var stopwatch = Stopwatch.StartNew();

            logger.LogInformation(
                "{RemoteIp} request starting {Method} {Path}",
                remoteIp,
                method,
                path
            );

            await next(context);

            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            logger.LogInformation("{RemoteIp} request finished {Method} {Path} in {Elapsed}ms {Status}",
                remoteIp,
                method,
                path,
                elapsed,
                context.Response.StatusCode
            );

            if (elapsed > 60000)
            {
                var agent = context.Request.Headers["User-Agent"].ToString();
                logger.LogWarning("Slow agent: {Agent} {Elapsed}ms", agent, elapsed);
                LogHeaders(context.Request.Headers, LogLevel.Debug);
            }
        }
    }

    //

    private void LogHeaders(IHeaderDictionary headers, LogLevel level)
    {
        if (logger.IsEnabled(level))
        {
            var strings = new List<string>();
            foreach (var (key, value) in headers)
            {
                strings.Add("\"" + key + ":" + value + "\"");
            }
            logger.Log(level, "Request headers: {RequestHeaders}", string.Join(';', strings));
        }
    }

    //

    private static bool IsLoggable(string path)
    {
        foreach (var loggablePath in LoggablePaths)
        {
            if (path.StartsWith(loggablePath))
            {
                return true;
            }
        }
        return false;
    }

    //

}