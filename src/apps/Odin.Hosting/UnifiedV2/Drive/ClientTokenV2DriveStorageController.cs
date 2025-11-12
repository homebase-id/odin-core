using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.ClientToken.Shared;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.UnifiedV2.Drive
{
    /// <summary>
    /// Api endpoints for reading drives
    /// </summary>
    [ApiController]
    [Route(AppApiPathConstantsV2.DriveV2 + "/files")]
    [Route(GuestApiPathConstantsV2.DriveV2 + "/files")]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenV2DriveStorageController(
        ILogger<ClientTokenV2DriveStorageController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        private readonly ILogger<ClientTokenV2DriveStorageController> _logger = logger;

        [HttpGet("header")]
        public async Task<IActionResult> GetFileHeader([FromQuery] Guid fileId, [FromQuery] Guid driveId)
        {
            var storage = this.GetHttpFileSystemResolver().ResolveFileSystem().Storage;
            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            var result = await storage.GetSharedSecretEncryptedHeader(file, WebOdinContext);

            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpGet("payload")]
        public async Task<IActionResult> GetPayload([FromQuery] Guid fileId, [FromQuery] Guid driveId,
            [FromQuery] string key,
            [FromQuery] int? chunkStart,
            [FromQuery] int? chunkLength)
        {
            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            FileChunk chunk = this.GetChunk(chunkStart, chunkLength);
            var payload = await GetPayloadStream(file, key, chunk);

            if (WebOdinContext.Caller.IsAnonymous)
            {
                HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            }

            return payload;
        }

        [HttpGet("thumb")]
        [HttpGet("thumb.{extension}")] // for link-preview support in signal/whatsapp
        public async Task<IActionResult> GetThumbnailAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid driveId,
            [FromQuery] int width, [FromQuery] int height,
            [FromQuery] string payloadKey,
            [FromQuery] bool directMatchOnly)
        {
            var file = new InternalDriveFileId()
            {
                FileId = fileId,
                DriveId = driveId
            };

            return await GetThumbnail(file, width, height, payloadKey, directMatchOnly);
        }
    }
}