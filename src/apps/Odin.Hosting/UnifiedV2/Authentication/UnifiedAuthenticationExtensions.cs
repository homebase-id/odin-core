using System;
using Microsoft.AspNetCore.Authentication;

namespace Odin.Hosting.UnifiedV2.Authentication
{
    public static class UnifiedAuthenticationExtensions
    {
        public static AuthenticationBuilder AddUnifiedAuthentication(this AuthenticationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.AddScheme<UnifiedAuthenticationSchemeOptions, UnifiedAuthenticationHandler>(
                UnifiedAuthConstants.SchemeName, _ => { });
        }
    }
}