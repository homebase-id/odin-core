using System;

namespace Youverse.Core.Services.Drive.Security
{
    [Flags]
    public enum Permission
    {
        Revoked = 0,
        Read = 32
    }
}