using System;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Authorization.Apps
{
    /// <summary>
    /// Specifies a permissions granted to an app for a given drive
    /// </summary>
    public class AppDriveGrant
    {
        public Guid DriveAlias { get; set; }

        public Guid DriveId { get; set; }
        
        public SymmetricKeyEncryptedAes AppKeyEncryptedStorageKey { get; set; }
        
        /// <summary>
        /// The type of access allowed for this drive grant
        /// </summary>
        public DrivePermission Permission { get; set; }
    }
}