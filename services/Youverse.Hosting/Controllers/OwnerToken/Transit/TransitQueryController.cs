﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.ClientToken.Drive;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.Transit
{
    [ApiController]
    [Route(OwnerApiPathConstants.TransitQueryV1)]
    [AuthorizeValidOwnerToken]
    public class TransitQueryController : ControllerBase
    {
        private readonly TransitQueryService _transitQueryService;

        public TransitQueryController(TransitQueryService transitQueryService)
        {
            _transitQueryService = transitQueryService;
        }

        // [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        // [HttpPost("modified")]
        // public async Task<QueryModifiedResult> GetModified([FromBody] QueryModifiedRequest request)
        // {
        //     var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
        //     var batch = await _driveQueryService.GetModified(driveId, request.QueryParams, request.ResultOptions);
        //     return batch;
        // }

        /// <summary>
        /// Executes a QueryBatch operation on a remote identity server
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] TransitQueryBatchRequest request)
        {
            var batch = await _transitQueryService.GetBatch((OdinId)request.OdinId, request);
            return QueryBatchResponse.FromResult(batch);
        }

        /// <summary>
        /// Retrieves a file's header and metadata from a remote identity server
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] TransitExternalFileIdentifier request)
        {
            SharedSecretEncryptedFileHeader result = await _transitQueryService.GetFileHeader((OdinId)request.OdinId, request.File);

            if (null == result)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        /// <summary>
        /// Retrieves a file's encrypted payload from a remote identity server
        ///
        /// The content type of the decrypted content is found in the header 'DecryptedContentType'
        ///
        /// The owner shared secret encrypted key header for the thumbnail is found in the header 'SharedSecretEncryptedHeader64'
        ///
        /// The flag indicating if the payload is encrypted is found in the header 'PayloadEncrypted'
        /// This is a byte array where the first 16 bytes are the IV, second 16 bytes are the EncryptedAESKey, and last 4 bytes is the encryption version
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("payload")]
        public async Task<IActionResult> GetPayloadStream([FromBody] TransitExternalFileIdentifier request)
        {
            var (encryptedKeyHeader, payloadIsEncrypted, decryptedContentType, payload) = await _transitQueryService.GetPayloadStream((OdinId)request.OdinId, request.File);

            if (payload == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, payloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());
            return new FileStreamResult(payload, "application/octet-stream");
        }

        /// <summary>
        /// Retrieves an encrypted thumbnail from a remote identity server.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// The content type of the decrypted content is found in the header 'DecryptedContentType'
        ///
        /// The owner shared secret encrypted key header for the thumbnail is found in the header 'sharedSecretEncryptedHeader64'
        ///
        /// The flag indicating if the payload is encrypted is found in the header 'PayloadEncrypted'
        /// This is a byte array where the first 16 bytes are the IV, second 48 bytes are the EncryptedAESKey, and last 4 bytes is the encryption version
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] TransitGetThumbRequest request)
        {
            var (encryptedKeyHeader, payloadIsEncrypted, decryptedContentType, thumb) =
                await _transitQueryService.GetThumbnail((OdinId)request.OdinId, request.File, request.Width, request.Height);

            if (thumb == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, payloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());
            return new FileStreamResult(thumb, "application/octet-stream");
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("metadata/type")]
        public async Task<PagedResult<ClientDriveData>> GetDrivesByType([FromBody] TransitGetDrivesByTypeRequest request)
        {
            var drives = await _transitQueryService.GetDrivesByType((OdinId)request.OdinId, request.DriveType);
            var clientDriveData = drives.Select(drive =>
                new ClientDriveData()
                {
                    TargetDrive = drive.TargetDrive,
                }).ToList();

            var page = new PagedResult<ClientDriveData>(PageOptions.All, 1, clientDriveData);
            return page;
        }
    }
}
