using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit
{
    public class EncryptionService : DotYouServiceBase, IEncryptionService
    {
        public EncryptionService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
        }

        public async Task<KeyHeader> Encrypt(KeyHeader originalHeader, byte[] publicKey)
        {
            var key = new RsaKeyData()
            {
                publicKey = publicKey
            };

            var data = originalHeader.GetKeyBytes();
            var encryptedData = RsaKeyManagement.Encrypt(key, data, true);

            return new KeyHeader()
            {
                Id = Guid.NewGuid(),
                EncryptedKey64 = Convert.ToBase64String(encryptedData)
            };
        }

        public async Task<IDictionary<DotYouIdentity, KeyHeader>> Encrypt(KeyHeader originalHeader, IDictionary<DotYouIdentity, byte[]> recipientPublicKeys)
        {
            var results = new Dictionary<DotYouIdentity, KeyHeader>();

            foreach (var kvp in recipientPublicKeys)
            {
                var header = await this.Encrypt(originalHeader, kvp.Value);
                results.Add(kvp.Key, header);
            }

            return results;
        }
    }
}