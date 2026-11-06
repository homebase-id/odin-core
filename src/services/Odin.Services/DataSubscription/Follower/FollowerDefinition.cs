using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Services.DataSubscription.Follower;

/// <summary/>
public class FollowerDefinition
{
    public OdinId OdinId { get; set; }

    public FollowerNotificationType NotificationType { get; set; }
    public IEnumerable<TargetDrive> Channels { get; set; }

    /// <summary>When the follow was made or last changed: following again, or changing channels, replaces it.</summary>
    public UnixTimeUtc? FollowedAt { get; set; }
}

/// <summary>An identity in a follow list, and when that follow was made or last changed.</summary>
public class FollowEntry
{
    public string OdinId { get; set; }

    public UnixTimeUtc? FollowedAt { get; set; }
}