using System.Collections.Generic;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.ReceivingHost;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.DataSubscription.SendingHost
{
    public class FeedDistributorService
    {
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly ICircleNetworkService _circleNetworkService;

        public FeedDistributorService(FileSystemResolver fileSystemResolver, DotYouContextAccessor contextAccessor,
            IDotYouHttpClientFactory dotYouHttpClientFactory, ICircleNetworkService circleNetworkService)
        {
            _fileSystemResolver = fileSystemResolver;
            _contextAccessor = contextAccessor;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _circleNetworkService = circleNetworkService;
        }


        public async Task<bool> SendReactionPreview(
            InternalDriveFileId file,
            FileSystemType fileSystemType,
            OdinId recipient)
        {
            var fs = _fileSystemResolver.ResolveFileSystem(file);
            var header = await fs.Storage.GetServerFileHeader(file);

            //TODO: Handle security??

            if (null == header.FileMetadata.ReactionPreview)
            {
                //TODO?
                return false;
            }

            var request = new UpdateReactionSummaryRequest()
            {
                FileId = new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                    TargetDrive = SystemDriveConstants.FeedDrive
                },
                ReactionPreview = header.FileMetadata.ReactionPreview
            };

            //TODO: need to validate the recipient can get the file - security

            var client = _dotYouHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
            var httpResponse = await client.SendReactionPreview(request);
            return IsSuccess(httpResponse);
        }

        public async Task<bool> SendFile(InternalDriveFileId file, FileSystemType fileSystemType, OdinId recipient)
        {
            //TODO: check if recipient can actually get the file

            var fs = _fileSystemResolver.ResolveFileSystem(file);
            var header = await fs.Storage.GetServerFileHeader(file);

            if (header.FileMetadata.PayloadIsEncrypted)
            {
                throw new YouverseSecurityException("Cannot send encrypted files to unconnected followers");
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