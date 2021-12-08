using System;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Services.Authorization;
using Youverse.Hosting.Security;
using Youverse.Hosting.Security.Authentication.Owner;

namespace Youverse.Hosting
{
    public static class AuthorizationServicesExtensions
    {
        public static IServiceCollection AddYouverseAuthorization(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            return services.AddAuthorization(policy =>
            {
                policy.AddPolicy(DotYouPolicyNames.IsDigitalIdentityOwner, 
                    pb =>
                    {
                        pb.RequireClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower());
                        pb.AuthenticationSchemes.Add(OwnerAuthConstants.DotIdentityOwnerScheme);
                    });

                policy.AddPolicy(DotYouPolicyNames.MustBeIdentified,
                    pb =>
                    {
                        pb.RequireClaim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower());
                        //pb.AuthenticationSchemes.Add((DotYouAuthSchemes.DotIdentityOwner));
                        pb.AuthenticationSchemes.Add((DotYouAuthConstants.ExternalDigitalIdentityClientCertificateScheme));
                    });
            });
        }
    }
}