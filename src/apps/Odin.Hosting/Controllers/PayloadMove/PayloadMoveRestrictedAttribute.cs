using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Odin.Hosting.Controllers.PayloadMove;

/// <summary>
/// The payload move endpoint answers only where it is meant to: on the provisioning domain, and only when
/// this host serves moved payloads. Controllers are mapped on every host name, so the host is checked here,
/// not just in Startup's branch. Anything else is a 404, to not confirm the endpoint exists.
/// </summary>
public class PayloadMoveRestrictedAttribute(bool sourceEnabled, string provisioningDomain) : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!sourceEnabled || context.HttpContext.Request.Host.Host != provisioningDomain)
        {
            // Strange hack to circumvent creation of a ProblemDetails
            context.Result = new ObjectResult(null) { StatusCode = (int)HttpStatusCode.NotFound };
            return;
        }

        base.OnActionExecuting(context);
    }
}
