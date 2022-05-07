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
        Task<RsaPublicKeyData> GetOfflinePublicKey();

        Task<byte[]> DecryptKeyHeaderUsingOfflineKey(byte[] encryptedData, uint publicKeyCrc32);

        Task<RsaPublicKeyData> GetRecipientOfflinePublicKey(DotYouIdentity recipient, bool lookupIfInvalid = true, bool failIfCannotRetrieve = true);

        Task<RsaEncryptedPayload> EncryptPayloadForRecipient(string recipient, byte[] payload);

        Task<byte[]> DecryptPayloadUsingOfflineKey(RsaEncryptedPayload payload);
    }
}