using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Drive;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerDriveStorageController(
        ILogger<OwnerDriveStorageController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveStorageControllerBase(peerOutgoingTransferService)
    {
        private readonly ILogger<OwnerDriveStorageController> _logger = logger;

        /// <summary>
        /// Indicates if the specified TempFile exists.  This is used for testing only
        /// </summary>
        [SwaggerOperation(Tags = [ControllerConstants.OwnerDrive])]
        [HttpGet("temp-file-exists")]
        public async Task<bool> TempFileExists([FromQuery] Guid fileId,
            [FromQuery] Guid alias,
            [FromQuery] Guid type,
            [FromQuery] TempStorageType storageType,
            [FromQuery] string extension)
        {
            var tempFile = new TempFile()
            {
                File = MapToInternalFile(new ExternalFileIdentifier()
                {
                    FileId = fileId,
                    TargetDrive = new TargetDrive()
                    {
                        Alias = alias,
                        Type = type
                    }
                }),
                StorageType = storageType
            };

            var result = await this.GetHttpFileSystemResolver()
                .ResolveFileSystem()
                .Storage
                .TempFileExists(tempFile, extension, WebOdinContext);

            return result;
        }

        /// <summary>
        /// Indicates if the specified TempFile exists.  This is used for testing only
        /// </summary>
        [SwaggerOperation(Tags = [ControllerConstants.OwnerDrive])]
        [HttpGet("has-orphan-payloads")]
        public async Task<bool> VerifyNoOrphans([FromQuery] Guid fileId,
            [FromQuery] Guid alias,
            [FromQuery] Guid type)
        {
            var file = MapToInternalFile(new ExternalFileIdentifier()
            {
                FileId = fileId,
                TargetDrive = new TargetDrive()
                {
                    Alias = alias,
                    Type = type
                }
            });

            var result = await this.GetHttpFileSystemResolver()
                .ResolveFileSystem()
                .Storage
                .HasOrphanPayloads(file, WebOdinContext);

            return result;
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("header")]
        public new async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            return await base.GetFileHeader(request);
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [HttpGet("header")]
        public async Task<IActionResult> GetFileHeaderAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type)
        {
            return await base.GetFileHeader(
                new ExternalFileIdentifier()
                {
                    FileId = fileId,
                    TargetDrive = new TargetDrive()
                    {
                        Alias = alias,
                        Type = type
                    }
                });
        }

        [HttpGet("transfer-history")]
        public async Task<FileTransferHistoryResponse> GetFileTransferHistory([FromQuery] Guid fileId, [FromQuery] Guid alias,
            [FromQuery] Guid type)
        {
            var result = await base.GetFileTransferHistory(new ExternalFileIdentifier()
            {
                FileId = fileId,
                TargetDrive = new TargetDrive()
                {
                    Alias = alias,
                    Type = type
                }
            });

            if (null == result)
            {
                Response.StatusCode = (int)HttpStatusCode.NotFound;
                return null;
            }

            return result;
        }

        /// <summary>
        /// Retrieves a file's header and metadata by globalTransitId
        /// </summary>
        [HttpGet("header_byglobaltransitid")]
        public async Task<IActionResult> GetFileHeaderByGlobalTransitId([FromQuery] Guid globalTransitId, [FromQuery] Guid alias,
            [FromQuery] Guid type)
        {
            return await base.GetFileHeaderByGlobalTransitId(
                new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId = globalTransitId,
                    TargetDrive = new TargetDrive()
                    {
                        Alias = alias,
                        Type = type
                    }
                });
        }

        /// <summary>
        /// Retrieves a file's payload
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("payload")]
        public new async Task<IActionResult> GetPayloadStream([FromBody] GetPayloadRequest request)
        {
            return await base.GetPayloadStream(request);
        }


        /// <summary>
        /// Retrieves a file's payload
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("payload")]
        public async Task<IActionResult> GetPayloadAsGetRequest([FromQuery] Guid fileId, [FromQuery] Guid alias, [FromQuery] Guid type,
            [FromQuery] string key)
        {
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
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("thumb")]
        public new async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            return await base.GetThumbnail(request);
        }

        /// <summary>
        /// Retrieves a thumbnail.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// See GET files/header
        /// </summary>
        [HttpGet("thumb")]
        public async Task<IActionResult> GetThumbnailAsGetRequest([FromQuery] Guid fileId, [FromQuery] string payloadKey,
            [FromQuery] Guid alias,
            [FromQuery] Guid type, [FromQuery] int width,
            [FromQuery] int height)
        {
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


        /// <summary>
        /// Deletes a file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("delete")]
        public new async Task<IActionResult> DeleteFile([FromBody] DeleteFileRequest request)
        {
            return await base.DeleteFile(request);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("deletefileidbatch")]
        public new async Task<IActionResult> DeleteFileIdBatch([FromBody] DeleteFileIdBatchRequest request)
        {
            return await base.DeleteFileIdBatch(request);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("deletegroupidbatch")]
        public new async Task<IActionResult> DeleteFilesByGroupIdBatch([FromBody] DeleteFilesByGroupIdBatchRequest request)
        {
            return await base.DeleteFilesByGroupIdBatch(request);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("deletepayload")]
        public async Task<DeletePayloadResult> DeletePayloadC(DeletePayloadRequest request)
        {
            return await base.DeletePayload(request);
        }

        /// <summary>
        /// Hard deletes a file which means the file is gone w/o a trace
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("harddelete")]
        public async Task<IActionResult> HardDeleteFileC([FromBody] DeleteFileRequest request)
        {
            return await base.HardDeleteFile(request);
        }

        [HttpPost("send-read-receipt")]
        public new async Task<IActionResult> SendReadReceipt(SendReadReceiptRequest request)
        {
            var result = await base.SendReadReceipt(request);
            return new JsonResult(result);
        }
    }
}