using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Contacts.Circle;

namespace Youverse.Hosting.Controllers.TransitPerimeter
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
                foreach (var rc in instances)
                {
                    var rsc = (IIncomingCertificateMetaData) rc;
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