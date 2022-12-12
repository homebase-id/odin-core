using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog.Context;
using Youverse.Core.Logging.Hostname;

namespace Youverse.Hosting.Middleware.Logging
{
    public class StickyHostnameMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IStickyHostname _stickyHostname;

        //

        public StickyHostnameMiddleware(RequestDelegate next, IStickyHostname stickyHostname)
        {
            _next = next;
            _stickyHostname = stickyHostname;
        }

        //

        public async Task Invoke(HttpContext context)
        {
            _stickyHostname.Hostname = context.Request.Host.Host;
            await _next(context);
        }
        
    }
}