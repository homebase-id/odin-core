using System;
using Microsoft.AspNetCore.Authentication;

namespace Youverse.Hosting.Authentication.ClientToken
{
    public static class ClientTokenAuthenticationExtensions
    {
        public static AuthenticationBuilder AddClientTokenAuthentication(this AuthenticationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.AddScheme<ClientTokenAuthenticationSchemeOptions, ClientTokenAuthenticationHandler>(
                ClientTokenConstants.YouAuthScheme, op => { });
        }
    }
}