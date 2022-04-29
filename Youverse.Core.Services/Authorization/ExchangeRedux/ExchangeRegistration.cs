using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.ExchangeRedux
{
    /// <summary>
    /// A set of drive grants and keys to exchange data with this identity.  This can be used for connections
    /// between two identities or integration with a 3rd party
    /// </summary>
    public class ExchangeRegistration
    {
        public Guid Id { get; set; }

        public UInt64 Created { get; set; }
        public UInt64 LastUsed { get; set; }

        public SymmetricKeyEncryptedXor RemoteKeyEncryptedKeyStoreKey { get; set; }

        public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }

        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedSharedSecret { get; set; }

        public bool IsRevoked { get; set; }

        public List<ExchangeDriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }
        
        public void AssertValidRemoteKey(SensitiveByteArray halfKey)
        {
            var _ = RemoteKeyEncryptedKeyStoreKey.DecryptKeyClone(ref halfKey); //this throws exception if half key is invalid
        }

    }


    public class XTokenRegistration
    {
        public Guid Id { get; set; }

        public UInt64 Created { get; set; }

        public UInt64 LastUsed { get; set; }

        public SymmetricKeyEncryptedXor RemoteKeyEncryptedKeyStoreKey { get; set; }

        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedSharedSecret { get; set; }

        public bool IsRevoked { get; set; }
        
        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedExchangeRegistrationKeyStoreKey { get; set; }

    }


}