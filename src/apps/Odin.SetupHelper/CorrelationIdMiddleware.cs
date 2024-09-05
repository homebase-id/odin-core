using Odin.Core.Logging.CorrelationId;

namespace Odin.SetupHelper;

public class CorrelationIdMiddleware(RequestDelegate next, ICorrelationContext correlationContext)
{
    private readonly string _correlationIdHeader = ICorrelationContext.DefaultHeaderName;

    //

    public Task Invoke(HttpContext context)
    {
        var correlationId = context.Request.Headers[_correlationIdHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            correlationContext.Id = correlationId;
        }
        context.Response.Headers[_correlationIdHeader] = correlationContext.Id;

        return next(context);
    }
}
