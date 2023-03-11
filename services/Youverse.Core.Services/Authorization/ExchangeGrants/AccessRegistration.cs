using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    public class AccessRegistration
    {
        public GuidId Id { get; set; }

        public AccessRegistrationClientType AccessRegistrationClientType { get; set; }

        public Int64 Created { get; set; }

        public SymmetricKeyEncryptedXor ClientAccessKeyEncryptedKeyStoreKey { get; set; }

        public SymmetricKeyEncryptedAes AccessKeyStoreKeyEncryptedSharedSecret { get; set; }

        public bool IsRevoked { get; set; }

        /// <summary>
        /// The GrantKeyStoreKey encrypted with this AccessRegistration's key
        /// </summary>
        public SymmetricKeyEncryptedAes AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey { get; set; }

        /// <summary>
        /// Decrypts the grant key store key and shared secret using your Client Auth Token 
        /// </summary>
        /// <returns></returns>
        public (SensitiveByteArray grantKeyStoreKey, SensitiveByteArray sharedSecret) DecryptUsingClientAuthenticationToken(ClientAuthenticationToken authToken)
        {
            var token = authToken.AccessTokenHalfKey;
            var accessKey = this.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref token);
            var sharedSecret = this.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);
            var grantKeyStoreKey = this.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey?.DecryptKeyClone(ref accessKey);
            accessKey.Wipe();

            return (grantKeyStoreKey, sharedSecret);
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