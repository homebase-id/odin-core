using System;

namespace Youverse.Core.Services.Authorization.Permissions
{
    [Flags]
    public enum ContactPermissions
    {
        None = 0,

        Create = 1,

        Read = 2,

        Update = 4,

        Delete = 8,

        Manage = Create | Read | Update | Delete
    }
}