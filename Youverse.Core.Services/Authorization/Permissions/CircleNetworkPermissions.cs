using System;

namespace Youverse.Core.Services.Authorization.Permissions
{
    [Flags]
    public enum CircleNetworkPermissions
    {
        None = 0,

        Connect = 1,

        Read = 2,

        Update = 4,

        Delete = 8,
        
        Manage = Connect | Read | Update | Delete
        
    }
}