using System.Diagnostics;

namespace YouAuthClientReferenceImplementation;

public class LoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;

    public LoggingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _next = next;
        _logger = loggerFactory.CreateLogger<LoggingMiddleware>();
    }

    public async Task Invoke(HttpContext httpContext)
    {
        if (httpContext.WebSockets.IsWebSocketRequest)
        {
            _logger.LogDebug("WebSocket connect: {path}", httpContext.Request.Path);
            await _next(httpContext);
        }
        else
        {
            var startTimestamp = Stopwatch.GetTimestamp();

            await _next(httpContext); // Call the next middleware

            var currentTimestamp = Stopwatch.GetTimestamp();
            var duration = TimeSpan.FromTicks(currentTimestamp - startTimestamp).TotalMilliseconds;

            _logger.LogInformation($"{httpContext.Request.Method} {httpContext.Request.Path} {duration}ms");
        }
    }
}
