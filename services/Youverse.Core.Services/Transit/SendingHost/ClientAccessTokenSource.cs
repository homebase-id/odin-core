using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.DataSubscription.Follower;

namespace Youverse.Core.Services.Transit.SendingHost;

/// <summary>
/// Specifies where Transit should get the <see cref="ClientAccessToken"/>
/// </summary>
public enum ClientAccessTokenSource
{
    /// <summary>
    /// Get the recipient's token from the <see cref="CircleNetworkService"/>
    /// </summary>
    Circle = 1,
    
    /// <summary>
    /// Get the recipient's token from the <see cref="FollowerService"/>
    /// </summary>
    DataSubscription = 2
}