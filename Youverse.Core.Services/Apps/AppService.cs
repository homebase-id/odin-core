using System.Threading.Tasks;
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
            var ekh = await _driveService.GetEncryptedKeyHeader(file, StorageDisposition.LongTerm);
            var storageKey = _context.AppContext.GetDriveStorageKey(file.DriveId);
            var appEkh = ToAppKeyHeader(ekh, storageKey.GetKey());

            var md = await _driveService.GetMetadata(file, StorageDisposition.LongTerm);

            return new ClientFileHeader()
            {
                EncryptedKeyHeader = appEkh,
                FileMetadata = md
            };
        }

        /// <summary>
        /// Converts a transfer key header to a long term key header and stores it for the specified file.
        /// </summary>
        public async Task<EncryptedKeyHeader> WriteTransferKeyHeader(DriveFileId file, EncryptedKeyHeader transferEncryptedKeyHeader, StorageDisposition storageDisposition)
        {
            var sharedSecret = _context.AppContext.GetClientSharedSecret().GetKey();
            var kh = transferEncryptedKeyHeader.DecryptAesToKeyHeader(sharedSecret);

            return await _driveService.WriteKeyHeader(file, kh, storageDisposition);
        }

        private EncryptedKeyHeader ToAppKeyHeader(EncryptedKeyHeader ekh, byte[] storageKey)
        {
            var keyHeader = ekh.DecryptAesToKeyHeader(storageKey);
            var appEkh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, ekh.Iv, _context.AppContext.GetClientSharedSecret().GetKey());
            return appEkh;
        }
    }
}