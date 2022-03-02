using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.Exchange
{
    /// <summary>
    /// Data which allows the 
    /// </summary>
    public class ExchangeRegistration
    {
        public Guid Id { get; set; }
        
        public UInt64 Created { get; set; }

        public SymmetricKeyEncryptedXor HalfKeyEncryptedDriveGrantKey { get; set; }

        public SymmetricKeyEncryptedAes MasterKeyEncryptedDriveGrantKey { get; set; }

        public byte[] KeyStoreKeyEncryptedSharedSecret { get; set; }

        public bool IsRevoked { get; set; }

        public List<ExchangeDriveGrant> DriveGrants { get; set; }
        
        public void AssertValidHalfKey(SensitiveByteArray halfKey)
        {
            var _ = HalfKeyEncryptedDriveGrantKey.DecryptKeyClone(ref halfKey); //this throws exception if half key is invalid
        }

    }
}