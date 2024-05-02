using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite;
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
        public async Task<PeerTransferResponse> AcceptUpdatedReactionPreview(UpdateReactionSummaryRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            await followerService.AssertTenantFollowsTheCaller(odinContext, cn);

            //S0510
            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                throw new OdinClientException("Invalid drive specified for reaction preview update");
            }

            var newContext = OdinContextUpgrades.UpgradeToReadFollowersForDistribution(odinContext);
            {
                var fileId = await this.ResolveInternalFile(request.FileId, newContext, cn);

                if (null == fileId)
                {
                    throw new OdinClientException("Invalid File");
                }

                await fileSystem.Storage.UpdateReactionPreviewOnFeedDrive(fileId.Value, request.ReactionPreview, newContext, cn);
            }

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<PeerTransferResponse> AcceptUpdatedFileMetadata(UpdateFeedFileMetadataRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            await followerService.AssertTenantFollowsTheCaller(odinContext, cn);
            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                throw new OdinClientException("Target drive must be the feed drive");
            }

            var newContext = OdinContextUpgrades.UpgradeToReadFollowersForDistribution(odinContext);
            {
                var driveId = odinContext.PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);

                var fileId = await this.ResolveInternalFile(request.FileId, newContext, cn);

                if (null == fileId)
                {
                    //new file
                    var internalFile = await fileSystem.Storage.CreateInternalFileId(driveId, cn);

                    var keyHeader = KeyHeader.Empty();
                    var serverMetadata = new ServerMetadata()
                    {
                        AccessControlList = AccessControlList.OwnerOnly,
                        AllowDistribution = false,
                    };

                    request.FileMetadata.SenderOdinId = odinContext.GetCallerOdinIdOrFail();
                    var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(
                        internalFile, keyHeader, request.FileMetadata, serverMetadata, newContext, cn);
                    await fileSystem.Storage.UpdateActiveFileHeader(internalFile, serverFileHeader, odinContext, cn, raiseEvent: true);

                    await mediator.Publish(new NewFeedItemReceived()
                    {
                        Sender = odinContext.GetCallerOdinIdOrFail(),
                        OdinContext = newContext
                    });
                }
                else
                {
                    // perform update
                    request.FileMetadata.SenderOdinId = newContext.GetCallerOdinIdOrFail();
                    await fileSystem.Storage.ReplaceFileMetadataOnFeedDrive(fileId.Value, request.FileMetadata, newContext, cn);
                }
            }

            return new PeerTransferResponse()
            {
                Code = PeerResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<PeerTransferResponse> Delete(DeleteFeedFileMetadataRequest request, IOdinContext odinContext, DatabaseConnection cn)
        {
            await followerService.AssertTenantFollowsTheCaller(odinContext, cn);
            var newContext = OdinContextUpgrades.UpgradeToReadFollowersForDistribution(odinContext);
            {
                var fileId = await this.ResolveInternalFile(request.FileId, newContext, cn);
                if (null == fileId)
                {
                    //TODO: what's the right status code here
                    return new PeerTransferResponse()
                    {
                        Code = PeerResponseCode.AcceptedDirectWrite
                    };
                }

                await fileSystem.Storage.RemoveFeedDriveFile(fileId.Value, newContext, cn);

                return new PeerTransferResponse()
                {
                    Code = PeerResponseCode.AcceptedDirectWrite
                };
            }
        }

        /// <summary>
        /// Looks up a file by a global transit identifier
        /// </summary>
        private async Task<InternalDriveFileId?> ResolveInternalFile(GlobalTransitIdFileIdentifier file, IOdinContext odinContext, DatabaseConnection cn)
        {
            var (_, fileId) = await fileSystemResolver.ResolveFileSystem(file, odinContext, cn, tryCommentDrive: false);
            return fileId;
        }
    }
}