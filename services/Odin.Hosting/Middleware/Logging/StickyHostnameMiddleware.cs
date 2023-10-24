using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Core.Logging.Hostname;

namespace Odin.Hosting.Middleware.Logging
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

        public Task Invoke(HttpContext context)
        {
            _stickyHostname.Hostname = context.Request.Host.Host;
            return _next(context);
        }
        
    }
}