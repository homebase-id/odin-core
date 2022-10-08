using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
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

            int priority = 1000;

            //TODO: this a strange place to calculate priority yet it was the best place w/o having to send back the acl outside of this method
            switch (header.ServerMetadata.AccessControlList.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    priority = 500;
                    break;
                case SecurityGroupType.Authenticated:
                    priority = 400;
                    break;
                case SecurityGroupType.Connected:
                    priority = 300;
                    break;
                case SecurityGroupType.Owner:
                    priority = 1;
                    break;
            }

            if (_contextAccessor.GetCurrent().Caller.IsOwner)
            {
                return new ClientFileHeader()
                {
                    FileId = header.FileMetadata.File.FileId,
                    SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
                    FileMetadata = RedactFileMetadata(header.FileMetadata),
                    ServerMetadata = header.ServerMetadata,
                    Priority = priority
                };
            }

            return new ClientFileHeader()
            {
                FileId = header.FileMetadata.File.FileId,
                SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
                FileMetadata = RedactFileMetadata(header.FileMetadata),
                Priority = priority
            };
        }

        private ClientFileMetadata RedactFileMetadata(FileMetadata fileMetadata)
        {
            var clientFile = new ClientFileMetadata
            {
                Created = fileMetadata.Created,
                Updated = fileMetadata.Updated,
                AppData = fileMetadata.AppData,
                ContentType = fileMetadata.ContentType,
                GlobalTransitId = fileMetadata.GlobalTransitId,
                PayloadSize = fileMetadata.PayloadSize,
                OriginalRecipientList = fileMetadata.OriginalRecipientList,
                PayloadIsEncrypted = fileMetadata.PayloadIsEncrypted,
                SenderDotYouId = fileMetadata.SenderDotYouId
            };
            return clientFile;
        }
    }
}