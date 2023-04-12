using System.Threading.Tasks;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.ReceivingHost;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.DataSubscription.SendingHost
{
    public class FeedDistributorService
    {
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;

        public FeedDistributorService(FileSystemResolver fileSystemResolver, IDotYouHttpClientFactory dotYouHttpClientFactory)
        {
            _fileSystemResolver = fileSystemResolver;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
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
            var client = _dotYouHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
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