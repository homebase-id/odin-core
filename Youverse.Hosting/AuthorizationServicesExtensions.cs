using System;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.TransitPerimeter;

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
                policy.AddPolicy(OwnerPolicies.IsDigitalIdentityOwnerPolicyName, OwnerPolicies.IsDigitalIdentityOwnerPolicy);
                policy.AddPolicy(TransitPerimeterPolicies.MustBeIdentifiedPolicyName, TransitPerimeterPolicies.MustBeIdentifiedPolicy);
            });
            
        }
    }
}