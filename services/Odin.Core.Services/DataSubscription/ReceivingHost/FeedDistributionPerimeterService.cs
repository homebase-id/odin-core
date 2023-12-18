using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.DataSubscription.SendingHost;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.ReceivingHost;

namespace Odin.Core.Services.DataSubscription.ReceivingHost
{
    public class FeedDistributionPerimeterService
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly IDriveFileSystem _fileSystem;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly FollowerService _followerService;

        public FeedDistributionPerimeterService(
            OdinContextAccessor contextAccessor,
            IDriveFileSystem fileSystem,
            FileSystemResolver fileSystemResolver,
            FollowerService followerService)
        {
            _contextAccessor = contextAccessor;
            _fileSystem = fileSystem;
            _fileSystemResolver = fileSystemResolver;
            _followerService = followerService;
        }
        
        public async Task<HostTransitResponse> AcceptUpdatedReactionPreview(UpdateReactionSummaryRequest request)
        {
            await _followerService.AssertTenantFollowsTheCaller();

            //S0510
            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                return new HostTransitResponse()
                {
                    Code = TransitResponseCode.Rejected
                };
            }

            using (new FeedDriveSecurityContext(_contextAccessor))
            {
                var fileId = await this.ResolveInternalFile(request.FileId);

                if (null == fileId)
                {
                    return new HostTransitResponse()
                    {
                        Code = TransitResponseCode.Rejected
                    };
                }

                try
                {
                    await _fileSystem.Storage.UpdateReactionPreviewOnFeedDrive(fileId.Value, request.ReactionPreview);
                }
                catch (OdinSecurityException)
                {
                    return new HostTransitResponse()
                    {
                        Code = TransitResponseCode.Rejected
                    };
                }
            }

            return new HostTransitResponse()
            {
                Code = TransitResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<HostTransitResponse> AcceptUpdatedFileMetadata(UpdateFeedFileMetadataRequest request)
        {
            await _followerService.AssertTenantFollowsTheCaller();
            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                return new HostTransitResponse()
                {
                    Code = TransitResponseCode.Rejected
                };
            }

            using (new FeedDriveSecurityContext(_contextAccessor))
            {
                var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);

                var fileId = await this.ResolveInternalFile(request.FileId);

                if (null == fileId)
                {
                    var internalFile = _fileSystem.Storage.CreateInternalFileId(driveId);

                    var keyHeader = KeyHeader.Empty();
                    var serverMetadata = new ServerMetadata()
                    {
                        AccessControlList = AccessControlList.OwnerOnly,
                        AllowDistribution = false,
                    };

                    request.FileMetadata.SenderOdinId = _contextAccessor.GetCurrent().GetCallerOdinIdOrFail();
                    var serverFileHeader = await _fileSystem.Storage.CreateServerFileHeader(internalFile, keyHeader, request.FileMetadata, serverMetadata);
                    await _fileSystem.Storage.UpdateActiveFileHeader(internalFile, serverFileHeader, raiseEvent: true);
                }
                else
                {
                    try
                    {
                        await _fileSystem.Storage.ReplaceFileMetadataOnFeedDrive(fileId.Value, request.FileMetadata);
                    }
                    catch (OdinSecurityException)
                    {
                        return new HostTransitResponse()
                        {
                            Code = TransitResponseCode.Rejected
                        };
                    }
                }
            }

            return new HostTransitResponse()
            {
                Code = TransitResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<HostTransitResponse> Delete(DeleteFeedFileMetadataRequest request)
        {
            await _followerService.AssertTenantFollowsTheCaller();
            using (new FeedDriveSecurityContext(_contextAccessor))
            {
                // var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);
                var fileId = await this.ResolveInternalFile(request.FileId);
                if (null == fileId)
                {
                    //TODO: what's the right status code here
                    return new HostTransitResponse()
                    {
                        Code = TransitResponseCode.AcceptedDirectWrite
                    };
                }

                await _fileSystem.Storage.RemoveFeedDriveFile(fileId.Value);

                return new HostTransitResponse()
                {
                    Code = TransitResponseCode.AcceptedDirectWrite
                };
            }
        }

        /// <summary>
        /// Looks up a file by a global transit identifier
        /// </summary>
        private async Task<InternalDriveFileId?> ResolveInternalFile(GlobalTransitIdFileIdentifier file)
        {
            var (_, fileId) = await _fileSystemResolver.ResolveFileSystem(file, tryCommentDrive: false);
            return fileId;
        }
    }
}