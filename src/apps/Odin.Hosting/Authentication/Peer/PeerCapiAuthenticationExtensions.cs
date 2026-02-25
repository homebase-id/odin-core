using System;
using Microsoft.AspNetCore.Authentication;

namespace Odin.Hosting.Authentication.Peer;

#nullable enable

public static class PeerCapiAuthenticationExtensions
{
    public static AuthenticationBuilder AddPeerCapiAuthentication(this AuthenticationBuilder builder, string schemeName)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.AddScheme<PeerCapiAuthenticationSchemeOptions, PeerCapiAuthenticationHandler>(schemeName, options => { });
    }
}