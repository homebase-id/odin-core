using Microsoft.AspNetCore.Authorization;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.UnifiedV2.Authentication.Policy
{
    public static class UnifiedPolicies
    {
        public static string AsClaimValue(ClientTokenType tt)
        {
            return ((int)tt).ToString();
        }

        public const string Anonymous = "Unified-Anonymous";
        public const string Owner = "Unified-OwnerToken";
        public const string OwnerOrApp = "Unified-OwnerOrApp";
        public const string OwnerOrAppOrGuest = "Unified-OwnerOrAppOrGuest";
        
        public const string Guest = "Unified-HasValidGuestAccessToken";

        // A guest is either a YouAuth domain, with a grant of its own, or a visitor signed in on the home site
        // (BuiltInBrowserApp), whose access is what its connection already allows.  V1 gives the two the same claims
        // (YouAuthAuthenticationHandler); V2 lets in the one only where it lets in the other.
        private static readonly string[] GuestTokenTypes =
        [
            AsClaimValue(ClientTokenType.YouAuth),
            AsClaimValue(ClientTokenType.BuiltInBrowserApp)
        ];

        public static void AddPolicies(AuthorizationOptions options)
        {
            options.AddPolicy(Owner, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(UnifiedClaimTypes.ClientTokenType, AsClaimValue(ClientTokenType.Owner));
                policy.AuthenticationSchemes.Add(UnifiedAuthConstants.SchemeName);
            });

            options.AddPolicy(Anonymous, policy =>
            {
                policy.AuthenticationSchemes.Add(UnifiedAuthConstants.SchemeName);
                //so long as it is set
                policy.RequireClaim(UnifiedClaimTypes.IsAuthenticated, [
                    "",
                    bool.TrueString.ToLower(),
                    bool.FalseString.ToLower()
                ]);
            });

            options.AddPolicy(OwnerOrApp, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(UnifiedClaimTypes.ClientTokenType,
                    [
                        AsClaimValue(ClientTokenType.Owner),
                        AsClaimValue(ClientTokenType.App)
                    ]
                );

                policy.AuthenticationSchemes.Add(UnifiedAuthConstants.SchemeName);
            });

            options.AddPolicy(OwnerOrAppOrGuest, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(UnifiedClaimTypes.ClientTokenType,
                    [
                        AsClaimValue(ClientTokenType.Owner),
                        AsClaimValue(ClientTokenType.App),
                        ..GuestTokenTypes
                    ]
                );

                policy.AuthenticationSchemes.Add(UnifiedAuthConstants.SchemeName);
            });
            options.AddPolicy(Guest, policy =>
            {
                policy.RequireAuthenticatedUser();

                policy.RequireClaim(UnifiedClaimTypes.ClientTokenType, GuestTokenTypes);

                policy.AuthenticationSchemes.Add(UnifiedAuthConstants.SchemeName);
            });
        }
    }
}