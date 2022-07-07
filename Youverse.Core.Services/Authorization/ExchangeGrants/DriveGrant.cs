using System;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    public class DriveGrant
    {
        /// <summary>
        /// The internal drive id being granted access
        /// </summary>
        public Guid DriveId { get; set; }

        public Guid DriveAlias { get; set; }

        public Guid DriveType { get; set; }
        
        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedStorageKey { get; set; }

        /// <summary>
        /// The type of access allowed for this drive grant
        /// </summary>
        public DrivePermission Permission { get; set; }
    }
}