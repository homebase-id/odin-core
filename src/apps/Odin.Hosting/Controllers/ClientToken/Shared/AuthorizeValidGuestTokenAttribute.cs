using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.ClientToken.Shared;

public class AuthorizeValidAppNotificationSubscriberTokenAttribute : AuthorizeAttribute
{
    public AuthorizeValidAppNotificationSubscriberTokenAttribute()
    {
        AuthenticationSchemes = YouAuthConstants.AppNotificationSubscriberScheme;
        // Policy = OwnerPolicies.IsAuthorizedApp;
    }
}