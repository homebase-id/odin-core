using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.EncryptionKeyService
{
    public class RecipientPublicKeyCacheItem
    {
        public Guid Id { get; set; } //recipient dotYouId as a guid

        public RsaPublicKeyData PublicKeyData { get; set; }
    }
}