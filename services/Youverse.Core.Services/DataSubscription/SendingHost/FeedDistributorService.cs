using System.Linq;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
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
        private readonly IDriveAclAuthorizationService _aclAuthorizationService;

        public FeedDistributorService(FileSystemResolver fileSystemResolver, IDotYouHttpClientFactory dotYouHttpClientFactory,
            IDriveAclAuthorizationService aclAuthorizationService)
        {
            _fileSystemResolver = fileSystemResolver;
            _dotYouHttpClientFactory = dotYouHttpClientFactory;
            _aclAuthorizationService = aclAuthorizationService;
        }

        public async Task<(bool success, bool shouldRetry)> SendFile(InternalDriveFileId file, FileSystemType fileSystemType, OdinId recipient)
        {
            //TODO: validate the user has rights to follow the channel
            var caller = new CallerContext(
                odinId: recipient,
                masterKey: null,
                securityLevel: SecurityGroupType.Anonymous,
                circleIds: null,
                tokenType: default);

            var fs = _fileSystemResolver.ResolveFileSystem(file);
            var header = await fs.Storage.GetServerFileHeader(file);
            
            if (!await _aclAuthorizationService.CallerHasPermission(caller, header.ServerMetadata.AccessControlList))
            {
                return (false, false);
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

            return (IsSuccess(httpResponse), true);
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