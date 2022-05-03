using System;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    public class AccessRegistration
    {
        public Guid Id { get; set; }

        public Guid GrantId { get; set; }
        public UInt64 Created { get; set; }

        public UInt64 LastUsed { get; set; }

        public SymmetricKeyEncryptedXor ClientAccessKeyEncryptedKeyStoreKey { get; set; }

        public SymmetricKeyEncryptedAes AccessKeyStoreKeyEncryptedSharedSecret { get; set; }

        public bool IsRevoked { get; set; }
        
        public SymmetricKeyEncryptedAes AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey { get; set; }

        public void AssertValidRemoteKey(SensitiveByteArray remoteKey)
        {
            this.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref remoteKey);
        }
    }

    public class ClientAccessToken
    {
        public Guid Id { get; set; }
        public SensitiveByteArray AccessTokenHalfKey { get; set; }
        public SensitiveByteArray SharedSecret { get; set; }

        public void Wipe()
        {
            this.AccessTokenHalfKey?.Wipe();
            this.SharedSecret?.Wipe();
        }
    }
}