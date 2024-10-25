using System;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Peer.AppNotification;

public class AppNotificationTokenResponse
{
    public byte[] SharedSecret { get; init; }
    public string AuthenticationToken64 { get; set; }

    public ClientAccessToken ToCat()
    {
        var authToken = ClientAuthenticationToken.FromPortableBytes64(this.AuthenticationToken64);
        return new ClientAccessToken()
        {
            Id = authToken.Id,
            AccessTokenHalfKey = authToken.AccessTokenHalfKey,
            ClientTokenType = authToken.ClientTokenType,
            SharedSecret = SharedSecret.ToSensitiveByteArray()
        };
    }
}