using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Odin.Hosting.Middleware.Logging;

#nullable enable

public class RequestLoggingMiddleware
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

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    //

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path + context.Request.QueryString;

        if (!_logger.IsEnabled(LogLevel.Trace) && !IsLoggable(path))
        {
            await _next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "";
        var method = context.Request.Method;

        if (context.WebSockets.IsWebSocketRequest)
        {
            _logger.LogInformation(
                "{RemoteIp} websock handshake {Path}",
                remoteIp,
                path
            );
            await _next(context);
        }
        else
        {
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "{RemoteIp} request starting {Method} {Path}",
                remoteIp,
                method,
                path
            );

            await _next(context);

            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            _logger.LogInformation("{RemoteIp} request finished {Method} {Path} in {Elapsed}ms {Status}",
                remoteIp,
                method,
                path,
                elapsed,
                context.Response.StatusCode
            );

            if (elapsed > 60000)
            {
                var agent = context.Request.Headers["User-Agent"].ToString();
                _logger.LogWarning("Slow agent: {Agent} {Elapsed}ms", agent, elapsed);
                LogHeaders(context.Request.Headers, LogLevel.Debug);
            }
        }
    }

    //

    private void LogHeaders(IHeaderDictionary headers, LogLevel level)
    {
        if (_logger.IsEnabled(level))
        {
            var strings = new List<string>();
            foreach (var (key, value) in headers)
            {
                strings.Add("\"" + key + ":" + value + "\"");
            }
            _logger.Log(level, "Request headers: {RequestHeaders}", string.Join(';', strings));
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