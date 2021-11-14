using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
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

        public async Task<EncryptedKeyHeader> Encrypt(EncryptedKeyHeader originalHeader, byte[] publicKey)
        {
            //TODO: implement this correctly
            return new EncryptedKeyHeader()
            {
                Type = EncryptionType.Aes,
                Data = new byte[] { 1, 1, 2, 3 }
            };

            // var key = new RsaKeyData()
            // {
            //     publicKey = publicKey
            // };
            //
            // var data = originalHeader.GetKeyBytes();
            // var encryptedData = RsaKeyManagement.Encrypt(key, data, true);
            //
            // return new EncryptedKeyHeader()
            // {
            //     Id = Guid.NewGuid(),
            //     AesKey = Convert.ToBase64String(encryptedData)
            // };
        }

        public async Task<IDictionary<DotYouIdentity, EncryptedKeyHeader>> Encrypt(EncryptedKeyHeader originalHeader, IDictionary<DotYouIdentity, byte[]> recipientPublicKeys)
        {
            var results = new Dictionary<DotYouIdentity, EncryptedKeyHeader>();

            foreach (var kvp in recipientPublicKeys)
            {
                var header = await this.Encrypt(originalHeader, kvp.Value);
                results.Add(kvp.Key, header);
            }

            return results;
        }

        public SecureKey ConvertTransferKeyHeader(byte[] transferEncryptedKeyHeader)
        {
            var sharedSecret = base.Context.AppContext.GetSharedSecret().GetKey();
            //TODO: call decryption suing sharedSecret 
            var bytes = new byte[] { 1, 1, 2, 3, 5, 8, 13, 21 };
            return new SecureKey(bytes);
        }
    }
}