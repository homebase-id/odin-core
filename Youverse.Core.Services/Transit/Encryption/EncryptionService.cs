using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit.Encryption
{
    public class EncryptionService : IEncryptionService
    {
        private readonly DotYouContext _context;
        private readonly ISystemStorage _systemStorage;
        public EncryptionService(DotYouContext context, ILogger<IEncryptionService> logger, ISystemStorage systemStorage)
        {
            _context = context;
            _systemStorage = systemStorage;
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

        public async Task<EncryptedKeyHeader> ConvertTransferKeyHeaderStream(Stream data)
        {
            string json = await new StreamReader(data).ReadToEndAsync();
            var transferEncryptedKeyHeader = JsonConvert.DeserializeObject<EncryptedKeyHeader>(json);

            if (null == transferEncryptedKeyHeader)
            {
                throw new InvalidDataException("Stream returned null EncryptedKeyHeader");
            }
            
            var sharedSecret = _context.AppContext.GetSharedSecret().GetKey();
            var kh = transferEncryptedKeyHeader.DecryptAesToKeyHeader(sharedSecret);
            
            var appEncryptionKey = _context.AppContext.GetAppEncryptionKey().GetKey();
            return EncryptedKeyHeader.EncryptKeyHeaderAes(kh, transferEncryptedKeyHeader.Iv, appEncryptionKey);
        }
    }
}