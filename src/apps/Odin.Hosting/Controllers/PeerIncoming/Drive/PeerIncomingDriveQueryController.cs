﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Storage.SQLite;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Query;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.PeerIncoming.Drive
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.DriveV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCertificateAuthScheme)]
    public class PeerIncomingDriveQueryController(DriveManager driveManager, TenantSystemStorage tenantSystemStorage) : OdinControllerBase
    {
        [HttpPost("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection(QueryBatchCollectionRequest request)
        {
            var perimeterService = GetPerimeterQueryService();
            using var cn = tenantSystemStorage.CreateConnection();
            return await perimeterService.QueryBatchCollection(request, WebOdinContext, cn);
        }

        [HttpPost("querymodified")]
        public async Task<QueryModifiedResponse> QueryModified(QueryModifiedRequest request)
        {
            var perimeterService = GetPerimeterQueryService();
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await perimeterService.QueryModified(request.QueryParams, request.ResultOptions, WebOdinContext, cn);
            return QueryModifiedResponse.FromResult(result);
        }

        [HttpPost("querybatch")]
        public async Task<QueryBatchResponse> QueryBatch(QueryBatchRequest request)
        {
            var perimeterService = GetPerimeterQueryService();
            var options = request.ResultOptionsRequest ?? QueryBatchResultOptionsRequest.Default;
            using var cn = tenantSystemStorage.CreateConnection();
            var batch = await perimeterService.QueryBatch(request.QueryParams, options.ToQueryBatchResultOptions(), WebOdinContext, cn);
            return QueryBatchResponse.FromResult(batch);
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            var perimeterService = GetPerimeterQueryService();
            using var cn = tenantSystemStorage.CreateConnection();
            SharedSecretEncryptedFileHeader result = await perimeterService.GetFileHeader(request.TargetDrive, request.FileId, WebOdinContext, cn);

            //404 is possible
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        /// <summary>
        /// Retrieves a file's encrypted payload
        /// </summary>
        [HttpPost("payload")]
        public async Task<IActionResult> GetPayloadStream([FromBody] GetPayloadRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var perimeterService = GetPerimeterQueryService();
            var (encryptedKeyHeader64, isEncrypted, _, payloadStream) =
                await perimeterService.GetPayloadStream(
                    request.File.TargetDrive,
                    request.File.FileId,
                    request.Key,
                    request.Chunk,
                    WebOdinContext,
                    cn);

            if (payloadStream == null)
            {
                return NotFound();
            }

            // Indicates the payload is missing
            if (payloadStream.Stream == Stream.Null)
            {
                return StatusCode((int)HttpStatusCode.Gone);
            }

            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(payloadStream.LastModified);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, payloadStream.ContentType);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.IcrEncryptedSharedSecret64Header, encryptedKeyHeader64);
            return new FileStreamResult(payloadStream.Stream, "application/octet-stream");
        }

        /// <summary>
        /// Retrieves an encrypted thumbnail.  The available thumbnails are defined on the AppFileMeta.
        ///
        /// See GET files/header
        /// </summary>
        /// <param name="request"></param>
        [HttpPost("thumb")]
        public async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            var perimeterService = GetPerimeterQueryService();

            using var cn = tenantSystemStorage.CreateConnection();
            var (encryptedKeyHeader64, isEncrypted, _, decryptedContentType, lastModified, thumb) =
                await perimeterService.GetThumbnail(request.File.TargetDrive, request.File.FileId, request.Height, request.Width, request.PayloadKey,
                    WebOdinContext, cn);

            if (thumb == null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
            HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, decryptedContentType);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.IcrEncryptedSharedSecret64Header, encryptedKeyHeader64);
            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(lastModified);
            return new FileStreamResult(thumb, "application/octet-stream");
        }

        [HttpPost("metadata/type")]
        public async Task<IEnumerable<PerimeterDriveData>> GetDrives([FromBody] GetDrivesByTypeRequest request)
        {
            var perimeterService = GetPerimeterQueryService();
            using var cn = tenantSystemStorage.CreateConnection();
            var drives = await perimeterService.GetDrives(request.DriveType, WebOdinContext, cn);
            return drives;
        }

        ///
        [HttpPost("header_byglobaltransitid")]
        public async Task<IActionResult> GetFileHeaderByGlobalTransitId([FromBody] GlobalTransitIdFileIdentifier file)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await LookupFileHeaderByGlobalTransitId(file, cn);
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpPost("header_byuniqueid")]
        public async Task<IActionResult> GetFileHeaderByUniqueId([FromBody] GetFileHeaderByUniqueIdRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await LookupHeaderByUniqueId(request.UniqueId, request.TargetDrive, cn);
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpPost("payload_byglobaltransitid")]
        public async Task<IActionResult> GetPayloadStreamByGlobalTransitId([FromBody] GetPayloadByGlobalTransitIdRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var header = await this.LookupFileHeaderByGlobalTransitId(request.File, cn);
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
                        TargetDrive = request.File.TargetDrive
                    },
                    Key = request.Key,
                    Chunk = request.Chunk
                });
        }

        [HttpPost("payload_byuniqueid")]
        public async Task<IActionResult> GetPayloadStreamByUniqueId([FromBody] GetPayloadByUniqueIdRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var header = await this.LookupHeaderByUniqueId(request.UniqueId, request.TargetDrive, cn);
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
                        TargetDrive = request.TargetDrive
                    },
                    Key = request.Key,
                    Chunk = request.Chunk
                });
        }

        [HttpPost("thumb_byglobaltransitid")]
        public async Task<IActionResult> GetThumbnailStreamByGlobalTransitId([FromBody] GetThumbnailByGlobalTransitIdRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var header = await this.LookupFileHeaderByGlobalTransitId(request.File, cn);
            if (null == header)
            {
                return NotFound();
            }

            return await GetThumbnail(new GetThumbnailRequest()
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = header.FileId,
                    TargetDrive = request.File.TargetDrive
                },
                Width = request.Width,
                Height = request.Height,
                PayloadKey = request.PayloadKey
            });
        }

        [HttpPost("thumb_byuniqueid")]
        public async Task<IActionResult> GetThumbnailStreamByUniqueId(GetThumbnailByUniqueIdRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var header = await this.LookupHeaderByUniqueId(request.ClientUniqueId, request.TargetDrive, cn);
            if (null == header)
            {
                return NotFound();
            }

            return await GetThumbnail(new GetThumbnailRequest()
            {
                File = new ExternalFileIdentifier()
                {
                    FileId = header.FileId,
                    TargetDrive = request.TargetDrive
                },
                Width = request.Width,
                Height = request.Height,
                PayloadKey = request.PayloadKey
            });
        }

        private async Task<SharedSecretEncryptedFileHeader> LookupFileHeaderByGlobalTransitId(GlobalTransitIdFileIdentifier file, DatabaseConnection cn)
        {
            var driveId = WebOdinContext.PermissionsContext.GetDriveId(new TargetDrive()
            {
                Alias = file.TargetDrive.Alias,
                Type = file.TargetDrive.Type
            });

            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;

            WebOdinContext.PermissionsContext.AssertCanReadDrive(driveId);
            var result = await queryService.GetFileByGlobalTransitId(driveId, file.GlobalTransitId, WebOdinContext, excludePreviewThumbnail: false, cn: cn);
            return result;
        }

        private async Task<SharedSecretEncryptedFileHeader> LookupHeaderByUniqueId(Guid clientUniqueId, TargetDrive targetDrive, DatabaseConnection cn)
        {
            var driveId = WebOdinContext.PermissionsContext.GetDriveId(targetDrive);
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;
            var result = await queryService.GetFileByClientUniqueId(driveId, clientUniqueId, WebOdinContext, excludePreviewThumbnail: false, cn: cn);
            return result;
        }

        private PeerDriveQueryService GetPerimeterQueryService()
        {
            var fileSystem = GetHttpFileSystemResolver().ResolveFileSystem();
            return new PeerDriveQueryService(driveManager, fileSystem);
        }
    }
}