using System;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Services.Authorization.ExchangeGrants
{
    public class ServerHalfOfClientKey
    {
        public GuidId Id { get; set; }

        public AccessRegistrationClientType AccessRegistrationClientType { get; set; }

        public UnixTimeUtc Created { get; set; }

        [JsonPropertyName("clientAccessKeyEncryptedKeyStoreKey")]
        public SymmetricKeyEncryptedXor ServerHalfOfKey { get; set; }

        [JsonPropertyName("accessKeyStoreKeyEncryptedSharedSecret")]
        public SymmetricKeyEncryptedAes ClientKeyEncryptedSharedSecret { get; set; }

        public bool IsRevoked { get; set; }

        /// <summary>
        /// The GrantKeyStoreKey encrypted with this AccessRegistration's key
        /// </summary>
        [JsonPropertyName("accessKeyStoreKeyEncryptedExchangeGrantKeyStoreKey")]
        public SymmetricKeyEncryptedAes ClientKeyEncryptedKeyStoreKey { get; set; }

        /// <summary>
        /// Decrypts the grant key store key and shared secret using your Client Auth Token 
        /// </summary>
        /// <returns></returns>
        public (SensitiveByteArray keyStoreKey, SensitiveByteArray sharedSecret) DecryptUsingClientAuthenticationToken(ClientAuthenticationToken authToken)
        {
            var clientToken = authToken.AccessTokenHalfKey;
            var serverHalfOfKey = this.ServerHalfOfKey.DecryptKeyClone(clientToken);
            var sharedSecret = this.ClientKeyEncryptedSharedSecret.DecryptKeyClone(serverHalfOfKey);
            var keyStoreKey = this.ClientKeyEncryptedKeyStoreKey?.DecryptKeyClone(serverHalfOfKey);
            serverHalfOfKey.Wipe();

            return (keyStoreKey, sharedSecret);
        }

        public void AssertValidRemoteKey(SensitiveByteArray remoteKey)
        {
            this.ServerHalfOfKey.DecryptKeyClone(remoteKey);
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