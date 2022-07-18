using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class AppService : IAppService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;

        public AppService(IDriveService driveService, DotYouContextAccessor contextAccessor)
        {
            _driveService = driveService;
            _contextAccessor = contextAccessor;
        }

        public async Task<ClientFileHeader> GetClientEncryptedFileHeader(InternalDriveFileId file)
        {
            var header = await _driveService.GetServerFileHeader(file);

            // //HACK: waiting for indexer to be updated to include payload is encrypted flag
            // if (header.FileMetadata.PayloadIsEncrypted && _contextAccessor.GetCurrent().Caller.SecurityLevel == SecurityGroupType.Anonymous)
            // {
            //     //HACK: skip this file in the search results since anonymous users cannot decrypt files
            // }

            EncryptedKeyHeader sharedSecretEncryptedKeyHeader;
            if (header.FileMetadata.PayloadIsEncrypted)
            {
                var storageKey = _contextAccessor.GetCurrent().PermissionsContext.GetDriveStorageKey(file.DriveId);
                var keyHeader = header.EncryptedKeyHeader.DecryptAesToKeyHeader(ref storageKey);
                var clientSharedSecret = _contextAccessor.GetCurrent().PermissionsContext.SharedSecretKey;
                sharedSecretEncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, header.EncryptedKeyHeader.Iv, ref clientSharedSecret);
            }
            else
            {
                sharedSecretEncryptedKeyHeader = EncryptedKeyHeader.Empty();
            }


            if (_contextAccessor.GetCurrent().Caller.IsOwner)
            {
                return new ClientFileHeader()
                {
                    SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
                    FileMetadata = header.FileMetadata,
                    ServerMetadata = header.ServerMetadata
                };
            }

            return new ClientFileHeader()
            {
                SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
                FileMetadata = header.FileMetadata
            };
        }
    }
}