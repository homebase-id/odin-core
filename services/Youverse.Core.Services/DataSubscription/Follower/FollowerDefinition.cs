using System;
using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.DataSubscription.Follower;

/// <summary/>
public class FollowerDefinition
{
    public OdinId DotYouId { get; set; }

    public FollowerNotificationType NotificationType { get; set; }
    public IEnumerable<TargetDrive> Channels { get; set; }

    public ClientAccessToken CreateClientAccessToken()
    {
        //HACK: shim for follower support
        var guidId = DotYouId.ToHashId();
        return new ClientAccessToken()
        {
            Id = guidId,
            AccessTokenHalfKey = guidId.ToByteArray().ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType.DataProvider,
            SharedSecret =  Guid.Empty.ToByteArray().ToSensitiveByteArray()
        };
    }
}