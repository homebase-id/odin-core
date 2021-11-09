using Microsoft.AspNetCore.Builder;
using Youverse.Hosting.Logging;

namespace Youverse.Hosting.Middleware.Logging
{
    public static class LoggingMiddlewareExtenstions
    {
        public static IApplicationBuilder UseLoggingMiddleware(this IApplicationBuilder app)
        {
            app.UseMiddleware<StickyHostnameMiddleware>();
            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseMiddleware<RequestLoggingMiddleware>();

            return app;
        }
    }
}