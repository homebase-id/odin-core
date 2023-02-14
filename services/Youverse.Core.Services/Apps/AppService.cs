using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core;
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

            return Utility.ConvertToSharedSecretEncryptedClientFileHeader(header, _contextAccessor);
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