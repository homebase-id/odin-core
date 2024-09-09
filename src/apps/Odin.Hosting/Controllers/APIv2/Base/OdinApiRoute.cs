using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Odin.Hosting.Controllers.APIv2.Base;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class OdinAuthorizeRouteAttribute(RootApiRoutes flags) : Attribute, IAsyncAuthorizationFilter
{
    public RootApiRoutes Flags { get; } = flags;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        //

        if (Flags.HasFlag(RootApiRoutes.Owner))
        {
            // check owner claims
            
            context.HttpContext.User.Claims.Any(c=>c.Type == "");
        }

        context.Result = new ForbidResult();
        return Task.CompletedTask;
    }
}