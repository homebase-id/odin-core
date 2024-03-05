using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Core.Logging.CorrelationId;

namespace Odin.Hosting.Middleware.Logging
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ICorrelationContext _correlationContext;
        private readonly string _correlationIdHeader;

        //

        public CorrelationIdMiddleware(RequestDelegate next, ICorrelationContext correlationContext)
        {
            _next = next;
            _correlationIdHeader = ICorrelationContext.DefaultHeaderName;
            _correlationContext = correlationContext;
        }

        //

        public Task Invoke(HttpContext context)
        {
            var correlationId = context.Request.Headers[_correlationIdHeader].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                _correlationContext.Id = correlationId;
            }
            context.Response.Headers[_correlationIdHeader] = _correlationContext.Id;

            return _next(context);
        }
    }
}