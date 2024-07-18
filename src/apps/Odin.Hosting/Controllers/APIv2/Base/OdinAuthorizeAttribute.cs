using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Odin.Hosting.Controllers.APIv2.Base;

public class OdinAuthorizeAttribute(RootApiRoutes flags) : AuthorizeAttribute, IAsyncAuthorizationFilter
{
    public RootApiRoutes Flags { get; } = flags;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var authorizationService = (IAuthorizationService)context.HttpContext.RequestServices.GetService(typeof(IAuthorizationService));
        var user = context.HttpContext.User;

        if (user == null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        
        // validate the user has the right claim

        var hasClaim = user.Claims.Any(c => c.Type == _claimType && c.Value == _claimValue);

        if (!hasClaim)
        {
            context.Result = new ForbidResult();
        }
        

        await Task.CompletedTask;

        // var policy1Result = await authorizationService.AuthorizeAsync(user, _policy1);
        // var policy2Result = await authorizationService.AuthorizeAsync(user, _policy2);
        //
        // if (!policy1Result.Succeeded && !policy2Result.Succeeded)
        // {
        //     context.Result = new ForbidResult();
        // }
    }
}