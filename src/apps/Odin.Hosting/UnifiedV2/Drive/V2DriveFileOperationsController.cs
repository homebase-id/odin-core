using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive
{
    /// <summary>
    /// File operations for the Unified V2 Drive API.
    /// Includes metadata updates, deletion operations (single and batch), 
    /// hard deletes, and read receipts.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.ByFileId)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveFileOperationsController(PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        /// <summary>
        /// Updates local metadata tags for a file.
        /// </summary>
        /// <remarks>
        /// The <c>LocalVersionTag</c> may be <c>Guid.Empty</c> for new files that do not yet
        /// have local content. Tags are merged or replaced as determined by the underlying storage engine.
        /// </remarks>
        /// <param name="request">Tag update parameters.</param>
        /// <returns>The updated metadata information.</returns>
        [HttpPatch("update-local-metadata-tags")]
        [SwaggerOperation(
            Summary = "Update local metadata tags",
            Description = "Updates tag metadata for a specified file in local storage.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataTags(
            [FromBody] UpdateLocalMetadataTagsRequest request)
        {
            OdinValidationUtils.AssertIsTrue(request.File.HasValue(), "File is invalid");

            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();
            return await fs.Storage.UpdateLocalMetadataTags(
                MapToInternalFile(request.File),
                request.LocalVersionTag,
                request.Tags,
                WebOdinContext);
        }

        /// <summary>
        /// Updates local metadata content, such as initialization vectors or other file metadata sections.
        /// </summary>
        /// <remarks>
        /// The <c>LocalVersionTag</c> may be <c>Guid.Empty</c> for files without local content.
        /// </remarks>
        /// <param name="request">Content update parameters.</param>
        /// <returns>The updated metadata information.</returns>
        [HttpPatch("update-local-metadata-content")]
        [SwaggerOperation(
            Summary = "Update local metadata content",
            Description = "Updates internal local metadata content sections for a file.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataContent(
            [FromBody] UpdateLocalMetadataContentRequest request)
        {
            OdinValidationUtils.AssertIsTrue(request.File.HasValue(), "File is invalid");

            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();
            return await fs.Storage.UpdateLocalMetadataContent(
                MapToInternalFile(request.File),
                request.LocalVersionTag,
                request.Iv,
                request.Content,
                WebOdinContext);
        }

        /// <summary>
        /// Deletes a single file.
        /// </summary>
        /// <remarks>
        /// <b>Why POST instead of DELETE?</b><br/>
        /// Although HTTP technically permits bodies in DELETE requests, real-world infrastructure does not:
        /// API gateways, proxies, load balancers, security filters, and even some HTTP clients
        /// often ignore, strip, or outright block DELETE request bodies.
        /// <br/><br/>
        /// Because this operation requires a request body, using POST is the reliable, industry-standard pattern.
        /// </remarks>
        /// <param name="request">The file to delete.</param>
        [HttpPost("delete")]
        [SwaggerOperation(
            Summary = "Delete a file",
            Description = "Deletes a file using a POST request for maximum compatibility.",
            Tags = [SwaggerInfo.FileWrite]
        )]
        public new async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest request)
        {
            return await base.DeleteFile(request);
        }

        /// <summary>
        /// Deletes a payload associated with a file.
        /// </summary>
        /// <param name="request">Payload delete parameters.</param>
        /// <returns>The result of the delete operation.</returns>
        [HttpPost("delete-payload")]
        [SwaggerOperation(
            Summary = "Delete file payload",
            Description = "Deletes a payload associated with a file without removing the file entry itself.",
            Tags = [SwaggerInfo.FileDelete]
        )]
        public async Task<DeletePayloadResult> DeletePayloadC(DeletePayloadRequest request)
        {
            return await DeletePayload(request);
        }

        /// <summary>
        /// Permanently deletes a file with no recovery possible.
        /// </summary>
        /// <remarks>
        /// A <b>hard delete</b> removes all file content, metadata, and historical references.  
        /// This action is irreversible.
        /// </remarks>
        /// <param name="request">The file to permanently delete.</param>
        [HttpPost("hard-delete")]
        [SwaggerOperation(
            Summary = "Hard delete a file",
            Description = "Permanently removes a file and all associated metadata. Irreversible.",
            Tags = [SwaggerInfo.FileDelete]
        )]
        public async Task<IActionResult> HardDeleteFileC([FromBody] DeleteFileRequest request)
        {
            return await HardDeleteFile(request);
        }

        /// <summary>
        /// Sends a read receipt for a file.
        /// </summary>
        /// <remarks>
        /// This is used for synchronization and analytics across peer transfers.
        /// </remarks>
        /// <param name="request">Read-receipt parameters.</param>
        /// <returns>A structured result indicating the receipt was processed.</returns>
        [HttpPost("send-read-receipt")]
        [SwaggerOperation(
            Summary = "Send read receipt",
            Description = "Sends a read receipt to the peer transfer service.",
            Tags = [SwaggerInfo.FileTransfer]
        )]
        public new async Task<IActionResult> SendReadReceipt(SendReadReceiptRequest request)
        {
            var result = await base.SendReadReceipt(request);
            return new JsonResult(result);
        }
    }
}
