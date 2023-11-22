using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.ReceivingHost;
using Odin.Core.Services.Peer.ReceivingHost.Quarantine;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.Peer
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.DriveV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerPerimeterDriveController : OdinControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly DriveManager _driveManager;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly IMediator _mediator;
        private readonly FileSystemResolver _fileSystemResolver;

        /// <summary />
        public PeerPerimeterDriveController(OdinContextAccessor contextAccessor, DriveManager driveManager,
            TenantSystemStorage tenantSystemStorage, IMediator mediator, FileSystemResolver fileSystemResolver)
        {
            _contextAccessor = contextAccessor;
            this._driveManager = driveManager;
            this._tenantSystemStorage = tenantSystemStorage;
            this._mediator = mediator;
            _fileSystemResolver = fileSystemResolver;
        }

        [HttpPost("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection(QueryBatchCollectionRequest request)
        {
            var perimeterService = GetPerimeterService();
            return await perimeterService.QueryBatchCollection(request);
        }

        [HttpPost("querymodified")]
        public async Task<QueryModifiedResponse> QueryModified(QueryModifiedRequest request)
        {
            var perimeterService = GetPerimeterService();
            var result = await perimeterService.QueryModified(request.QueryParams, request.ResultOptions);
            return QueryModifiedResponse.FromResult(result);
        }

        [HttpPost("querybatch")]
        public async Task<QueryBatchResponse> QueryBatch(QueryBatchRequest request)
        {
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
            var (encryptedKeyHeader64, isEncrypted, payloadStream) =
                await perimeterService.GetPayloadStream(request.File.TargetDrive, request.File.FileId, request.Key, request.Chunk);

            if (payloadStream == null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(payloadStream.LastModified);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, payloadStream.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.IcrEncryptedSharedSecret64Header, encryptedKeyHeader64);
            return new FileStreamResult(payloadStream.Stream, "application/octet-stream");
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

            var (encryptedKeyHeader64, isEncrypted, decryptedContentType, lastModified, thumb) =
                await perimeterService.GetThumbnail(request.File.TargetDrive, request.File.FileId, request.Height, request.Width, request.PayloadKey);

            if (thumb == null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.IcrEncryptedSharedSecret64Header, encryptedKeyHeader64);
            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(lastModified);
            return new FileStreamResult(thumb, "application/octet-stream");
        }

        [HttpPost("metadata/type")]
        public async Task<IEnumerable<PerimeterDriveData>> GetDrives([FromBody] GetDrivesByTypeRequest request)
        {
            var perimeterService = GetPerimeterService();
            var drives = await perimeterService.GetDrives(request.DriveType);
            return drives;
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

        ///
        [HttpPost("header_byglobaltransitid")]
        public async Task<IActionResult> GetFileHeaderByGlobalTransitId([FromBody] GlobalTransitIdFileIdentifier file)
        {
            var result = await LookupFileHeaderByGlobalTransitId(file);
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpPost("header_byuniqueid")]
        public async Task<IActionResult> GetFileHeaderByUniqueId([FromBody] GetFileHeaderByUniqueIdRequest request)
        {
            var result = await LookupHeaderByUniqueId(request.UniqueId, request.TargetDrive);
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpPost("payload_byglobaltransitid")]
        public async Task<IActionResult> GetPayloadStreamByGlobalTransitId([FromBody] GetPayloadByGlobalTransitIdRequest request)
        {
            var header = await this.LookupFileHeaderByGlobalTransitId(request.File);
            if (null == header)
            {
                return NotFound();
            }

            return await GetPayloadStream(
                new GetPayloadRequest()
                {
                    File = new ExternalFileIdentifier()
                    {
                        FileId = header.FileId,
                        TargetDrive = request.File.TargetDrive
                    },
                    Key = request.Key,
                    Chunk = request.Chunk
                });
        }

        [HttpPost("payload_byuniqueid")]
        public async Task<IActionResult> GetPayloadStreamByUniqueId([FromBody] GetPayloadByUniqueIdRequest request)
        {
            var header = await this.LookupHeaderByUniqueId(request.UniqueId, request.TargetDrive);
            if (null == header)
            {
                return NotFound();
            }

            return await GetPayloadStream(
                new GetPayloadRequest()
                {
                    File = new ExternalFileIdentifier()
                    {
                        FileId = header.FileId,
                        TargetDrive = request.TargetDrive
                    },
                    Key = request.Key,
                    Chunk = request.Chunk
                });
        }

        [HttpPost("thumb_byglobaltransitid")]
        public async Task<IActionResult> GetThumbnailStreamByGlobalTransitId([FromBody] GetThumbnailByGlobalTransitIdRequest request)
        {
            var header = await this.LookupFileHeaderByGlobalTransitId(request.File);
            if (null == header)
            {
                return NotFound();
            }

            return await GetThumbnail(new GetThumbnailRequest()
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = header.FileId,
                    TargetDrive = request.File.TargetDrive
                },
                Width = request.Width,
                Height = request.Height,
                PayloadKey = request.PayloadKey
            });
        }

        [HttpPost("thumb_byuniqueid")]
        public async Task<IActionResult> GetThumbnailStreamByUniqueId(GetThumbnailByUniqueIdRequest request)
        {
            var header = await this.LookupHeaderByUniqueId(request.ClientUniqueId, request.TargetDrive);
            if (null == header)
            {
                return NotFound();
            }

            return await GetThumbnail(new GetThumbnailRequest()
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = header.FileId,
                    TargetDrive = request.TargetDrive
                },
                Width = request.Width,
                Height = request.Height,
                PayloadKey = request.PayloadKey
            });
        }

        private async Task<SharedSecretEncryptedFileHeader> LookupFileHeaderByGlobalTransitId(GlobalTransitIdFileIdentifier file)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(new TargetDrive()
            {
                Alias = file.TargetDrive.Alias,
                Type = file.TargetDrive.Type
            });

            var queryService = GetFileSystemResolver().ResolveFileSystem().Query;
            var result = await queryService.GetFileByGlobalTransitId(driveId, file.GlobalTransitId, excludePreviewThumbnail: false);
            return result;
        }

        private async Task<SharedSecretEncryptedFileHeader> LookupHeaderByUniqueId(Guid clientUniqueId, TargetDrive targetDrive)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(targetDrive);
            var queryService = GetFileSystemResolver().ResolveFileSystem().Query;
            var result = await queryService.GetFileByClientUniqueId(driveId, clientUniqueId, excludePreviewThumbnail: false);
            return result;
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