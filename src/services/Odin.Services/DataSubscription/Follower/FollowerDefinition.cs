using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Services.DataSubscription.Follower;

/// <summary/>
public class FollowerDefinition
{
    public OdinId OdinId { get; set; }

    public FollowerNotificationType NotificationType { get; set; }
    public IEnumerable<TargetDrive> Channels { get; set; }
}