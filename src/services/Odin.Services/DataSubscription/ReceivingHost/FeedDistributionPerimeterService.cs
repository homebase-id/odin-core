using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.DataSubscription.ReceivingHost
{
    public class FeedDistributionPerimeterService(
        IDriveFileSystem fileSystem,
        FileSystemResolver fileSystemResolver,
        FollowerService followerService,
        IMediator mediator)
    {
        public async Task<PeerTransferResponse> AcceptUpdatedReactionPreview(UpdateReactionSummaryRequest request, OdinContext odinContext)
        {
            await followerService.AssertTenantFollowsTheCaller();

            //S0510
            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                throw new OdinClientException("Invalid drive specified for reaction preview update");
            }

            using (new FeedDriveDistributionSecurityContext(contextAccessor))
            {
                var fileId = await this.ResolveInternalFile(request.FileId);

                if (null == fileId)
                {
                    throw new OdinClientException("Invalid File");
                }

                await fileSystem.Storage.UpdateReactionPreviewOnFeedDrive(fileId.Value, request.ReactionPreview, odinContext);
            }

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<PeerTransferResponse> AcceptUpdatedFileMetadata(UpdateFeedFileMetadataRequest request, OdinContext odinContext)
        {
            await followerService.AssertTenantFollowsTheCaller();
            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                throw new OdinClientException("Target drive must be the feed drive");
            }

            using (new FeedDriveDistributionSecurityContext(contextAccessor))
            {
                var driveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);

                var fileId = await this.ResolveInternalFile(request.FileId);

                if (null == fileId)
                {
                    //new file
                    var internalFile = await fileSystem.Storage.CreateInternalFileId(driveId);

                    var keyHeader = KeyHeader.Empty();
                    var serverMetadata = new ServerMetadata()
                    {
                        AccessControlList = AccessControlList.OwnerOnly,
                        AllowDistribution = false,
                    };

                    request.FileMetadata.SenderOdinId = odinContext.GetCallerOdinIdOrFail();
                    var serverFileHeader =
                        await fileSystem.Storage.CreateServerFileHeader(internalFile, keyHeader, request.FileMetadata, serverMetadata, odinContext);
                    await fileSystem.Storage.UpdateActiveFileHeader(internalFile, serverFileHeader, raiseEvent: true, odinContext: odinContext);

                    await mediator.Publish(new NewFeedItemReceived()
                    {
                        Sender = odinContext.GetCallerOdinIdOrFail(),
                    });
                }
                else
                {
                    // perform update
                    request.FileMetadata.SenderOdinId = odinContext.GetCallerOdinIdOrFail();
                    await fileSystem.Storage.ReplaceFileMetadataOnFeedDrive(fileId.Value, request.FileMetadata, odinContext);
                }
            }

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<PeerTransferResponse> Delete(DeleteFeedFileMetadataRequest request, OdinContext odinContext)
        {
            await followerService.AssertTenantFollowsTheCaller();
            using (new FeedDriveDistributionSecurityContext(contextAccessor))
            {
                var fileId = await this.ResolveInternalFile(request.FileId);
                if (null == fileId)
                {
                    //TODO: what's the right status code here
                    return new PeerTransferResponse()
                    {
                        Code = PeerResponseCode.AcceptedDirectWrite
                    };
                }

                await fileSystem.Storage.RemoveFeedDriveFile(fileId.Value, odinContext);

                return new PeerTransferResponse()
                {
                    Code = PeerResponseCode.AcceptedDirectWrite
                };
            }
        }

        /// <summary>
        /// Looks up a file by a global transit identifier
        /// </summary>
        private async Task<InternalDriveFileId?> ResolveInternalFile(GlobalTransitIdFileIdentifier file)
        {
            var (_, fileId) = await fileSystemResolver.ResolveFileSystem(file, tryCommentDrive: false);
            return fileId;
        }
    }
}