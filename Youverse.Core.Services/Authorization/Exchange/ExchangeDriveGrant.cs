using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.Exchange
{
    //TODO: determine if we can use this instead of an app drive grant for the app registration
    public class ExchangeDriveGrant
    {
        public Guid DriveAlias { get; set; }
        
        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedStorageKey { get; set; }

    }
}