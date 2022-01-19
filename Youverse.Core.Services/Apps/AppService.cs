using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class AppService : IAppService
    {
        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;
        private readonly ISystemStorage _systemStorage;

        public AppService(IDriveService driveService, DotYouContext context, ISystemStorage systemStorage)
        {
            _driveService = driveService;
            _context = context;
            _systemStorage = systemStorage;
        }

        public async Task<ClientFileHeader> GetClientEncryptedFileHeader(DriveFileId file)
        {
            var ekh = await _driveService.GetEncryptedKeyHeader(file);
            var storageKey = _context.AppContext.GetDriveStorageKey(file.DriveId);
            
            var keyHeader = ekh.DecryptAesToKeyHeader(ref storageKey);
            var clientSharedSecret = _context.AppContext.GetClientSharedSecret();
            var appEkh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, ekh.Iv, ref clientSharedSecret);

            var md = await _driveService.GetMetadata(file);

            return new ClientFileHeader()
            {
                EncryptedKeyHeader = appEkh,
                FileMetadata = md
            };
        }
    }
}