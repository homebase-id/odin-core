using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Core.Logging.CorrelationId;

namespace Odin.Hosting.Middleware.Logging;

public class CorrelationIdMiddleware(RequestDelegate next, ICorrelationContext correlationContext)
{
    private const string CorrelationIdHeader = ICorrelationContext.DefaultHeaderName;

    //

    public Task Invoke(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            correlationContext.Id = correlationId;
        }
        context.Response.Headers[CorrelationIdHeader] = correlationContext.Id;

        return next(context);
    }
}