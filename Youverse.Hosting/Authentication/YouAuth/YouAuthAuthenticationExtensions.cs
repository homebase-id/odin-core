using System;
using Microsoft.AspNetCore.Authentication;

namespace Youverse.Hosting.Authentication.YouAuth
{
    public static class YouAuthAuthenticationExtensions
    {
        public static AuthenticationBuilder AddYouAuthAuthentication(this AuthenticationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.AddScheme<YouAuthAuthenticationSchemeOptions, YouAuthAuthenticationHandler>(
                YouAuthConstants.Scheme, op => { });
        }
    }
}