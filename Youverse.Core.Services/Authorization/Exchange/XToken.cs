using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.Exchange
{
    public class XToken
    {
        public Guid Id { get; set; }
        
        public UInt64 Created { get; set; }

        public SymmetricKeyEncryptedXor DriveKeyHalfKey { get; set; }

        public SymmetricKeyEncryptedAes MasterKeyEncryptedDriveKey { get; set; }

        public byte[] ClientSharedSecretKey { get; set; }

        public bool IsRevoked { get; set; }

        public List<XTokenDriveGrant> DriveGrants { get; set; }

        public void AssertValidHalfKey(SensitiveByteArray halfKey)
        {
            var _ = DriveKeyHalfKey.DecryptKeyClone(ref halfKey); //this throws exception if half key is invalid
        }

    }

    public class XTokenDriveGrant
    {
        public Guid DriveId { get; set; }
        
        public SymmetricKeyEncryptedAes XTokenEncryptedStorageKey { get; set; }

    }

}