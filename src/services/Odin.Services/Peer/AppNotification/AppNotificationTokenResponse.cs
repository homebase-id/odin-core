using System;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Peer.AppNotification;

public class AppNotificationTokenResponse
{
    public Guid Id { get; init; }
    public byte[] AccessTokenHalfKey { get; init; }

    public ClientTokenType ClientTokenType { get; init; }

    public byte[] SharedSecret { get; init; }

    public ClientAccessToken ToCat()
    {
        return new ClientAccessToken()
        {
            Id = Id,
            AccessTokenHalfKey = AccessTokenHalfKey.ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType,
            SharedSecret = SharedSecret.ToSensitiveByteArray()
        };
    }
}