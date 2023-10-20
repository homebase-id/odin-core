using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.SendingHost;

namespace Odin.Hosting.Controllers.Base.Drive.Specialized
{
    public abstract class DriveQueryByUniqueIdControllerBase : DriveStorageControllerBase
    {
        protected DriveQueryByUniqueIdControllerBase(
            ILogger logger,
            FileSystemResolver fileSystemResolver,
            ITransitService transitService
        ) : base(logger, fileSystemResolver, transitService)
        {
        }

        [HttpGet("header")]
        public async Task<IActionResult> GetFileHeaderByUniqueId([FromQuery] Guid clientUniqueId, [FromQuery] Guid alias, [FromQuery] Guid type)
        {
            var result = await GetFileHeaderByUniqueIdInternal(clientUniqueId, alias, type);
            if (result == null)
            {
                return NotFound();
            }

            AddCacheHeader();

            return new JsonResult(result);
        }

        [HttpGet("payload")]
        public async Task<IActionResult> GetPayloadStreamByUniqueId([FromQuery] Guid clientUniqueId, [FromQuery] Guid alias, [FromQuery] Guid type,
            [FromQuery] string key,
            [FromQuery] int? chunkStart, [FromQuery] int? chunkLength)
        {
            FileChunk chunk = null;
            if (Request.Headers.TryGetValue("Range", out var rangeHeaderValue) &&
                RangeHeaderValue.TryParse(rangeHeaderValue, out var range))
            {
                var firstRange = range.Ranges.First();
                if (firstRange.From != null && firstRange.To != null)
                {
                    HttpContext.Response.StatusCode = 206;

                    int start = Convert.ToInt32(firstRange.From ?? 0);
                    int end = Convert.ToInt32(firstRange.To ?? int.MaxValue);

                    chunk = new FileChunk()
                    {
                        Start = start,
                        Length = end - start + 1
                    };
                }
            }
            else if (chunkStart.HasValue)
            {
                chunk = new FileChunk()
                {
                    Start = chunkStart.GetValueOrDefault(),
                    Length = chunkLength.GetValueOrDefault(int.MaxValue)
                };
            }

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
            [FromQuery] int height)
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
                Height = height
            });
        }

        private async Task<SharedSecretEncryptedFileHeader> GetFileHeaderByUniqueIdInternal(Guid clientUniqueId, Guid alias, Guid type)
        {
            var queryService = GetFileSystemResolver().ResolveFileSystem().Query;

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