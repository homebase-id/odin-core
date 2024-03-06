using System;
using Microsoft.AspNetCore.Authentication;
using Odin.Services.Authentication.Owner;

namespace Odin.Hosting.Authentication.Owner
{
    public static class OwnerAuthenticationExtensions
    {
        public static AuthenticationBuilder AddOwnerAuthentication(this AuthenticationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.AddScheme<OwnerAuthenticationSchemeOptions, OwnerAuthenticationHandler>(
                OwnerAuthConstants.SchemeName,
                op => { });
        }
    }
}