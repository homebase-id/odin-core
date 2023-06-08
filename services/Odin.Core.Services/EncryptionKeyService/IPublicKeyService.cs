using System;
using System.Threading.Tasks;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;

namespace Odin.Core.Services.EncryptionKeyService
{
    public interface IPublicKeyService
    {
        /// <summary>
        /// Gets the latest effective offline public key
        /// </summary>
        /// <returns></returns>
        Task<RsaPublicKeyData> GetOfflinePublicKey();

        /// <summary>
        /// Gets the offline public key matching the specific CRC.
        /// </summary>
        /// <param name="crc"></param>
        /// <returns></returns>
        Task<RsaFullKeyData> GetOfflinePublicKey(UInt32 crc);

        Task<bool> IsValidPublicKey(UInt32 crc);

        Task<(bool, byte[])> DecryptKeyHeaderUsingOfflineKey(byte[] encryptedData, uint publicKeyCrc32, bool failIfNoMatchingPublicKey = true);

        Task<RsaPublicKeyData> GetRecipientOfflinePublicKey(OdinId recipient, bool lookupIfInvalid = true, bool failIfCannotRetrieve = true);

        Task<RsaEncryptedPayload> EncryptPayloadForRecipient(string recipient, byte[] payload);

        Task<(bool, byte[])> DecryptPayloadUsingOfflineKey(RsaEncryptedPayload payload);

        /// <summary>
        /// Destroys the cache item for the recipients public key so a new one will be retrieved
        /// </summary>
        /// <param name="recipient"></param>
        /// <returns></returns>
        Task InvalidatePublicKey(OdinId recipient);
    }
}