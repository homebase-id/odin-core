using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Storage.SQLite;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Controllers.Base.Drive.Specialized
{
    public abstract class DriveQueryByUniqueIdControllerBase(
        PeerOutgoingTransferService peerOutgoingTransferService,
        TenantSystemStorage tenantSystemStorage)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        [HttpGet("header")]
        public async Task<IActionResult> GetFileHeaderByUniqueId([FromQuery] Guid clientUniqueId, [FromQuery] Guid alias, [FromQuery] Guid type)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var result = await GetFileHeaderByUniqueIdInternal(clientUniqueId, alias, type, db);
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
            var db = tenantSystemStorage.IdentityDatabase;
            var header = await this.GetFileHeaderByUniqueIdInternal(clientUniqueId, alias, type, db);
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
                },
                db);
        }

        [HttpGet("thumb")]
        public async Task<IActionResult> GetThumbnailStreamByUniqueId([FromQuery] Guid clientUniqueId, [FromQuery] Guid alias, [FromQuery] Guid type,
            [FromQuery] int width,
            [FromQuery] int height,
            [FromQuery] string payloadKey)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            var header = await this.GetFileHeaderByUniqueIdInternal(clientUniqueId, alias, type, db);
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
                },
                db);
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileHeaderByUniqueIdInternal(Guid clientUniqueId, Guid alias, Guid type, IdentityDatabase db)
        {
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;

            var driveId = WebOdinContext.PermissionsContext.GetDriveId(new TargetDrive()
            {
                Alias = alias,
                Type = type
            });

            var result = await queryService.GetFileByClientUniqueId(driveId, clientUniqueId, excludePreviewThumbnail: false, odinContext: WebOdinContext,
                db: db);
            return result;
        }
    }
}