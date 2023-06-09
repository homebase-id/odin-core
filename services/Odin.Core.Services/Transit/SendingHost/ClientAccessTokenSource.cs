using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Contacts.Circle.Membership;

namespace Odin.Core.Services.Transit.SendingHost;

/// <summary>
/// Specifies where Transit should get the <see cref="ClientAccessToken"/>
/// </summary>
public enum ClientAccessTokenSource
{
    /// <summary>
    /// Get the recipient's token from the <see cref="CircleNetworkService"/>
    /// </summary>
    Circle = 1,
    
    // /// <summary>
    // /// Get the recipient's token from the <see cref="FollowerService"/> of an identity that follows me
    // /// </summary>
    // Follower = 2,
    //
    // /// <summary>
    // /// Get the recipient's token from the <see cref="FollowerService"/> of an identity I follow
    // /// </summary>
    // IdentityIFollow = 3,
    
    /// <summary>
    /// 
    /// </summary>
    Fallback = 96
}