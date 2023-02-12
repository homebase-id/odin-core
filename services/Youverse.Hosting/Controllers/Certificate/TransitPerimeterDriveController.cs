using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Hosting.Authentication.Perimeter;

namespace Youverse.Hosting.Controllers.Certificate
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/host")]
    [Authorize(Policy = CertificatePerimeterPolicies.IsInYouverseNetwork, AuthenticationSchemes = PerimeterAuthConstants.TransitCertificateAuthScheme)]
    public class TransitPerimeterDriveController : ControllerBase
    {
        private readonly ITransitPerimeterService _perimeterService;
        private readonly IDriveStorageService _driveStorageService;

        public TransitPerimeterDriveController(ITransitPerimeterService perimeterService, IDriveStorageService driveStorageService)
        {
            _perimeterService = perimeterService;
            _driveStorageService = driveStorageService;
        }

        [HttpPost("querybatch")]
        public async Task<QueryBatchResponse> QueryBatch(QueryBatchRequest request)
        {
            var batch = await _perimeterService.QueryBatch(request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions());
            return QueryBatchResponse.FromResult(batch);
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            ClientFileHeader result = await _perimeterService.GetFileHeader(request.TargetDrive, request.FileId);

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
        public async Task<IActionResult> GetPayloadStream([FromBody] ExternalFileIdentifier request)
        {
            var (encryptedKeyHeader64, payloadIsEncrypted, decryptedContentType, payload) = await _perimeterService.GetPayloadStream(request.TargetDrive, request.FileId);

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
            var (encryptedKeyHeader64, payloadIsEncrypted, decryptedContentType, thumb) =
                await _perimeterService.GetThumbnail(request.File.TargetDrive, request.File.FileId, request.Height, request.Width);

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
        public async Task<IEnumerable<PerimeterDriveData>> GetDrives([FromBody]GetDrivesByTypeRequest request)
        {
            var drives = await _perimeterService.GetDrives(request.DriveType);
            return drives;
        }


        [HttpPost("deletelinkedfile")]
        public async Task<HostTransitResponse> DeleteLinkedFile(DeleteLinkedFileTransitRequest transitRequest)
        {
            return await _perimeterService.DeleteLinkedFile(transitRequest.TargetDrive, transitRequest.GlobalTransitId);
        }
    }
}