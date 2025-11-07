using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Controllers.Anonymous.Cdn;

/// <summary>
/// These endpoints are meant to be called exclusively by a CDN
/// </summary>

[ApiController]
[Route(CdnApiPathConstants.DriveStorageV1)]
[AllowAnonymous]
public class CdnDriveStorageController(
    OdinConfiguration config,
    PeerOutgoingTransferService peerOutgoingTransferService)
    : DriveStorageControllerBase(peerOutgoingTransferService)
{
    /// <summary>
    /// Retrieves a file's payload
    /// </summary>
    [HttpPost("payload")]
    public new async Task<IActionResult> GetPayloadStream([FromBody] GetPayloadRequest request)
    {
        if (!config.Cdn.Enabled)
        {
            return StatusCode(403, new { error = "Forbidden", message = "CDN not enabled on backend" });
        }

        return await base.GetPayloadStream(request);
    }

    /// <summary>
    /// Retrieves a file's payload
    /// </summary>
    [HttpGet("payload")]
    public async Task<IActionResult> GetPayloadAsGetRequest(
        [FromQuery] Guid fileId,
        [FromQuery] Guid alias,
        [FromQuery] Guid type,
        [FromQuery] string key)
    {
        if (!config.Cdn.Enabled)
        {
            return StatusCode(403, new { error = "Forbidden", message = "CDN not enabled on backend" });
        }

        FileChunk chunk = this.GetChunk(null, null);

        return await base.GetPayloadStream(
            new GetPayloadRequest()
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = fileId,
                    TargetDrive = new()
                    {
                        Alias = alias,
                        Type = type
                    }
                },
                Key = key,
                Chunk = chunk
            });
    }

    /// <summary>
    /// Retrieves a thumbnail.  The available thumbnails are defined on the AppFileMeta.
    ///
    /// See GET files/header
    /// </summary>
    [HttpPost("thumb")]
    public new async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
    {
        if (!config.Cdn.Enabled)
        {
            return StatusCode(403, new { error = "Forbidden", message = "CDN not enabled on backend" });
        }

        return await base.GetThumbnail(request);
    }

    /// <summary>
    /// Retrieves a thumbnail.  The available thumbnails are defined on the AppFileMeta.
    ///
    /// See GET files/header
    /// </summary>
    [HttpGet("thumb")]
    public async Task<IActionResult> GetThumbnailAsGetRequest(
        [FromQuery] Guid fileId,
        [FromQuery] string payloadKey,
        [FromQuery] Guid alias,
        [FromQuery] Guid type,
        [FromQuery] int width,
        [FromQuery] int height)
    {
        if (!config.Cdn.Enabled)
        {
            return StatusCode(403, new { error = "Forbidden", message = "CDN not enabled on backend" });
        }

        return await base.GetThumbnail(new GetThumbnailRequest()
        {
            File = new ExternalFileIdentifier()
            {
                FileId = fileId,
                TargetDrive = new()
                {
                    Alias = alias,
                    Type = type
                }
            },
            Width = width,
            Height = height,
            PayloadKey = payloadKey,
        });
    }
}