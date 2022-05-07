using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.EncryptionKeyService
{
    public interface IPublicKeyService
    {
        Task<RsaPublicKeyData> GetOfflinePublicKey();

        Task<byte[]> DecryptUsingOfflineKey(byte[] encryptedData, uint publicKeyCrc32);

        Task<RsaPublicKeyData> GetRecipientOfflinePublicKey(DotYouIdentity recipient, bool lookupIfInvalid = true, bool failIfCannotRetrieve = true);
    }
}