using System;

namespace Youverse.Core.Identity.DataAttribute
{
    [Flags]
    public enum IdentityRights
    {
        None = 0,
        Connect = 1,    // Allow authenticated, non-friend users to send you a "friend request"
        Follow = 2,     // Allow authenticated, non-friend users to follow your public posts
        Message = 4,    // Allow authenticated, non-friend users to message you (=> spam - even allow=)
        All = Connect + Follow
    }
}