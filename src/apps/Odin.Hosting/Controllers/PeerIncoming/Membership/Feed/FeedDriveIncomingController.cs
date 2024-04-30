using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.DataSubscription.ReceivingHost;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;

namespace Odin.Hosting.Controllers.PeerIncoming.Membership.Feed
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.FeedV1)]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.FeedAuthScheme)]
    public class FeedDriveIncomingController : OdinControllerBase
    {
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly FollowerService _followerService;
        private readonly IMediator _mediator;
        private readonly TransitInboxBoxStorage _transitInboxStorage;

        /// <summary />
        public FeedDriveIncomingController(
            FileSystemResolver fileSystemResolver, FollowerService followerService, IMediator mediator, TransitInboxBoxStorage transitInboxStorage)
        {
            _fileSystemResolver = fileSystemResolver;
            _followerService = followerService;
            _mediator = mediator;
            _transitInboxStorage = transitInboxStorage;
        }

        [HttpPost("filemetadata")]
        public async Task<PeerTransferResponse> AcceptUpdatedFileMetadata(UpdateFeedFileMetadataRequest payload)
        {
            var perimeterService = GetPerimeterService();
            return await perimeterService.AcceptUpdatedFileMetadata(payload, WebOdinContext);
        }

        [HttpPost("delete")]
        public async Task<PeerTransferResponse> DeleteFileMetadata(DeleteFeedFileMetadataRequest payload)
        {
            var perimeterService = GetPerimeterService();
            return await perimeterService.Delete(payload, WebOdinContext);
        }

        private FeedDistributionPerimeterService GetPerimeterService()
        {
            var fileSystem = GetHttpFileSystemResolver().ResolveFileSystem();
            return new FeedDistributionPerimeterService(
                fileSystem,
                _fileSystemResolver,
                _followerService,
                _mediator,
                _transitInboxStorage);
        }
    }
}