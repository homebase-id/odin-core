using System;

namespace Youverse.Core.Services.Drive
{
    [Flags]
    public enum DriveAccess
    {
        Read = 0,
        ReadWrite = 1,
        ManagePermissions = 2,
        All = ManagePermissions  | ReadWrite
    }
}