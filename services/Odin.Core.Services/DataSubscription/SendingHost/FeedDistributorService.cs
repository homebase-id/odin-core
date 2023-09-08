using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.ReceivingHost;
using Odin.Core.Storage;
using Refit;

namespace Odin.Core.Services.DataSubscription.SendingHost
{
    public class FeedDistributorService
    {
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IOdinHttpClientFactory _odinHttpClientFactory;

        public FeedDistributorService(FileSystemResolver fileSystemResolver, IOdinHttpClientFactory odinHttpClientFactory)
        {
            _fileSystemResolver = fileSystemResolver;
            _odinHttpClientFactory = odinHttpClientFactory;
        }
        
        public async Task<bool> SendFile(InternalDriveFileId file, FileSystemType fileSystemType, OdinId recipient)
        {
            //TODO: check if recipient can actually get the file

            var fs = _fileSystemResolver.ResolveFileSystem(file);
            var header = await fs.Storage.GetServerFileHeader(file);

            var request = new UpdateFeedFileMetadataRequest()
            {
                FileId = new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    TargetDrive = SystemDriveConstants.FeedDrive
                },
                FileMetadata = header.FileMetadata
            };

            //TODO: need to validate the recipient can get the file - security
            var client = _odinHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
            var httpResponse = await client.SendFeedFileMetadata(request);

            return IsSuccess(httpResponse);
        }

        bool IsSuccess(ApiResponse<HostTransitResponse> httpResponse)
        {
            if (httpResponse.IsSuccessStatusCode)
            {
                var transitResponse = httpResponse.Content;
                return transitResponse!.Code == TransitResponseCode.AcceptedDirectWrite || transitResponse!.Code == TransitResponseCode.AcceptedIntoInbox;
            }

            return false;
        }
    }
}