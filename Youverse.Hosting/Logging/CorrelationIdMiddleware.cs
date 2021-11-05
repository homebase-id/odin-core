using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Youverse.Core.Logging.CorrelationId;

namespace Youverse.Hosting.Logging
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
            _correlationIdHeader = "X-Correlation-Id"; // SEB:TODO make configurable
            _correlationContext = correlationContext;
        }

        //

        public async Task Invoke(HttpContext context)
        {
            var correlationId = context.Request.Headers[_correlationIdHeader].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                _correlationContext.Id = correlationId;
            }
            context.Response.Headers[_correlationIdHeader] = _correlationContext.Id;

            await _next(context);
        }
    }
}