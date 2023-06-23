using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Transit;
using Odin.Core.Services.Transit.ReceivingHost;
using Odin.Core.Services.Transit.ReceivingHost.Quarantine;
using Odin.Hosting.Authentication.Perimeter;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.Certificate
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/host")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PerimeterAuthConstants.TransitCertificateAuthScheme)]
    public class TransitPerimeterDriveController : OdinControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly PublicPrivateKeyService _publicKeyService;
        private readonly DriveManager _driveManager;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly IMediator _mediator;
        private readonly FileSystemResolver _fileSystemResolver;

        /// <summary />
        public TransitPerimeterDriveController(OdinContextAccessor contextAccessor, PublicPrivateKeyService publicKeyService, DriveManager driveManager,
            TenantSystemStorage tenantSystemStorage, IMediator mediator, FileSystemResolver fileSystemResolver)
        {
            _contextAccessor = contextAccessor;
            this._publicKeyService = publicKeyService;
            this._driveManager = driveManager;
            this._tenantSystemStorage = tenantSystemStorage;
            this._mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
        }

        [HttpPost("querybatch")]
        public async Task<QueryBatchResponse> QueryBatch(QueryBatchRequest request)
        {
            var fileSystem = base.GetFileSystemResolver().ResolveFileSystem();
            var perimeterService = GetPerimeterService();
            var batch = await perimeterService.QueryBatch(request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions());
            return QueryBatchResponse.FromResult(batch);
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            var perimeterService = GetPerimeterService();
            SharedSecretEncryptedFileHeader result = await perimeterService.GetFileHeader(request.TargetDrive, request.FileId);

            //404 is possible
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        /// <summary>
        /// Retrieves a file's encrypted payload
        /// </summary>
        [HttpPost("payload")]
        public async Task<IActionResult> GetPayloadStream([FromBody] GetPayloadRequest request)
        {
            var perimeterService = GetPerimeterService();
            var (encryptedKeyHeader64, payloadIsEncrypted, decryptedContentType, payload) =
                await perimeterService.GetPayloadStream(request.File.TargetDrive, request.File.FileId, request.Chunk);

            if (payload == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, payloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.IcrEncryptedSharedSecret64Header, encryptedKeyHeader64);
            return new FileStreamResult(payload, "application/octet-stream");
        }

        /// <summary>
        /// Retrieves an encrypted thumbnail.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// See GET files/header
        /// </summary>
        /// <param name="request"></param>
        [HttpPost("thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            var perimeterService = GetPerimeterService();

            var (encryptedKeyHeader64, payloadIsEncrypted, decryptedContentType, thumb) =
                await perimeterService.GetThumbnail(request.File.TargetDrive, request.File.FileId, request.Height, request.Width);

            if (thumb == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, payloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.IcrEncryptedSharedSecret64Header, encryptedKeyHeader64);
            return new FileStreamResult(thumb, "application/octet-stream");
        }

        [HttpPost("metadata/type")]
        public async Task<IEnumerable<PerimeterDriveData>> GetDrives([FromBody] GetDrivesByTypeRequest request)
        {
            var perimeterService = GetPerimeterService();
            var drives = await perimeterService.GetDrives(request.DriveType);
            return drives;
        }
        
        [HttpGet("security/context")]
        public Task<RedactedOdinContext> GetRemoteSecurityContext()
        {
            return Task.FromResult(_contextAccessor.GetCurrent().Redacted());
        }

        [HttpPost("deletelinkedfile")]
        public async Task<HostTransitResponse> DeleteLinkedFile(DeleteRemoteFileTransitRequest transitRequest)
        {
            var perimeterService = GetPerimeterService();
            return await perimeterService.AcceptDeleteLinkedFileRequest(
                transitRequest.RemoteGlobalTransitIdFileIdentifier.TargetDrive, 
                transitRequest.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                transitRequest.FileSystemType);
        }

        private TransitPerimeterService GetPerimeterService()
        {
            var fileSystem = base.GetFileSystemResolver().ResolveFileSystem();
            return new TransitPerimeterService(_contextAccessor,
                _driveManager,
                fileSystem,
                _tenantSystemStorage,
                _mediator,
                _fileSystemResolver);
        }
    }
}