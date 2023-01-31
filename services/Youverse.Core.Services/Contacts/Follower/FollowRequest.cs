using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Contacts.Follower;

public class FollowRequest
{
    public FollowRequest()
    {
    }

    /// <summary>
    /// The identity subscribing
    /// </summary>
    public string DotYouId { get; set; }

    /// <summary>
    /// How the identity wants to be notified
    /// </summary>
    public FollowerNotificationType NotificationType { get; set; }

    /// <summary>
    /// The list of subscribed channels
    /// </summary>
    public IEnumerable<TargetDrive> Channels { get; set; }
}

public class TransitFollowRequest : FollowRequest
{
    /// <summary>
    /// Token used to write to the subscribers feed drive
    /// </summary>
    public byte[] PortableClientAuthToken { get; set; }
}

public class UnfollowRequest
{
    public string DotYouId { get; set; }
}