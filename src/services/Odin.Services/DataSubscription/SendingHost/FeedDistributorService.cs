using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.DataSubscription.SendingHost
{
    public class FeedDistributorService(
        FileSystemResolver fileSystemResolver,
        IOdinHttpClientFactory odinHttpClientFactory,
        IDriveAclAuthorizationService driveAcl,
        OdinConfiguration odinConfiguration)
    {
        public async Task<bool> DeleteFile(InternalDriveFileId file, FileSystemType fileSystemType, OdinId recipient, IOdinContext odinContext)
        {
            var fs = await fileSystemResolver.ResolveFileSystem(file, odinContext);
            var header = await fs.Storage.GetServerFileHeader(file, odinContext);

            if (null == header)
            {
                //TODO: need log more info here
                return false;
            }

            var authorized = await driveAcl.IdentityHasPermission(recipient,
                header.ServerMetadata.AccessControlList, odinContext);

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

            try
            {
                await TryRetry.WithDelayAsync(
                    odinConfiguration.Host.PeerOperationMaxAttempts,
                    odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () => { httpResponse = await client.DeleteFeedMetadata(request); });
            }
            catch (TryRetryException e)
            {
                HandleTryRetryException(e);
                throw;
            }

            return IsSuccess(httpResponse);
        }

        public async Task<bool> SendFile(InternalDriveFileId file, FeedDistributionItem distroItem, OdinId recipient, IOdinContext odinContext)
        {
            var fs = await fileSystemResolver.ResolveFileSystem(file, odinContext);
            var header = await fs.Storage.GetServerFileHeader(file, odinContext);

            if (null == header)
            {
                //TODO: need log more info here
                // need to ensure this is removed from the feed box
                return false;
            }

            var authorized = await driveAcl.IdentityHasPermission(recipient,
                header.ServerMetadata.AccessControlList, odinContext);

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
                FileMetadata = header.FileMetadata,
                SenderEccPublicKey = distroItem.EccPublicKey,
                EccSalt = distroItem.EccSalt.GetKey(),
                EncryptedPayload = distroItem.EncryptedPayload
            };

            var client = odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: distroItem.FileSystemType);

            ApiResponse<PeerTransferResponse> httpResponse = null;

            try
            {
                await TryRetry.WithDelayAsync(
                    odinConfiguration.Host.PeerOperationMaxAttempts,
                    odinConfiguration.Host.PeerOperationDelayMs,
                    CancellationToken.None,
                    async () => { httpResponse = await client.SendFeedFileMetadata(request); });
            }
            catch (TryRetryException e)
            {
                HandleTryRetryException(e);
                throw;
            }

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

        private void HandleTryRetryException(TryRetryException ex)
        {
            var e = ex.InnerException;
            if (e is TaskCanceledException || e is HttpRequestException || e is OperationCanceledException)
            {
                throw new OdinClientException("Failed while calling remote identity", e);
            }
        }
    }
}