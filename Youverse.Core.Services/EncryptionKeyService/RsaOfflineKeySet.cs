using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.EncryptionKeyService
{
    public class RsaOfflineKeySet
    {
        public Guid Id { get; set; }
        public RsaFullKeyListData Keys { get; set; }
    }
}