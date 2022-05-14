using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Requests;

namespace Youverse.Core.Services.EncryptionKeyService
{
    public interface IPublicKeyService
    {
        /// <summary>
        /// Gets the latest effective offlie public key
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

        Task<byte[]> DecryptKeyHeaderUsingOfflineKey(byte[] encryptedData, uint publicKeyCrc32, bool failIfNoMatchingPublicKey = true);

        Task<RsaPublicKeyData> GetRecipientOfflinePublicKey(DotYouIdentity recipient, bool lookupIfInvalid = true, bool failIfCannotRetrieve = true);

        Task<RsaEncryptedPayload> EncryptPayloadForRecipient(string recipient, byte[] payload);

        Task<byte[]> DecryptPayloadUsingOfflineKey(RsaEncryptedPayload payload);
    }
}