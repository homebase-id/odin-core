using System;
using Microsoft.AspNetCore.Authentication;

namespace Odin.Hosting.Authentication.YouAuth
{
    public static class YouAuthAuthenticationExtensions
    {
        public static AuthenticationBuilder AddClientTokenAuthentication(this AuthenticationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.AddScheme<YouAuthAuthenticationSchemeOptions, YouAuthAuthenticationHandler>(
                YouAuthConstants.YouAuthScheme, op => { });
        }
    }
}