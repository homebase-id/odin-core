using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.DataSubscription.ReceivingHost;
using Odin.Core.Services.DataSubscription.SendingHost;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Transit.ReceivingHost;
using Odin.Hosting.Authentication.Perimeter;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.Certificate.Feed
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/host/feed")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.FeedAuthScheme)]
    public class FeedDrivePerimeterController : OdinControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly IPublicKeyService _publicKeyService;
        private readonly DriveManager _driveManager;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly IMediator _mediator;
        private readonly FileSystemResolver _fileSystemResolver;
        private readonly FollowerService _followerService;

        /// <summary />
        public FeedDrivePerimeterController(OdinContextAccessor contextAccessor, IPublicKeyService publicKeyService, DriveManager driveManager,
            TenantSystemStorage tenantSystemStorage, IMediator mediator, FileSystemResolver fileSystemResolver, FollowerService followerService)
        {
            _contextAccessor = contextAccessor;
            this._publicKeyService = publicKeyService;
            this._driveManager = driveManager;
            this._tenantSystemStorage = tenantSystemStorage;
            this._mediator = mediator;
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