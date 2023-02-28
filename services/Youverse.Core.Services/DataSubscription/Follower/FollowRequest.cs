using System.Collections.Generic;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.DataSubscription.Follower;

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
}

public class PerimterFollowRequest : FollowRequest
{
    /// <summary>
    /// Token used to write to the subscribers feed drive
    /// </summary>
    public byte[] PortableClientAuthToken { get; set; }
}

public class UnfollowRequest
{
    public string OdinId { get; set; }
}