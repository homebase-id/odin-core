using System.IO;
using System.Runtime;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class AppService : IAppService
    {
        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;

        public AppService(IDriveService driveService, DotYouContext context)
        {
            _driveService = driveService;
            _context = context;
        }

        public async Task<ClientFileHeader> GetDeviceEncryptedFileHeader(DriveFileId file)
        {
            var ekh = await _driveService.GetEncryptedKeyHeader(file);
            var storageKey = _context.AppContext.GetDriveStorageKey(file.DriveId);
            var appEkh = ToAppKeyHeader(ekh, storageKey.GetKey());

            var md = await _driveService.GetMetadata(file);

            return new ClientFileHeader()
            {
                EncryptedKeyHeader = appEkh,
                FileMetadata = md
            };
        }

        public async Task<EncryptedKeyHeader> WriteTransferKeyHeader(DriveFileId file, Stream stream)
        {
            string json = await new StreamReader(stream).ReadToEndAsync();
            var transferKeyHeader = JsonConvert.DeserializeObject<EncryptedRecipientTransferKeyHeader>(json);

            return await this.WriteTransferKeyHeader(file, transferKeyHeader);
        }

        /// <summary>
        /// Converts a transfer key header to a long term key header and stores it for the specified file.
        /// </summary>
        public async Task<EncryptedKeyHeader> WriteTransferKeyHeader(DriveFileId file, EncryptedKeyHeader transferEncryptedKeyHeader)
        {
            var sharedSecret = _context.AppContext.GetClientSharedSecret().GetKey();
            var kh = transferEncryptedKeyHeader.DecryptAesToKeyHeader(sharedSecret);
            return await _driveService.WriteKeyHeader(file, kh);
        }

        private EncryptedKeyHeader ToAppKeyHeader(EncryptedKeyHeader ekh, byte[] storageKey)
        {
            var keyHeader = ekh.DecryptAesToKeyHeader(storageKey);
            var appEkh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, ekh.Iv, _context.AppContext.GetClientSharedSecret().GetKey());
            return appEkh;
        }
    }
}