using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Incoming;
using Odin.Core.Storage;
using Refit;

namespace Odin.Core.Services.DataSubscription.SendingHost
{
    public class FeedDistributorService
    {
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;
        private readonly IDriveAclAuthorizationService _driveAcl;

        public FeedDistributorService(FileSystemResolver fileSystemResolver, IOdinHttpClientFactory odinHttpClientFactory,
            IDriveAclAuthorizationService driveAcl)
        {
            _fileSystemResolver = fileSystemResolver;
            _odinHttpClientFactory = odinHttpClientFactory;
            _driveAcl = driveAcl;
        }

        public async Task<bool> DeleteFile(InternalDriveFileId file, FileSystemType fileSystemType, OdinId recipient)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(file);
            var header = await fs.Storage.GetServerFileHeader(file);

            if (null == header)
            {
                //TODO: need log more info here
                return false;
            }

            var authorized = await _driveAcl.IdentityHasPermission(recipient,
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
            
            var client = _odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
            var httpResponse = await client.DeleteFeedMetadata(request);

            return IsSuccess(httpResponse);
        }
        
        public async Task<bool> SendFile(InternalDriveFileId file, FileSystemType fileSystemType, OdinId recipient)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(file);
            var header = await fs.Storage.GetServerFileHeader(file);

            if (null == header)
            {
                //TODO: need log more info here
                return false;
            }

            var authorized = await _driveAcl.IdentityHasPermission(recipient,
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
            
            var client = _odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
            var httpResponse = await client.SendFeedFileMetadata(request);

            return IsSuccess(httpResponse);
        }

        bool IsSuccess(ApiResponse<PeerResponse> httpResponse)
        {
            if (httpResponse.IsSuccessStatusCode)
            {
                var transitResponse = httpResponse.Content;
                return transitResponse!.Code == PeerResponseCode.AcceptedDirectWrite || transitResponse!.Code == PeerResponseCode.AcceptedIntoInbox;
            }

            return false;
        }
    }
}