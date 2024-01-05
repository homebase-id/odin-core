using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.DataSubscription.ReceivingHost;
using Odin.Core.Services.DataSubscription.SendingHost;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.ReceivingHost;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Refit;

namespace Odin.Hosting.Controllers.Peer.Membership.Feed
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.FeedV1)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.FeedAuthScheme)]
    public class FeedDrivePerimeterController : OdinControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly FollowerService _followerService;
        private readonly IMediator _mediator;

        /// <summary />
        public FeedDrivePerimeterController(OdinContextAccessor contextAccessor,
            FileSystemResolver fileSystemResolver, FollowerService followerService, IMediator mediator)
        {
            _contextAccessor = contextAccessor;
            _fileSystemResolver = fileSystemResolver;
            _followerService = followerService;
            _mediator = mediator;
        }

        [HttpPost("filemetadata")]
        public async Task<HostTransitResponse> AcceptUpdatedFileMetadata(UpdateFeedFileMetadataRequest payload)
        {
            var perimeterService = GetPerimeterService();
            return await perimeterService.AcceptUpdatedFileMetadata(payload);
        }
        
        [HttpPost("delete")]
        public async Task<HostTransitResponse> DeleteFileMetadata(DeleteFeedFileMetadataRequest payload)
        {
            var perimeterService = GetPerimeterService();
            return await perimeterService.Delete(payload);
        }

        private FeedDistributionPerimeterService GetPerimeterService()
        {
            var fileSystem = base.GetHttpFileSystemResolver().ResolveFileSystem();
            return new FeedDistributionPerimeterService(
                _contextAccessor,
                fileSystem,
                _fileSystemResolver,
                _followerService,
                _mediator);
        }
    }
}