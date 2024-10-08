using System;

namespace Odin.Services.Drives
{
    //Note there is a separate Write permission as we have scenarios where we want to receive
    //but not let data be read; i.e. best buy sending me purchase receipts.  They cannot read
    //my other data

    /// <summary>
    /// Permissions for operations on a drive.
    /// </summary>
    [Flags]
    public enum DrivePermission
    {
        None = 0,

        /// <summary>
        /// Can read data on a drive
        /// </summary>
        Read = 1,

        /// <summary>
        /// Write normal files.  This means only write.  Use DrivePermissions.ReadWrite if you want to check both at once
        /// </summary>
        Write = 2,

        React = 4,

        Comment = 8,

        /// <summary>
        /// Write comments and reactions
        /// </summary>
        WriteReactionsAndComments = React | Comment,

        ReadWrite = Read | Write | Comment | React,

        All = ReadWrite
    }
}