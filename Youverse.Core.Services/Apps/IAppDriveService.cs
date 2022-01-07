using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    /// <summary>
    /// App specific functions for serving files from the <see cref="IDriveService"/> 
    /// </summary>
    public interface IAppDriveService
    {
    }

    public class AppDriveService : IAppDriveService
    {
        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;

        public AppDriveService(IDriveService driveService, DotYouContext context)
        {
            _driveService = driveService;
            _context = context;
        }

        public async Task<KeyHeaderResponse> GetKeyHeader(DriveFileId file)
        {
            var ekh = await _driveService.GetEncryptedKeyHeader(file, StorageDisposition.LongTerm);

            var storageKey = _context.AppContext.GetDriveStorageKey(file.DriveId);
            var keyHeader = ekh.DecryptAesToKeyHeader(storageKey.GetKey());

            var appEkh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, ekh.Iv, _context.AppContext.GetDeviceSharedSecret().GetKey());

            return new KeyHeaderResponse()
            {
                Iv = ekh.Iv,
                EncryptedKeyHeader = appEkh
            };
        }

        public async Task<ClientFileHeader> GetHeader(DriveFileId file)
        {
            var md = await _driveService.GetMetadata(file, StorageDisposition.LongTerm);
            var kh = await this.GetKeyHeader(file);
            
            return  new ClientFileHeader()
            {
                FileMetadata = md,
                KeyHeaderResponse = kh
            };
        }
    }
    
    public class ClientFileHeader
    {
        public KeyHeaderResponse KeyHeaderResponse { get; set; }
        
        public FileMetadata FileMetadata { get; set; }
    }
}