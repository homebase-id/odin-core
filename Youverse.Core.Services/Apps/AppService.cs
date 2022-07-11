using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.SystemStorage;

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

        public async Task<ClientFileHeader> GetClientEncryptedFileHeader(InternalDriveFileId file)
        {
            var header = await _driveService.GetServerFileHeader(file);

            KeyHeader keyHeader;
            if (header.FileMetadata.PayloadIsEncrypted)
            {
                var storageKey = _contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(file.DriveId);
                keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
            }
            else
            {
                keyHeader = KeyHeader.Empty();
            }

            var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
            var appEkh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, header.EncryptedKeyHeader.Iv, ref clientSharedSecret);

           
            if (_contextAccessor.GetCurrent().Caller.IsOwner)
            {
                return new ClientFileHeader()
                {
                    EncryptedKeyHeader = appEkh,
                    FileMetadata = header.FileMetadata, 
                    ServerMetadata = header.ServerMetadata
                };
            }
            
            return new ClientFileHeader()
            {
                EncryptedKeyHeader = appEkh,
                FileMetadata = header.FileMetadata
            };

        }
    }
}