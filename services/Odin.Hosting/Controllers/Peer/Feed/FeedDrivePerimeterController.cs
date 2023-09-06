using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.DataSubscription.ReceivingHost;
using Odin.Core.Services.DataSubscription.SendingHost;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.ReceivingHost;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.Peer.Feed
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.FeedV1)]
    [Route("")]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.FeedAuthScheme)]
    public class FeedDrivePerimeterController : OdinControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly FollowerService _followerService;

        /// <summary />
        public FeedDrivePerimeterController(OdinContextAccessor contextAccessor,
            FileSystemResolver fileSystemResolver, FollowerService followerService)
        {
            _contextAccessor = contextAccessor;
            _fileSystemResolver = fileSystemResolver;
            _followerService = followerService;
        }

        [HttpPost("filemetadata")]
        public async Task<HostTransitResponse> AcceptUpdatedFileMetadata(UpdateFeedFileMetadataRequest payload)
        {
            var perimeterService = GetPerimeterService();
            return await perimeterService.AcceptUpdatedFileMetadata(payload);
        }


        private FeedDistributionPerimeterService GetPerimeterService()
        {
            var fileSystem = base.GetFileSystemResolver().ResolveFileSystem();
            return new FeedDistributionPerimeterService(
                _contextAccessor,
                fileSystem,
                _fileSystemResolver,
                _followerService);
        }
    }
}