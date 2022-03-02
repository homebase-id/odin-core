using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.Exchange
{
    public class ExchangeDriveGrant
    {
        public Guid DriveIdentifier { get; set; }
        
        public SymmetricKeyEncryptedAes XTokenEncryptedStorageKey { get; set; }

    }
}