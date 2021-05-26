using System;
using System.Linq;
using System.Security.Claims;
using DotYou.Kernel.Services.Identity;
using DotYou.Types;
using DotYou.Types.Circle;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter
{
    /// <summary>
    /// Applies meta data describing the incoming payload (message, invitation, etc.) such as
    /// the client certificate and timestamp received.
    /// </summary>
    public class ApplyPerimeterMetaData : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.HttpContext.Request.Path.StartsWithSegments("/api/perimeter"))
            {
                return;
            }

            var instances = context.ActionArguments.Values.Where(o => (o.GetType().IsClass) && o is IIncomingCertificateMetaData).ToList();
            if (instances.Any())
            {
                var cert = context.HttpContext.User.FindFirstValue(DotYouClaimTypes.PublicKeyCertificate);

                foreach (var rc in instances)
                {
                    var rsc = (IIncomingCertificateMetaData) rc;
                    rsc.SenderPublicKeyCertificate = cert;

                    //No null check here because the identity must be set at authentication.
                    rsc.SenderDotYouId = (DotYouIdentity) context.HttpContext.User.Identity.Name;

                    rsc.ReceivedTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}