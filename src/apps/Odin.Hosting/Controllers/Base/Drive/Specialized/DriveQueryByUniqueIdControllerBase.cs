﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Controllers.Base.Drive.Specialized
{
    public abstract class DriveQueryByUniqueIdControllerBase(FileSystemResolver fileSystemResolver, IPeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(fileSystemResolver, peerOutgoingTransferService)
    {
        [HttpGet("header")]
        public async Task<IActionResult> GetFileHeaderByUniqueId([FromQuery] Guid clientUniqueId, [FromQuery] Guid alias, [FromQuery] Guid type)
        {
            var result = await GetFileHeaderByUniqueIdInternal(clientUniqueId, alias, type);
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpGet("payload")]
        public async Task<IActionResult> GetPayloadStreamByUniqueId([FromQuery] Guid clientUniqueId, [FromQuery] Guid alias, [FromQuery] Guid type,
            [FromQuery] string key,
            [FromQuery] int? chunkStart, [FromQuery] int? chunkLength)
        {
            FileChunk chunk = this.GetChunk(chunkStart, chunkLength);
            var header = await this.GetFileHeaderByUniqueIdInternal(clientUniqueId, alias, type);
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

        [HttpGet("thumb")]
        public async Task<IActionResult> GetThumbnailStreamByUniqueId([FromQuery] Guid clientUniqueId, [FromQuery] Guid alias, [FromQuery] Guid type,
            [FromQuery] int width,
            [FromQuery] int height,
            [FromQuery] string payloadKey)
        {
            var header = await this.GetFileHeaderByUniqueIdInternal(clientUniqueId, alias, type);
            if (null == header)
            {
                return NotFound();
            }

            return await GetThumbnail(new GetThumbnailRequest()
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = header.FileId,
                    TargetDrive = new()
                    {
                        Alias = alias,
                        Type = type
                    }
                },
                Width = width,
                Height = height,
                PayloadKey = payloadKey
            });
        }

        ///
        /// These POST methods are due to an issue in Refit >:-[
        ///
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeaderByUniqueIdAsPost([FromBody] ClientUniqueIdFileIdentifier file)
        {
            return await this.GetFileHeaderByUniqueId(file.ClientUniqueId, file.TargetDrive.Alias, file.TargetDrive.Type);
        }

        [HttpPost("payload")]
        public async Task<IActionResult> GetPayloadStreamByUniqueIdAsPost([FromBody] GetPayloadByUniqueIdRequest request)
        {
            return await this.GetPayloadStreamByUniqueId(
                request.UniqueId,
                request.TargetDrive.Alias,
                request.TargetDrive.Type,
                request.Key,
                request.Chunk?.Start,
                request.Chunk?.Length);
        }

        [HttpPost("thumb")]
        public async Task<IActionResult> GetThumbnailStreamByUniqueIdAsPost([FromBody] GetThumbnailByUniqueIdRequest request)
        {
            return await this.GetThumbnailStreamByUniqueId(
                request.ClientUniqueId,
                request.TargetDrive.Alias,
                request.TargetDrive.Type,
                request.Width,
                request.Height,
                request.PayloadKey);
        }


        private async Task<SharedSecretEncryptedFileHeader> GetFileHeaderByUniqueIdInternal(Guid clientUniqueId, Guid alias, Guid type)
        {
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;

            var driveId = OdinContext.PermissionsContext.GetDriveId(new TargetDrive()
            {
                Alias = alias,
                Type = type
            });

            var result = await queryService.GetFileByClientUniqueId(driveId, clientUniqueId, excludePreviewThumbnail: false);
            return result;
        }
    }
}