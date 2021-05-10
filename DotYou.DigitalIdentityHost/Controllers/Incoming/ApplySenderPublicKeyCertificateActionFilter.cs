using System.Linq;
using System.Security.Claims;
using DotYou.Kernel.Services.Identity;
using DotYou.Types.Circle;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DotYou.TenantHost.Controllers.Incoming
{
    /// <summary>
    /// Sets the sender's public key token using the claims provided by client certificate authentication.
    /// </summary>
    public class ApplySenderPublicKeyCertificateActionFilter:IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.HttpContext.Request.Path.StartsWithSegments("/api/incoming"))
            {
                return;
            }
            
            var instances = context.ActionArguments.Values.Where(o =>(o.GetType().IsClass) && o is IRequireSenderCertificate).ToList();
            if (instances.Any())
            {
                var cert = context.HttpContext.User.FindFirstValue(DotYouClaimTypes.PublicKeyCertificate);
                foreach (var rc in instances)
                {
                    ((IRequireSenderCertificate) rc).SenderPublicKeyCertificate = cert;
                }
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {

        }
    }
}