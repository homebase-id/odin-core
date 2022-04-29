using System;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    //TODO: determine if we can use this instead of an app drive grant for the app registration
    public class DriveGrant
    {
        public Guid DriveAlias { get; set; }
        
        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedStorageKey { get; set; }

        /// <summary>
        /// The type of access allowed for this drive grant
        /// </summary>
        public DrivePermissions Permissions { get; set; }
    }
}