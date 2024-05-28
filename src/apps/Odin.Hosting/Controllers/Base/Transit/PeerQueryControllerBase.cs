﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Core.Time;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Transit
{
    /// <summary>
    /// Routes requests from the owner app to a target identity
    /// </summary>
    public class PeerQueryControllerBase(PeerDriveQueryService peerDriveQueryService, TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpPost("batchcollection")]
        public async Task<QueryBatchCollectionResponse> GetBatchCollection([FromBody] PeerQueryBatchCollectionRequest request)
        {
            AssertIsValidOdinId(request.OdinId, out var id);
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await peerDriveQueryService.GetBatchCollection(id, request, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext, cn);
            return result;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpPost("modified")]
        public async Task<QueryModifiedResponse> GetModified([FromBody] PeerQueryModifiedRequest request)
        {
            AssertIsValidOdinId(request.OdinId, out var id);
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await peerDriveQueryService.GetModified(id, request, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext, cn);
            return QueryModifiedResponse.FromResult(result);
        }

        /// <summary>
        /// Executes a QueryBatch operation on a remote identity server
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] PeerQueryBatchRequest request)
        {
            AssertIsValidOdinId(request.OdinId, out var id);
            using var cn = tenantSystemStorage.CreateConnection();
            var batch = await peerDriveQueryService.GetBatch(id, request, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext, cn);
            return QueryBatchResponse.FromResult(batch);
        }

        /// <summary>
        /// Retrieves a file's header and metadata from a remote identity server
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] TransitExternalFileIdentifier request)
        {
            AssertIsValidOdinId(request.OdinId, out var id);
            using var cn = tenantSystemStorage.CreateConnection();
            SharedSecretEncryptedFileHeader result =
                await peerDriveQueryService.GetFileHeader(id, request.File, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext, cn);

            if (null == result)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpGet("header")]
        public async Task<IActionResult> GetFileHeaderAsGetRequest([FromQuery] string odinId, [FromQuery] Guid fileId, [FromQuery] Guid alias,
            [FromQuery] Guid type)
        {
            return await this.GetFileHeader(
                new TransitExternalFileIdentifier()
                {
                    OdinId = odinId,
                    File = new ExternalFileIdentifier()
                    {
                        FileId = fileId,
                        TargetDrive = new TargetDrive()
                        {
                            Alias = alias,
                            Type = type
                        }
                    }
                });
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
        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpPost("payload")]
        public async Task<IActionResult> GetPayloadStream([FromBody] TransitGetPayloadRequest request)
        {
            AssertIsValidOdinId(request.OdinId, out var id);
            using var cn = tenantSystemStorage.CreateConnection();
            var (encryptedKeyHeader, isEncrypted, payloadStream) = await peerDriveQueryService.GetPayloadStream(id,
                request.File, request.Key, request.Chunk, GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext, cn);

            return HandlePayloadResponse(encryptedKeyHeader, isEncrypted, payloadStream);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpGet("payload")]
        public async Task<IActionResult> GetPayloadAsGetRequest([FromQuery] string odinId, [FromQuery] Guid fileId, [FromQuery] Guid alias,
            [FromQuery] Guid type, [FromQuery] string key)
        {
            FileChunk chunk = this.GetChunk(null, null);
            return await this.GetPayloadStream(
                new TransitGetPayloadRequest()
                {
                    OdinId = odinId,
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
        /// Retrieves an encrypted thumbnail from a remote identity server.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// The content type of the decrypted content is found in the header 'DecryptedContentType'
        ///
        /// The owner shared secret encrypted key header for the thumbnail is found in the header 'sharedSecretEncryptedHeader64'
        ///
        /// The flag indicating if the payload is encrypted is found in the header 'PayloadEncrypted'
        /// This is a byte array where the first 16 bytes are the IV, second 48 bytes are the EncryptedAESKey, and last 4 bytes is the encryption version
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpPost("thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] TransitGetThumbRequest request)
        {
            AssertIsValidOdinId(request.OdinId, out var id);
            using var cn = tenantSystemStorage.CreateConnection();
            var (encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb) =
                await peerDriveQueryService.GetThumbnail(id, request.File, request.Width, request.Height,
                    request.PayloadKey,
                    GetHttpFileSystemResolver().GetFileSystemType(), WebOdinContext, cn);

            return HandleThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpGet("thumb")]
        public async Task<IActionResult> GetThumbnailAsGetRequest([FromQuery] string odinId, [FromQuery] Guid fileId, [FromQuery] string payloadKey,
            [FromQuery] Guid alias,
            [FromQuery] Guid type, [FromQuery] int width,
            [FromQuery] int height)
        {
            return await this.GetThumbnail(new TransitGetThumbRequest()
            {
                OdinId = odinId,
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

        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpPost("metadata/type")]
        public async Task<PagedResult<ClientDriveData>> GetDrivesByType([FromBody] TransitGetDrivesByTypeRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var drives = await peerDriveQueryService.GetDrivesByType((OdinId)request.OdinId, request.DriveType, GetHttpFileSystemResolver().GetFileSystemType(),
                WebOdinContext, cn);
            var clientDriveData = drives.Select(drive => new ClientDriveData()
                {
                    TargetDrive = drive.TargetDrive,
                    Attributes = drive.Attributes
                }).ToList();

            var page = new PagedResult<ClientDriveData>(PageOptions.All, 1, clientDriveData);
            return page;
        }


        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpGet("header_byglobaltransitid")]
        public async Task<IActionResult> GetFileHeaderByGlobalTransitId([FromQuery] string odinId,
            [FromQuery] Guid globalTransitId,
            [FromQuery] Guid alias,
            [FromQuery] Guid type)
        {
            AssertIsValidOdinId(odinId, out var id);

            var fst = GetHttpFileSystemResolver().GetFileSystemType();
            var file = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = globalTransitId,
                TargetDrive = new TargetDrive()
                {
                    Alias = alias,
                    Type = type
                }
            };

            using var cn = tenantSystemStorage.CreateConnection();
            var result = await peerDriveQueryService.GetFileHeaderByGlobalTransitId(id, file, fst, WebOdinContext, cn);

            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpGet("payload_byglobaltransitid")]
        public async Task<IActionResult> GetPayloadStreamByGlobalTransitId([FromQuery] string odinId,
            [FromQuery] Guid globalTransitId, [FromQuery] Guid alias, [FromQuery] Guid type,
            [FromQuery] string key,
            [FromQuery] int? chunkStart, [FromQuery] int? chunkLength)
        {
            AssertIsValidOdinId(odinId, out var id);
            var fst = GetHttpFileSystemResolver().GetFileSystemType();
            var file = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = globalTransitId,
                TargetDrive = new TargetDrive()
                {
                    Alias = alias,
                    Type = type
                }
            };

            var chunk = GetChunk(chunkStart, chunkLength);

            using var cn = tenantSystemStorage.CreateConnection();
            var (encryptedKeyHeader, isEncrypted, payloadStream) =
                await peerDriveQueryService.GetPayloadByGlobalTransitId(id, file, key, chunk, fst, WebOdinContext, cn);

            return HandlePayloadResponse(encryptedKeyHeader, isEncrypted, payloadStream);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.PeerQuery })]
        [HttpGet("thumb_byglobaltransitid")]
        public async Task<IActionResult> GetThumbnailStreamByGlobalTransitId([FromQuery] string odinId,
            [FromQuery] Guid globalTransitId,
            [FromQuery] Guid alias,
            [FromQuery] Guid type,
            [FromQuery] int width,
            [FromQuery] int height,
            [FromQuery] bool directMatchOnly,
            [FromQuery] string payloadKey)
        {
            AssertIsValidOdinId(odinId, out var id);

            var fst = GetHttpFileSystemResolver().GetFileSystemType();
            var file = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = globalTransitId,
                TargetDrive = new TargetDrive()
                {
                    Alias = alias,
                    Type = type
                }
            };

            using var cn = tenantSystemStorage.CreateConnection();
            var (encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb) =
                await peerDriveQueryService.GetThumbnailByGlobalTransitId(id, file, payloadKey, width, height, directMatchOnly, fst, WebOdinContext, cn);

            return HandleThumbnailResponse(encryptedKeyHeader, isEncrypted, decryptedContentType, lastModified, thumb);
        }

        private IActionResult HandleThumbnailResponse(EncryptedKeyHeader encryptedKeyHeader, bool isEncrypted, string decryptedContentType,
            UnixTimeUtc? lastModified, Stream thumb)
        {
            if (thumb == Stream.Null)
            {
                return NotFound();
            }

            AddGuestApiCacheHeader();

            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
            HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(lastModified);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());
            return new FileStreamResult(thumb, "application/octet-stream");
        }

        private IActionResult HandlePayloadResponse(EncryptedKeyHeader encryptedKeyHeader, bool isEncrypted, PayloadStream payloadStream)
        {
            if (payloadStream == null)
            {
                return NotFound();
            }

            AddGuestApiCacheHeader();

            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadKey, payloadStream.Key);
            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(payloadStream.LastModified);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, payloadStream.ContentType);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());
            return new FileStreamResult(payloadStream.Stream, "application/octet-stream");
        }
    }
}