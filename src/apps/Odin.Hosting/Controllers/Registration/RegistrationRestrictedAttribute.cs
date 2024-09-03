using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Odin.Hosting.Controllers.Registration;

public class RegistrationRestrictedAttribute(bool provisioningEnabled) : ActionFilterAttribute
{
    // Note: the route to registration is normally blocked in startup.cs,
    // we're just adding an extra layer of security here
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!provisioningEnabled)
        {
            // Strange hack to circumvent creation of a ProblemDetails
            context.Result = new ObjectResult(null) { StatusCode = (int)HttpStatusCode.NotFound };
            return;
        }

        base.OnActionExecuting(context);
    }
}
