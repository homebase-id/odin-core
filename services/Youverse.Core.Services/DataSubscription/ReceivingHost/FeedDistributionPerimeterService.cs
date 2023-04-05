using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.DataSubscription.SendingHost;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.ReceivingHost;

namespace Youverse.Core.Services.DataSubscription.ReceivingHost
{
    public class FeedDistributionPerimeterService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveFileSystem _fileSystem;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly FollowerService _followerService;

        public FeedDistributionPerimeterService(
            DotYouContextAccessor contextAccessor,
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

            InternalDriveFileId? fileId;
            using (new FeedDriveSecurityContext(_contextAccessor))
            {
                fileId = await this.ResolveInternalFile(request.FileId);

                if (null == fileId)
                {
                    return new HostTransitResponse()
                    {
                        Code = TransitResponseCode.Rejected
                    };
                }

                var header = await _fileSystem.Storage.GetServerFileHeader(fileId.Value);

                //S0510
                if (header.FileMetadata.SenderOdinId != _contextAccessor.GetCurrent().Caller.OdinId)
                {
                    return new HostTransitResponse()
                    {
                        Code = TransitResponseCode.Rejected
                    };
                }
            }

            await _fileSystem.Storage.UpdateReactionPreview(fileId.Value, request.ReactionPreview);

            return new HostTransitResponse()
            {
                Code = TransitResponseCode.AcceptedDirectWrite
            };
        }

        public async Task<HostTransitResponse> AcceptUpdatedFileHeader(UpdateFeedFileMetadataRequest request)
        {
            await _followerService.AssertTenantFollowsTheCaller();
            if (request.FileId.TargetDrive != SystemDriveConstants.FeedDrive)
            {
                return new HostTransitResponse()
                {
                    Code = TransitResponseCode.Rejected
                };
            }

            InternalDriveFileId? fileId;
            using (new FeedDriveSecurityContext(_contextAccessor))
            {
                fileId = await this.ResolveInternalFile(request.FileId);

                var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(SystemDriveConstants.FeedDrive);
                if (null == fileId)
                {
                    var internalFile = _fileSystem.Storage.CreateInternalFileId(driveId);

                    var keyHeader = KeyHeader.Empty();
                    var serverMetadata = new ServerMetadata()
                    {
                        AccessControlList = AccessControlList.OwnerOnly,
                        AllowDistribution = false,
                    };

                    var serverFileHeader = await _fileSystem.Storage.CreateServerFileHeader(internalFile, keyHeader, request.FileMetadata, serverMetadata);
                    await _fileSystem.Storage.UpdateActiveFileHeader(internalFile, serverFileHeader, raiseEvent: true);
                }
                else
                {
                    var header = await _fileSystem.Storage.GetServerFileHeader(fileId.Value);

                    //S0510
                    if (header.FileMetadata.SenderOdinId != _contextAccessor.GetCurrent().Caller.OdinId)
                    {
                        return new HostTransitResponse()
                        {
                            Code = TransitResponseCode.Rejected
                        };
                    }

                    header.FileMetadata = request.FileMetadata;

                    await _fileSystem.Storage.UpdateActiveFileHeader(fileId.Value, header, raiseEvent:true);
                }
            }

            return new HostTransitResponse()
            {
                Code = TransitResponseCode.AcceptedDirectWrite
            };
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

    public class FeedDriveSecurityContext : IDisposable
    {
        private readonly SecurityGroupType _prevSecurityGroupType;
        private readonly DotYouContextAccessor _dotYouContextAccessor;

        public FeedDriveSecurityContext(DotYouContextAccessor dotYouContextAccessor)
        {
            _dotYouContextAccessor = dotYouContextAccessor;
            _prevSecurityGroupType = _dotYouContextAccessor.GetCurrent().Caller.SecurityLevel;

            _dotYouContextAccessor.GetCurrent().Caller.SecurityLevel = SecurityGroupType.Owner;
            // _dotYouContextAccessor.GetCurrent().SetPermissionContext();
        }

        public void Dispose()
        {
            _dotYouContextAccessor.GetCurrent().Caller.SecurityLevel = _prevSecurityGroupType;
        }
    }
}