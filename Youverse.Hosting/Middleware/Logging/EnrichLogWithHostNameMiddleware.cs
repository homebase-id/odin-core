using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Youverse.Hosting.Middleware.Logging
{
    public class EnrichLogWithHostNameMiddleware
    {
        private readonly RequestDelegate _next;

        //

        public EnrichLogWithHostNameMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        //

        public async Task Invoke(HttpContext context)
        {
            var hostname = context.Request.Host.Host; 
            
            using (LogContext.PushProperty("HostName", hostname))
            {
                await _next(context);
            }
        }
        
    }
}