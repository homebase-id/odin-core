using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    public class AccessRegistration
    {
        public ByteArrayId Id { get; set; }

        public AccessRegistrationClientType AccessRegistrationClientType { get; set; }
        
        public UInt64 Created { get; set; }

        public SymmetricKeyEncryptedXor ClientAccessKeyEncryptedKeyStoreKey { get; set; }

        public SymmetricKeyEncryptedAes AccessKeyStoreKeyEncryptedSharedSecret { get; set; }

        public bool IsRevoked { get; set; }

        /// <summary>
        /// The GrantKeyStoreKey encrypted with this AccessRegistration's key
        /// </summary>
        public SymmetricKeyEncryptedAes AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey { get; set; }

        /// <summary>
        /// Decrypts the grant key store key using your AccessKey; returns null if this AccessRegistration is bound to an 
        /// </summary>
        /// <param name="accessKey"></param>
        /// <returns></returns>
        public SensitiveByteArray GetGrantKeyStoreKey(SensitiveByteArray accessKey)
        {
            var grantKeyStoreKey = this.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey?.DecryptKeyClone(ref accessKey);
            return grantKeyStoreKey;
        }

        public void AssertValidRemoteKey(SensitiveByteArray remoteKey)
        {
            this.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref remoteKey);
        }
    }

    /// <summary>
    /// Specifies the type of client using the AccessRegistration.  This lets us reduce permissions/access for browsers, apps, or 3rd party callers.
    /// </summary>
    public enum AccessRegistrationClientType
    {
        Cookies = 1,
        Other = 99
    }
}