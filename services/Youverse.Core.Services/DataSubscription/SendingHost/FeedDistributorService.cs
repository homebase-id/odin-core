using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Transit;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.DataSubscription.SendingHost
{
    public class FeedDistributorService 
    {
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDotYouHttpClientFactory _dotYouHttpClientFactory;
        private readonly ICircleNetworkService _circleNetworkService;

        public FeedDistributorService(FileSystemResolver fileSystemResolver, DotYouContextAccessor contextAccessor, IDotYouHttpClientFactory dotYouHttpClientFactory, ICircleNetworkService circleNetworkService)
        {
            _fileSystemResolver = fileSystemResolver;
            _contextAccessor = contextAccessor;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _circleNetworkService = circleNetworkService;
        }


        public async Task<Dictionary<string, TransitResponseCode>> SendReactionPreview(
            InternalDriveFileId file,
            FileSystemType fileSystemType,
            IEnumerable<OdinId> recipients)
        {
            Dictionary<string, TransitResponseCode> result = new Dictionary<string, TransitResponseCode>();

            var fs = _fileSystemResolver.ResolveFileSystem(file);
            var header = await fs.Storage.GetServerFileHeader(file);

            //TODO: Handle security??

            if (null == header.FileMetadata.ReactionPreview)
            {
                //TODO?
                return null;
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

            foreach (var recipient in recipients)
            {
                //TODO: need to validate the recipient can get the file - security
                
                var client = _dotYouHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
                var httpResponse = await client.SendReactionPreview(request);

                if (httpResponse.IsSuccessStatusCode)
                {
                    var transitResponse = httpResponse.Content;
                    result.Add(recipient, transitResponse!.Code);
                }
                else
                {
                    result.Add(recipient, TransitResponseCode.Rejected);
                }
            }

            return result;
        }

        public async Task<Dictionary<string, TransitResponseCode>> SendFiles(InternalDriveFileId file, FileSystemType fileSystemType, List<OdinId> recipients)
        {
            Dictionary<string, TransitResponseCode> result = new Dictionary<string, TransitResponseCode>();

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

            foreach (var recipient in recipients)
            {
                //TODO: need to validate the recipient can get the file - security
                
                var client = _dotYouHttpClientFactory.CreateClient<IFeedDistributorHttpClient>(recipient, fileSystemType: fileSystemType);
                var httpResponse = await client.SendFeedFileMetadata(request);

                if (httpResponse.IsSuccessStatusCode)
                {
                    var transitResponse = httpResponse.Content;
                    result.Add(recipient, transitResponse!.Code);
                }
                else
                {
                    result.Add(recipient, TransitResponseCode.Rejected);
                }
            }

            return result;
            
        }
    }
}