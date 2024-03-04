using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer;
using Odin.Core.Storage;
using Odin.Core.Util;
using Refit;

namespace Odin.Core.Services.DataSubscription.SendingHost
{
    public class FeedDistributorService(
        FileSystemResolver fileSystemResolver,
        IOdinHttpClientFactory odinHttpClientFactory,
        IDriveAclAuthorizationService driveAcl,
        OdinConfiguration odinConfiguration)
    {
        public async Task<bool> DeleteFile(InternalDriveFileId file, FileSystemType fileSystemType, OdinId recipient)
        {
            var fs = fileSystemResolver.ResolveFileSystem(file);
            var header = await fs.Storage.GetServerFileHeader(file);

            if (null == header)
            {
                //TODO: need log more info here
                return false;
            }

            var authorized = await driveAcl.IdentityHasPermission(recipient,
                header.ServerMetadata.AccessControlList);

            if (!authorized)
            {
                //TODO: need more info here
                return false;
            }

            var request = new DeleteFeedFileMetadataRequest()
            {
                FileId = new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    TargetDrive = SystemDriveConstants.FeedDrive
                }
            };

            var client = odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
            ApiResponse<PeerTransferResponse> httpResponse = null;
            
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                TimeSpan.FromMilliseconds(odinConfiguration.Host.PeerOperationDelayMs),
                async () => { httpResponse = await client.DeleteFeedMetadata(request); });

            return IsSuccess(httpResponse);
        }

        public async Task<bool> SendFile(InternalDriveFileId file, FileSystemType fileSystemType, OdinId recipient)
        {
            var fs = fileSystemResolver.ResolveFileSystem(file);
            var header = await fs.Storage.GetServerFileHeader(file);

            if (null == header)
            {
                //TODO: need log more info here
                return false;
            }

            var authorized = await driveAcl.IdentityHasPermission(recipient,
                header.ServerMetadata.AccessControlList);

            if (!authorized)
            {
                //TODO: need more info here
                return false;
            }

            var request = new UpdateFeedFileMetadataRequest()
            {
                FileId = new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    TargetDrive = SystemDriveConstants.FeedDrive
                },
                FileMetadata = header.FileMetadata
            };
            
            var client = odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);

            ApiResponse<PeerTransferResponse> httpResponse = null;
            
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.PeerOperationMaxAttempts,
                TimeSpan.FromMilliseconds(odinConfiguration.Host.PeerOperationDelayMs),
                async () => { httpResponse = await client.SendFeedFileMetadata(request); });

            return IsSuccess(httpResponse);
        }

        bool IsSuccess(ApiResponse<PeerTransferResponse> httpResponse)
        {
            if (httpResponse?.IsSuccessStatusCode ?? false)
            {
                var transitResponse = httpResponse.Content;
                return transitResponse!.Code == PeerResponseCode.AcceptedDirectWrite || transitResponse!.Code == PeerResponseCode.AcceptedIntoInbox;
            }

            return false;
        }
    }
}