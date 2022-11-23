using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Identity;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Transit;

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
            var batch = await _transitQueryService.GetBatch((DotYouIdentity)request.DotYouId, request);

            var response = new QueryBatchResponse()
            {
                IncludeMetadataHeader = batch.IncludeMetadataHeader,
                CursorState = batch.Cursor.ToState(),
                SearchResults = batch.SearchResults
            };

            return response;
        }

        /// <summary>
        /// Retrieves a file's header and metadata from a remote identity server
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] TransitExternalFileIdentifier request)
        {
            ClientFileHeader result = await _transitQueryService.GetFileHeader((DotYouIdentity)request.DotYouId, request.File);

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
            var (encryptedKeyHeader, payloadIsEncrypted, decryptedContentType, payload) = await _transitQueryService.GetPayloadStream((DotYouIdentity)request.DotYouId, request.File);

            if (payload == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(TransitConstants.PayloadEncrypted, payloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(TransitConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.Add(TransitConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());
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
        /// This is a byte array where the first 16 bytes are the IV, second 16 bytes are the EncryptedAESKey, and last 4 bytes is the encryption version
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] TransitGetThumbRequest request)
        {
            var (encryptedKeyHeader, payloadIsEncrypted, decryptedContentType, thumb) =
                await _transitQueryService.GetThumbnail((DotYouIdentity)request.DotYouId, request.File, request.Width, request.Height);

            if (thumb == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(TransitConstants.PayloadEncrypted, payloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(TransitConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.Add(TransitConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());
            return new FileStreamResult(thumb, "application/octet-stream");
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("metadata")]
        public async Task<IActionResult> GetMetdata()
        {
            throw new NotImplementedException();
        }
    }
}