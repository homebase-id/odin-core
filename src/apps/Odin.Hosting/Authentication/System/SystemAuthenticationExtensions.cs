using System;
using Microsoft.AspNetCore.Authentication;

namespace Odin.Hosting.Authentication.System
{
    public static class SystemAuthenticationExtensions
    {
        public static AuthenticationBuilder AddSystemAuthentication(this AuthenticationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.AddScheme<SystemAuthenticationSchemeOptions, SystemAuthenticationHandler>(
                SystemAuthConstants.SchemeName,
                op => { });
        }
    }
}