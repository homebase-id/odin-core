using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.DataSubscription.Follower;

/// <summary/>
public class FollowerDefinition
{
    public OdinId OdinId { get; set; }

    public FollowerNotificationType NotificationType { get; set; }
    public IEnumerable<TargetDrive> Channels { get; set; }
}