using System.Threading.Tasks;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Services.AppNotifications.SystemNotifications;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.DataSubscription.SendingHost;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Incoming;

namespace Odin.Core.Services.DataSubscription.ReceivingHost
{
    public class FeedDistributionPerimeterService(
        OdinContextAccessor contextAccessor,
        IDriveFileSystem fileSystem,
        FileSystemResolver fileSystemResolver,
        FollowerService followerService,
        IMediator mediator)
    {
        public async Task<PeerResponse> AcceptUpdatedReactionPreview(UpdateReactionSummaryRequest request)
        {
            await followerService.AssertTenantFollowsTheCaller();

            //S0510
            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                return new PeerResponse()
                {
                    Code = PeerResponseCode.Rejected
                };
            }

            using (new FeedDriveDistributionSecurityContext(contextAccessor))
            {
                var fileId = await this.ResolveInternalFile(request.FileId);

                if (null == fileId)
                {
                    return new PeerResponse()
                    {
                        Code = PeerResponseCode.Rejected
                    };
                }

                try
                {
                    await fileSystem.Storage.UpdateReactionPreviewOnFeedDrive(fileId.Value, request.ReactionPreview);
                }
                catch (OdinSecurityException)
                {
                    return new PeerResponse()
                    {
                        Code = PeerResponseCode.Rejected
                    };
                }
            }

            return new PeerResponse()
            {
                Code = PeerResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<PeerResponse> AcceptUpdatedFileMetadata(UpdateFeedFileMetadataRequest request)
        {
            await followerService.AssertTenantFollowsTheCaller();
            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                return new PeerResponse()
                {
                    Code = PeerResponseCode.Rejected
                };
            }

            using (new FeedDriveDistributionSecurityContext(contextAccessor))
            {
                var driveId = contextAccessor.GetCurrent().PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);

                var fileId = await this.ResolveInternalFile(request.FileId);

                if (null == fileId)
                {
                    //new file
                    var internalFile = fileSystem.Storage.CreateInternalFileId(driveId);

                    var keyHeader = KeyHeader.Empty();
                    var serverMetadata = new ServerMetadata()
                    {
                        AccessControlList = AccessControlList.OwnerOnly,
                        AllowDistribution = false,
                    };

                    request.FileMetadata.SenderOdinId = contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
                    var serverFileHeader = await fileSystem.Storage.CreateServerFileHeader(internalFile, keyHeader, request.FileMetadata, serverMetadata);
                    await fileSystem.Storage.UpdateActiveFileHeader(internalFile, serverFileHeader, raiseEvent: true);

                    await mediator.Publish(new NewFeedItemReceived()
                    {
                        Sender = contextAccessor.GetCurrent().GetCallerOdinIdOrFail(),
                    });
                }
                else
                {

                    // update
                    try
                    {
                        request.FileMetadata.SenderOdinId = contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
                        await fileSystem.Storage.ReplaceFileMetadataOnFeedDrive(fileId.Value, request.FileMetadata);
                    }
                    catch (OdinSecurityException)
                    {
                        return new PeerResponse()
                        {
                            Code = PeerResponseCode.Rejected
                        };
                    }
                }
            }

            return new PeerResponse()
            {
                Code = PeerResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<PeerResponse> Delete(DeleteFeedFileMetadataRequest request)
        {
            await followerService.AssertTenantFollowsTheCaller();
            using (new FeedDriveDistributionSecurityContext(contextAccessor))
            {
                var fileId = await this.ResolveInternalFile(request.FileId);
                if (null == fileId)
                {
                    //TODO: what's the right status code here
                    return new PeerResponse()
                    {
                        Code = PeerResponseCode.AcceptedDirectWrite
                    };
                }

                await fileSystem.Storage.RemoveFeedDriveFile(fileId.Value);

                return new PeerResponse()
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
