using System;
using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.DataSubscription.Follower;

/// <summary/>
public class FollowerDefinition
{
    public OdinId OdinId { get; set; }

    public FollowerNotificationType NotificationType { get; set; }
    public IEnumerable<TargetDrive> Channels { get; set; }
    
}