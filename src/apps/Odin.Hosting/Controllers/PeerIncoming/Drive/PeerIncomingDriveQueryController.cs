using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Query;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Controllers.PeerIncoming.Drive
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.DriveV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCapiAuthScheme)]
    [ApiExplorerSettings(GroupName = "peer-v1")]
    public class PeerIncomingDriveQueryController(IDriveManager driveManager) : OdinControllerBase
    {
        [HttpPost("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection(QueryBatchCollectionRequest request)
        {
            var perimeterService = GetPerimeterService();

            return await perimeterService.QueryBatchCollection(request, WebOdinContext);
        }

        [HttpPost("querymodified")]
        public async Task<QueryModifiedResponse> QueryModified(QueryModifiedRequest request)
        {
            var perimeterService = GetPerimeterService();

            var result = await perimeterService.QueryModified(request.QueryParams, request.ResultOptions, WebOdinContext);
            return QueryModifiedResponse.FromResult(result);
        }

        [HttpPost("querybatch")]
        public async Task<QueryBatchResponse> QueryBatch(QueryBatchRequest request)
        {
            var perimeterService = GetPerimeterService();
            var options = request.ResultOptionsRequest ?? QueryBatchResultOptionsRequest.Default;

            var batch = await perimeterService.QueryBatch(request.QueryParams, options.ToQueryBatchResultOptions(), WebOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }

        /// <summary>
        /// Retrieves a file's header and metadata
        /// </summary>
        [HttpPost("header")]
        public async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            var perimeterService = GetPerimeterService();

            SharedSecretEncryptedFileHeader result = await perimeterService.GetFileHeader(request.TargetDrive, request.FileId,
                WebOdinContext);

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
            var perimeterService = GetPerimeterService();
            var (encryptedKeyHeader64, isEncrypted, _, payloadStream) = await perimeterService.GetPayloadStreamAsync(
                request.File.TargetDrive,
                request.File.FileId,
                request.Key,
                request.Chunk,
                WebOdinContext);

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
            HttpContext.Response.Headers.ContentLength = payloadStream.Stream.Length;

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
            var perimeterService = GetPerimeterService();


            var (encryptedKeyHeader64, isEncrypted, _, decryptedContentType, lastModified, thumb) =
                await perimeterService.GetThumbnailAsync(request.File.TargetDrive, request.File.FileId, request.Height, request.Width,
                    request.PayloadKey,
                    WebOdinContext);

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
            var perimeterService = GetPerimeterService();

            var drives = await perimeterService.GetDrivesAsync(request.DriveType, WebOdinContext);
            return drives;
        }
        
        /// <summary>
        /// Reports whether a file with the given UniqueId (or, on the sibling endpoint,
        /// GlobalTransitId) exists on this peer, and — when the caller is entitled to it —
        /// the file's current VersionTag.
        /// </summary>
        /// <remarks>
        /// Purpose: "heal-a-group" flows. The author of a file fanned out across connected
        /// peers needs a cheap way to detect peers whose copy is missing or out of date so
        /// it can resend. Without this, the only signal is uploading and observing whether
        /// the peer rejects the write — wasteful and noisy.
        ///
        /// Response is always 200 with a <see cref="FileExistsOnPeerResponse"/> body when
        /// the caller is authorized; "file is not here" is expressed as <c>Exists = false</c>,
        /// not as 404. Lack of any drive permission throws <see cref="OdinSecurityException"/>.
        ///
        /// The body is tiered by what the caller can already see through other channels:
        ///   • Caller has Read on the drive — returns { Exists, VersionTag }. A Read caller
        ///     can already fetch the full header (and the VersionTag with it) via the other
        ///     peer-query endpoints, so withholding it here would only force a follow-up
        ///     call to the same answer.
        ///   • Caller has only Write on the drive, and is the file's OriginalAuthor —
        ///     returns { Exists, VersionTag }. The author is the only party allowed to
        ///     overwrite a file by UniqueId, and needs the tag to decide between
        ///     "do nothing" and "resend".
        ///   • Caller has only Write on the drive, and is NOT the OriginalAuthor —
        ///     returns { Exists, VersionTag = null }. Write-only peers have no normal path
        ///     to a header, so for them the VersionTag really is new information; without
        ///     overwrite rights they don't need it and we don't surface it.
        ///
        /// Why we don't also hide existence in the last case: a caller with drive access can
        /// already determine that a UniqueId / GlobalTransitId is taken via other paths
        /// (drive listings, or attempting an upload and observing the collision rejection).
        /// Suppressing the existence bit here would only force a wasted round trip on the
        /// way to the same answer.
        ///
        /// VersionTag is deliberately not a request parameter: the server returns the
        /// authoritative value (when the caller is entitled to it) and the caller does
        /// the comparison.
        /// </remarks>
        [HttpPost("file-exists_byuniqueid")]
        public async Task<FileExistsOnPeerResponse> GetFileExistsByUniqueId([FromBody] RemoteFileExistsByUniqueIdRequest request)
        {
            var (hasRead, _) = AssertDriveAccessForFileExists(request.DriveId);
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;
            var header = await queryService.GetServerFileHeaderByClientUniqueIdSkippingFileAcl(
                request.DriveId, request.UniqueId, WebOdinContext);
            return BuildFileExistsResponse(header, hasRead);
        }

        /// <inheritdoc cref="GetFileExistsByUniqueId"/>
        [HttpPost("file-exists_byglobaltransitid")]
        public async Task<FileExistsOnPeerResponse> GetFileExistsByGlobalTransitId(
            [FromBody] RemoteFileExistsByGlobalTransitIdRequest request)
        {
            var (hasRead, _) = AssertDriveAccessForFileExists(request.DriveId);
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;
            var header = await queryService.GetServerFileHeaderByGlobalTransitIdSkippingFileAcl(
                request.DriveId, request.GlobalTransitId, WebOdinContext);
            return BuildFileExistsResponse(header, hasRead);
        }

        private (bool hasRead, bool hasWrite) AssertDriveAccessForFileExists(Guid driveId)
        {
            var hasRead = WebOdinContext.PermissionsContext.HasDrivePermission(driveId, DrivePermission.Read);
            var hasWrite = WebOdinContext.PermissionsContext.HasDrivePermission(driveId, DrivePermission.Write);
            if (!hasRead && !hasWrite)
            {
                throw new OdinSecurityException("no access to drive");
            }

            return (hasRead, hasWrite);
        }

        private FileExistsOnPeerResponse BuildFileExistsResponse(ServerFileHeader header, bool hasRead)
        {
            if (header == null)
            {
                return new FileExistsOnPeerResponse { Exists = false, VersionTag = null };
            }

            var caller = WebOdinContext.GetCallerOdinIdOrFail();
            var isAuthor = header.FileMetadata.OriginalAuthor.HasValue
                           && header.FileMetadata.OriginalAuthor.Value == caller;

            var entitledToTag = hasRead || isAuthor;
            return new FileExistsOnPeerResponse
            {
                Exists = true,
                VersionTag = entitledToTag ? header.FileMetadata.VersionTag : null,
            };
        }

        ///
        [HttpPost("header_byglobaltransitid")]
        public async Task<IActionResult> GetFileHeaderByGlobalTransitId([FromBody] GlobalTransitIdFileIdentifier file)
        {
            var result = await LookupFileHeaderByGlobalTransitId(file);
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpPost("header_byuniqueid")]
        public async Task<IActionResult> GetFileHeaderByUniqueId([FromBody] GetFileHeaderByUniqueIdRequest request)
        {
            var result = await LookupHeaderByUniqueId(request.UniqueId, request.TargetDrive);
            if (result == null)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }

        [HttpPost("payload_byglobaltransitid")]
        public async Task<IActionResult> GetPayloadStreamByGlobalTransitId([FromBody] GetPayloadByGlobalTransitIdRequest request)
        {
            var header = await this.LookupFileHeaderByGlobalTransitId(request.File);
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
            var header = await this.LookupHeaderByUniqueId(request.UniqueId, request.TargetDrive);
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
            var header = await this.LookupFileHeaderByGlobalTransitId(request.File);
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
            var header = await this.LookupHeaderByUniqueId(request.ClientUniqueId, request.TargetDrive);
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

        private async Task<SharedSecretEncryptedFileHeader> LookupFileHeaderByGlobalTransitId(GlobalTransitIdFileIdentifier file)
        {
            var driveId = file.TargetDrive.Alias;
            WebOdinContext.PermissionsContext.AssertCanReadDrive(driveId);
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;
            var result = await queryService.GetFileByGlobalTransitId(driveId, file.GlobalTransitId, WebOdinContext,
                excludePreviewThumbnail: false);
            return result;
        }

        private async Task<SharedSecretEncryptedFileHeader> LookupHeaderByUniqueId(Guid clientUniqueId, TargetDrive targetDrive)
        {
            var driveId = targetDrive.Alias;
            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;

            var options = new ResultOptions()
            {
                MaxRecords = 1,
                IncludeHeaderContent = true,
                ExcludePreviewThumbnail = false
            };

            var result = await queryService.GetFileByClientUniqueId(driveId, clientUniqueId, options, WebOdinContext);

            return result;
        }

        private Odin.Services.Peer.Incoming.Drive.Query.PeerDriveQueryService GetPerimeterService()
        {
            var fileSystem = GetHttpFileSystemResolver().ResolveFileSystem();
            return new Odin.Services.Peer.Incoming.Drive.Query.PeerDriveQueryService(driveManager, fileSystem);
        }
    }
}