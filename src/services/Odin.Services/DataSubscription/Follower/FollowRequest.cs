using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Services.DataSubscription.Follower;

public class FollowRequest
{
    /// <summary>
    /// The identity subscribing
    /// </summary>
    public string OdinId { get; set; }

    /// <summary>
    /// How the identity wants to be notified
    /// </summary>
    public FollowerNotificationType NotificationType { get; set; }

    /// <summary>
    /// The list of subscribed channels
    /// </summary>
    public IEnumerable<TargetDrive> Channels { get; set; }

    /// <summary>
    /// Indicates the feed should be updated with the history
    /// </summary>
    public bool SynchronizeFeedHistoryNow { get; set; } = true;
}

public class PerimeterFollowRequest : FollowRequest
{
    
}

public class UnfollowRequest
{
    public string OdinId { get; set; }
}

public class SynchronizeFeedHistoryRequest
{
    public string OdinId { get; set; }
}