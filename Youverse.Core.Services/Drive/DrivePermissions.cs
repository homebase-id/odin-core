using System;

namespace Youverse.Core.Services.Drive
{
    //Note there is a separate Write permission as we have scenarios where we want to receive
    //but not let data be read; i.e. best buy sending me purchase receipts.  They cannot read
    //my other data
    
    /// <summary>
    /// Permissions for operations on a drive.
    /// </summary>
    [Flags]
    public enum DrivePermissions
    {
        /// <summary>
        /// Can read data on a drive
        /// </summary>
        Read = 0,
        
        /// <summary>
        /// Write permissions.  This means only write.  Use DrivePermissions.ReadWrite if you want to check both at once
        /// </summary>
        Write = 1,
        
        ReadWrite = Read | Write,
        
        // ManagePermissions = 2,
        // All = ManagePermissions  | ReadWrite
        All = ReadWrite
    }
}