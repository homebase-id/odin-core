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
        public UInt64 LastUsed { get; set; }

        public SymmetricKeyEncryptedXor RemoteKeyEncryptedKeyStoreKey { get; set; }

        public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }

        public byte[] KeyStoreKeyEncryptedSharedSecret { get; set; }

        public bool IsRevoked { get; set; }

        public List<ExchangeDriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }
        
        public List<Guid> CircleGrants { get; set; }
        
        public void AssertValidHalfKey(SensitiveByteArray halfKey)
        {
            var _ = RemoteKeyEncryptedKeyStoreKey.DecryptKeyClone(ref halfKey); //this throws exception if half key is invalid
        }

    }


    public class ChildExchangeRegistration
    {
        public Guid Id { get; set; }

        public UInt64 Created { get; set; }

        public UInt64 LastUsed { get; set; }

        public SymmetricKeyEncryptedXor RemoteKeyEncryptedKeyStoreKey { get; set; }

        public byte[] KeyStoreKeyEncryptedSharedSecret { get; set; }

        public bool IsRevoked { get; set; }

        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedParentXTokenKeyStoreKey { get; set; }

    }


}