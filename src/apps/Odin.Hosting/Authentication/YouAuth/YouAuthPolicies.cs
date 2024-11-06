using Microsoft.AspNetCore.Authorization;
using Odin.Services.Authorization;

namespace Odin.Hosting.Authentication.YouAuth;

public static class YouAuthPolicies
{
    public const string IsAuthorizedApp = "IsAuthorizedApp";

    public const string IsYouAuthAuthorized = "IsClientTokenIdentified";

    public const string IsAppNotificationSubscriber = "IsAppNotificationSubscriber";

    public static void AddPolicies(AuthorizationOptions policy)
    {
        policy.AddPolicy(IsAppNotificationSubscriber, pb =>
        {
            pb.RequireClaim(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower());
            pb.RequireClaim(OdinClaimTypes.IsIdentityOwner, bool.FalseString.ToLower());
            pb.AuthenticationSchemes.Add(YouAuthConstants.AppNotificationSubscriberScheme);
        });

        policy.AddPolicy(IsYouAuthAuthorized, pb =>
        {
            pb.RequireClaim(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower());
            pb.AuthenticationSchemes.Add(YouAuthConstants.YouAuthScheme);
        });

        policy.AddPolicy(IsYouAuthAuthorized, pb =>
        {
            pb.RequireClaim(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower());
            pb.AuthenticationSchemes.Add(YouAuthConstants.YouAuthScheme);
        });

        policy.AddPolicy(IsAuthorizedApp, pb =>
        {
            pb.RequireClaim(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower());
            pb.RequireClaim(OdinClaimTypes.IsAuthorizedApp, true.ToString().ToLower());
            pb.AuthenticationSchemes.Add(YouAuthConstants.YouAuthScheme);
        });
    }
}