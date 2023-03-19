using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.SendingHost;

namespace Youverse.Core.Services.Apps
{
    public class AppService : IAppService
    {
        private readonly ITransitService _transitService;

        private readonly FileSystemResolver _fileSystemResolver;

        public AppService(ITransitService transitService, FileSystemResolver fileSystemResolver)
        {
            _transitService = transitService;
            _fileSystemResolver = fileSystemResolver;
        }

        public async Task<DeleteLinkedFileResult> DeleteFile(InternalDriveFileId file, List<string> requestRecipients)
        {
            var result = new DeleteLinkedFileResult()
            {
                RecipientStatus = new Dictionary<string, DeleteLinkedFileStatus>(),
                LocalFileDeleted = false
            };

            var fs = _fileSystemResolver.ResolveFileSystem(file);

            var header = await fs.Storage.GetServerFileHeader(file);
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
                    var map = await _transitService.SendDeleteLinkedFileRequest(file.DriveId, header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                        new SendFileOptions()
                        {
                            FileSystemType = header.ServerMetadata.FileSystemType,
                            TransferFileType = TransferFileType.Normal,
                            ClientAccessTokenSource = ClientAccessTokenSource.Circle
                        },
                        recipients);

                    foreach (var (key, value) in map)
                    {
                        switch (value)
                        {
                            case TransitResponseCode.AcceptedIntoInbox:
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

            await fs.Storage.SoftDeleteLongTermFile(file);
            result.LocalFileDeleted = true;

            return result;
        }
    }
}