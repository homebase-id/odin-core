using System;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Services.Authorization.ExchangeGrants
{
    public class AccessRegistration
    {
        public GuidId Id { get; set; }

        public AccessRegistrationClientType AccessRegistrationClientType { get; set; }

        public UnixTimeUtc Created { get; set; }

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
        public (SensitiveByteArray keyStoreKey, SensitiveByteArray sharedSecret) DecryptUsingClientAuthenticationToken(ClientAuthenticationToken authToken)
        {
            var token = authToken.AccessTokenHalfKey;
            var accessKey = this.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(token);
            var sharedSecret = this.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(accessKey);
            var grantKeyStoreKey = this.AccessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey?.DecryptKeyClone(accessKey);
            accessKey.Wipe();

            return (grantKeyStoreKey, sharedSecret);
        }

        public void AssertValidRemoteKey(SensitiveByteArray remoteKey)
        {
            this.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(remoteKey);
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