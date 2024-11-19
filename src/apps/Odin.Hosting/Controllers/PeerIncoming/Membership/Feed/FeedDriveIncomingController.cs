using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.DataSubscription.ReceivingHost;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Peer;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Drives.Management;
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
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly DriveManager _driveManager;
        private readonly ILoggerFactory _loggerFactory;


        /// <summary />
        public FeedDriveIncomingController(
            FileSystemResolver fileSystemResolver, FollowerService followerService, IMediator mediator, TransitInboxBoxStorage transitInboxStorage,
            TenantSystemStorage tenantSystemStorage, DriveManager driveManager, ILoggerFactory loggerFactory)
        {
            _fileSystemResolver = fileSystemResolver;
            _followerService = followerService;
            _mediator = mediator;
            _transitInboxStorage = transitInboxStorage;
            _tenantSystemStorage = tenantSystemStorage;
            _driveManager = driveManager;
            _loggerFactory = loggerFactory;
        }

        [HttpPost("send-feed-filemetadata")]
        public async Task<PeerTransferResponse> AcceptUpdatedFileMetadata(UpdateFeedFileMetadataRequest payload)
        {
            var perimeterService = GetPerimeterService();
            
            return await perimeterService.AcceptUpdatedFileMetadataAsync(payload, WebOdinContext);
        }

        [HttpPost("delete")]
        public async Task<PeerTransferResponse> DeleteFileMetadata(DeleteFeedFileMetadataRequest payload)
        {
            var perimeterService = GetPerimeterService();
            
            return await perimeterService.DeleteAsync(payload, WebOdinContext);
        }

        private FeedDistributionPerimeterService GetPerimeterService()
        {
            var fileSystem = GetHttpFileSystemResolver().ResolveFileSystem();
            var logger = _loggerFactory.CreateLogger<FeedDistributionPerimeterService>();
            return new FeedDistributionPerimeterService(
                fileSystem,
                _fileSystemResolver,
                _followerService,
                _mediator,
                _transitInboxStorage,
                _driveManager, 
                logger);
        }
    }
}