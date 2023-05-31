using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Youverse.Hosting.Middleware.Logging
{
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

            if (!IsLoggable(path))
            {
                await _next(context);
                return;
            }
            
            var stopwatch = Stopwatch.StartNew();

            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "";
            var protocol = context.Request.Protocol;
            var method = context.Request.Method;

            _logger.LogInformation(
                "{RemoteIp} request starting |> {Protocol} {Method} {Path}",
                remoteIp,
                protocol,
                method, 
                path
            );

            // Log request headers
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                LogHeaders("Request headers", context.Request.Headers);
            }

            await _next(context);

            _logger.LogInformation("{RemoteIp} request finished |> {Protocol} {Method} {Path} in {Elapsed}ms {Status}",
                remoteIp,
                protocol,
                method,
                path,
                stopwatch.Elapsed.TotalMilliseconds,
                context.Response.StatusCode
            );
        }

        // 

        private void LogHeaders(string lead, IHeaderDictionary headers)
        {
            var strings = new List<string>();
            foreach (var (key, value) in headers)
            {
                strings.Add("\"" + key + ":" + value + "\"");
            }
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            _logger.LogDebug(lead + " {RequestHeaders}", string.Join(';', strings));
        }
        
        //

        private bool IsLoggable(string path)
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
}