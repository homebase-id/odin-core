using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Odin.Hosting.Authentication.Unified;

namespace Odin.Hosting.Controllers.APIv2.Base;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class OdinAuthorizeRouteAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
{
    public RootApiRoutes Flags { get; }

    public OdinAuthorizeRouteAttribute(RootApiRoutes flags)
    {
        this.Flags = flags;
        AuthenticationSchemes = UnifiedAuthConstants.SchemeName;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        var authorizationService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();

        //
        // Important!
        // You must check from lowest required auth to most secure
        //

        if (Flags.HasFlag(RootApiRoutes.Guest))
        {
            if (await HasPolicy(authorizationService, user, UnifiedPolicies.Guest))
            {
                return;
            }
        }


        if (Flags.HasFlag(RootApiRoutes.Apps))
        {
            if (await HasPolicy(authorizationService, user, UnifiedPolicies.App))
            {
                return;
            }
        }

        //
        if (Flags.HasFlag(RootApiRoutes.Owner))
        {
            if (await HasPolicy(authorizationService, user, UnifiedPolicies.Owner))
            {
                return;
            }
        }

        context.Result = new ForbidResult();
    }

    private static async Task<bool> HasPolicy(IAuthorizationService authorizationService, ClaimsPrincipal user, string policyName)
    {
        var authorizationResult = await authorizationService.AuthorizeAsync(user, policyName);
        if (authorizationResult.Succeeded)
        {
            return true;
        }

        return false;
    }
}