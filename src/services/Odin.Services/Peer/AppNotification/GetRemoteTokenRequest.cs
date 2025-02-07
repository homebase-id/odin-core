using System;
using Odin.Core;
using Odin.Core.Identity;

namespace Odin.Services.Peer.AppNotification;

public class GetRemoteTokenRequest
{
    public OdinId Identity { get; set; }
}

public class PeerNotificationSubscription
{
    /// <summary>
    /// A client generated id for the 'collab channel' or community.  (effectively, it is a scope)
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    /// The peer identity to which this identity is subscribed or unsubscribed
    /// </summary>
    public OdinId Identity { get; set; }

    public Guid ToKey()
    {
        return new Guid(
            ByteArrayUtil.ReduceSHA256Hash(ByteArrayUtil.Combine(SubscriptionId.ToByteArray(),
                Identity.ToHashId().ToByteArray())));
    }
}