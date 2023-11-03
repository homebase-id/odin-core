using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    public class TransitQueryControllerBase : OdinControllerBase
    {
        private readonly TransitQueryService _transitQueryService;

        public TransitQueryControllerBase(TransitQueryService transitQueryService)
        {
            _transitQueryService = transitQueryService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("batchcollection")]
        public async Task<QueryBatchCollectionResponse> GetBatchCollection([FromBody] TransitQueryBatchCollectionRequest request)
        {
            var result = await _transitQueryService.GetBatchCollection((OdinId)request.OdinId, request, GetFileSystemResolver().GetFileSystemType());
            return result;
        }


        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("modified")]
        public async Task<QueryModifiedResponse> GetModified([FromBody] TransitQueryModifiedRequest request)
        {
            var result = await _transitQueryService.GetModified((OdinId)request.OdinId, request, GetFileSystemResolver().GetFileSystemType());
            return QueryModifiedResponse.FromResult(result);
        }

        /// <summary>
        /// Executes a QueryBatch operation on a remote identity server
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] TransitQueryBatchRequest request)
        {
            var batch = await _transitQueryService.GetBatch((OdinId)request.OdinId, request, GetFileSystemResolver().GetFileSystemType());
            return QueryBatchResponse.FromResult(batch);
        }

        /// <summary>
        /// Retrieves a file's header and metadata from a remote identity server
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] TransitExternalFileIdentifier request)
        {
            var fst = base.GetFileSystemResolver().GetFileSystemType();
            SharedSecretEncryptedFileHeader result =
                await _transitQueryService.GetFileHeader((OdinId)request.OdinId, request.File, GetFileSystemResolver().GetFileSystemType());

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
        /// This is a byte array where the first 16 bytes are the IV, second 48 bytes are the EncryptedAESKey, and last 4 bytes is the encryption version
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("payload")]
        public async Task<IActionResult> GetPayloadStream([FromBody] TransitGetPayloadRequest request)
        {
            var (encryptedKeyHeader, isEncrypted, payloadStream) = await _transitQueryService.GetPayloadStream((OdinId)request.OdinId,
                request.File, request.Key, request.Chunk, GetFileSystemResolver().GetFileSystemType());

            if (payloadStream == null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadKey, payloadStream.Key);
            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(payloadStream.LastModified);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, payloadStream.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());
            return new FileStreamResult(payloadStream.Stream, "application/octet-stream");
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
            var (encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb) =
                await _transitQueryService.GetThumbnail((OdinId)request.OdinId, request.File, request.Width, request.Height,
                    GetFileSystemResolver().GetFileSystemType());

            if (thumb == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(lastModified);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());
            return new FileStreamResult(thumb, "application/octet-stream");
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("metadata/type")]
        public async Task<PagedResult<ClientDriveData>> GetDrivesByType([FromBody] TransitGetDrivesByTypeRequest request)
        {
            var drives = await _transitQueryService.GetDrivesByType((OdinId)request.OdinId, request.DriveType, GetFileSystemResolver().GetFileSystemType());
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