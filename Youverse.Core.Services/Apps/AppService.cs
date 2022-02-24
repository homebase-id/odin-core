using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class AppService : IAppService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;
        private readonly ISystemStorage _systemStorage;

        public AppService(IDriveService driveService, DotYouContextAccessor contextAccessor, ISystemStorage systemStorage)
        {
            _driveService = driveService;
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
        }

        public async Task<ClientFileHeader> GetClientEncryptedFileHeader(DriveFileId file)
        {
            var ekh = await _driveService.GetEncryptedKeyHeader(file);
            var storageKey = _contextAccessor.GetCurrent().Permissions.GetDriveStorageKey(file.DriveId);
            
            var keyHeader = ekh.DecryptAesToKeyHeader(ref storageKey);
            var clientSharedSecret = _contextAccessor.GetCurrent().AppContext.ClientSharedSecret;
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