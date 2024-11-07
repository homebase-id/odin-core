using System;
using Microsoft.AspNetCore.Authentication;

namespace Odin.Hosting.Authentication.YouAuth
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
                YouAuthConstants.YouAuthScheme, op => { });
        }
        
        public static AuthenticationBuilder AddAppNotificationSubscriberAuthentication(this AuthenticationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.AddScheme<YouAuthAuthenticationSchemeOptions, YouAuthAuthenticationHandler>(
                YouAuthConstants.AppNotificationSubscriberScheme, op => { });
        }
    }
}