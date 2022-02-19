using System;

namespace Youverse.Core.Services.Authorization.Permissions
{
    [Flags]
    public enum CircleNetworkRequestPermissions
    {
        None = 0,

        CreateOrSend = 1,

        Read = 2,

        Update = 4,

        Delete = 8,
        
        Manage = CreateOrSend | Read | Update | Delete
        
    }
}