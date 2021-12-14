using System;
using Microsoft.AspNetCore.Authentication;

namespace Youverse.Hosting.Authentication.App
{
    public static class AppAuthenticationExtensions
    {
        public static AuthenticationBuilder AddAppAuthentication(this AuthenticationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.AddScheme<AppAuthenticationSchemeOptions, AppAuthenticationHandler>(
                AppAuthConstants.AppAuthSchemeName,
                op => { });
        }
    }
}