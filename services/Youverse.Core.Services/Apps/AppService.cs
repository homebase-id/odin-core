using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit;
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
                    var targetDrive = (await _driveManager.GetDrive(driveId, true)).TargetDriveInfo;

                    var remoteGlobalTransitIdentifier = new GlobalTransitIdFileIdentifier()
                    {
                        GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                        TargetDrive = td
                    };
                    //send the deleted file
                    var map = await _transitService.SendDeleteLinkedFileRequest(remoteGlobalTransitIdentifier,
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

        // public async Task<Dictionary<string, DeleteLinkedFileStatus>> SendDeleteFileRequest(GlobalTransitIdFileIdentifier fileIdentifier, List<string> requestRecipients)
        // {
        //     var recipientStatus = new Dictionary<string, DeleteLinkedFileStatus>();
        //
        //     var recipients = requestRecipients ?? new List<string>();
        //     if (recipients.Any())
        //     {
        //         //send the deleted file
        //         var map = await _transitService.SendDeleteLinkedFileRequest(file.DriveId, header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
        //             new SendFileOptions()
        //             {
        //                 FileSystemType = header.ServerMetadata.FileSystemType,
        //                 TransferFileType = TransferFileType.Normal,
        //                 ClientAccessTokenSource = ClientAccessTokenSource.Circle
        //             },
        //             recipients);
        //
        //         foreach (var (key, value) in map)
        //         {
        //             switch (value)
        //             {
        //                 case TransitResponseCode.AcceptedIntoInbox:
        //                     recipientStatus.Add(key, DeleteLinkedFileStatus.RequestAccepted);
        //                     break;
        //
        //                 case TransitResponseCode.Rejected:
        //                 case TransitResponseCode.QuarantinedPayload:
        //                 case TransitResponseCode.QuarantinedSenderNotConnected:
        //                     recipientStatus.Add(key, DeleteLinkedFileStatus.RequestRejected);
        //                     break;
        //
        //                 default:
        //                     throw new YouverseSystemException($"Unknown TransitResponseCode {value}");
        //             }
        //         }
        //     }
        //
        //     return recipientStatus;
        // }
    }
}