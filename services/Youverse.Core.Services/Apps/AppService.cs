using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class AppService : IAppService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ITransitService _transitService;

        private readonly StandardFileSystem _fileSystem;
        public AppService(DotYouContextAccessor contextAccessor, ITransitService transitService, StandardFileSystem fileSystem)
        {
            _contextAccessor = contextAccessor;
            _transitService = transitService;
            _fileSystem = fileSystem;
        }

        public async Task<ClientFileHeader> GetClientEncryptedFileHeader(InternalDriveFileId file)
        {
            var header = await _fileSystem.Storage.GetServerFileHeader(file);

            if (header == null)
            {
                return null;
            }

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

            var clientFileHeader = new ClientFileHeader()
            {
                FileId = header.FileMetadata.File.FileId,
                FileState = header.FileMetadata.FileState,
                SharedSecretEncryptedKeyHeader = sharedSecretEncryptedKeyHeader,
                FileMetadata = RedactFileMetadata(header.FileMetadata),
                Priority = priority
            };

            //add additional info
            if (_contextAccessor.GetCurrent().Caller.IsOwner)
            {
                clientFileHeader.ServerMetadata = header.ServerMetadata;
            }

            return clientFileHeader;
        }

        public async Task<DeleteLinkedFileResult> DeleteFile(InternalDriveFileId file, List<string> requestRecipients)
        {
            var result = new DeleteLinkedFileResult()
            {
                RecipientStatus = new Dictionary<string, DeleteLinkedFileStatus>(),
                LocalFileDeleted = false
            };

            var header = await _fileSystem.Storage.GetServerFileHeader(file);
            if (header == null)
            {
                result.LocalFileNotFound = true;
                return result;
            }

            var recipients = requestRecipients ?? new List<string>();
            if (recipients.Any())
            {
                if (header.FileMetadata.GlobalTransitId.HasValue)
                {
                    //send the deleted file
                    var map = await _transitService.SendDeleteLinkedFileRequest(file.DriveId, header.FileMetadata.GlobalTransitId.GetValueOrDefault(), recipients);

                    foreach (var (key, value) in map)
                    {
                        switch (value)
                        {
                            case TransitResponseCode.Accepted:
                                result.RecipientStatus.Add(key, DeleteLinkedFileStatus.RequestAccepted);
                                break;

                            case TransitResponseCode.Rejected:
                            case TransitResponseCode.QuarantinedPayload:
                            case TransitResponseCode.QuarantinedSenderNotConnected:
                                result.RecipientStatus.Add(key, DeleteLinkedFileStatus.RequestRejected);
                                break;

                            default:
                                throw new YouverseSystemException($"Unknown TransitResponseCode {value}");
                        }
                    }
                }
            }

            await _fileSystem.Storage.SoftDeleteLongTermFile(file);
            result.LocalFileDeleted = true;

            return result;
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