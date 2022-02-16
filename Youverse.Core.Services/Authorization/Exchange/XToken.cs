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

        public byte[] SharedSecretKey { get; set; }

        public bool IsRevoked { get; set; }

        public List<DriveKey> DriveKeys { get; set; }

    }

    public class DriveKey
    {
        public Guid DriveId { get; set; }
        
        public SymmetricKeyEncryptedAes XTokenEncryptedStorageKey { get; set; }

    }

}