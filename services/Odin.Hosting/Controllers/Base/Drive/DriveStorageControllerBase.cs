using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Odin.Core.Exceptions;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.SendingHost;

namespace Odin.Hosting.Controllers.Base.Drive
{
    /// <summary>
    /// Base class for any endpoint reading drive storage
    /// </summary>
    public abstract class DriveStorageControllerBase : OdinControllerBase
    {
        private readonly ILogger _logger;
        private readonly ITransitService _transitService;
        private readonly FileSystemResolver _fileSystemResolver;

        protected DriveStorageControllerBase(
            ILogger logger,
            FileSystemResolver fileSystemResolver,
            ITransitService transitService
        )
        {
            _fileSystemResolver = fileSystemResolver;
            _transitService = transitService;
            _logger = logger;
        }

        /// <summary>
        /// Returns the file header
        /// </summary>
        protected async Task<IActionResult> GetFileHeader(ExternalFileIdentifier request)
        {
            var result = await this.GetFileSystemResolver().ResolveFileSystem().Storage.GetSharedSecretEncryptedHeader(MapToInternalFile(request));

            if (result == null)
            {
                return NotFound();
            }

            // No caching on header
            // AddGuestApiCacheHeader();

            return new JsonResult(result);
        }

        /// <summary>
        /// Returns the payload for a given file
        /// </summary>
        protected async Task<IActionResult> GetPayloadStream(GetPayloadRequest request)
        {
            DriveFileUtility.AssertValidPayloadKey(request.Key);
            
            var file = MapToInternalFile(request.File);
            var fs = this.GetFileSystemResolver().ResolveFileSystem();

            var payloadStream = await fs.Storage.GetPayloadStream(file, request.Key, request.Chunk);
            if (payloadStream == null)
            {
                return NotFound();
            }

            var header = await fs.Storage.GetSharedSecretEncryptedHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata.IsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadKey, payloadStream.Key);
            HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(payloadStream.LastModified);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, payloadStream.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader64);

            if (null != request.Chunk)
            {
                var payloadSize = header.FileMetadata.Payloads.SingleOrDefault(p => p.Key == request.Key)?.BytesWritten ??
                                  throw new OdinSystemException("Invalid payload key");

                var to = request.Chunk.Start + request.Chunk.Length - 1;
                HttpContext.Response.Headers.Add("Content-Range",
                    new ContentRangeHeaderValue(request.Chunk.Start, Math.Min(to, payloadSize), payloadSize)
                        .ToString());
            }

            AddGuestApiCacheHeader();

            var result = new FileStreamResult(payloadStream.Stream, payloadStream.ContentType);

            return result;
        }

        /// <summary>
        /// Returns the thumbnail matching the width and height.  Note: you should get the content type from the file header
        /// </summary>
        protected async Task<IActionResult> GetThumbnail(GetThumbnailRequest request)
        {
            DriveFileUtility.AssertValidPayloadKey(request.PayloadKey);
            
            var file = MapToInternalFile(request.File);
            var fs = this.GetFileSystemResolver().ResolveFileSystem();
            
            var header = await fs.Storage.GetSharedSecretEncryptedHeader(file);
            if (header == null)
            {
                return NotFound();
            }

            var payloadDescriptor = header.FileMetadata.GetPayloadDescriptor(request.PayloadKey);
            if (null == payloadDescriptor)
            {
                return NotFound();
            }
            
            var (thumbPayload, thumbHeader) = await fs.Storage.GetThumbnailPayloadStream(file,
                request.Width, request.Height, request.PayloadKey, request.DirectMatchOnly);
            
            if (thumbPayload == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata!.IsEncrypted.ToString());
            HttpContext.Response.Headers.LastModified = payloadDescriptor.GetLastModifiedHttpHeaderValue();
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, thumbHeader.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, header.SharedSecretEncryptedKeyHeader.ToBase64());

            AddGuestApiCacheHeader();

            var result = new FileStreamResult(thumbPayload, header.FileMetadata.IsEncrypted
                ? "application/octet-stream"
                : thumbHeader.ContentType);

            return result;
        }

        /// <summary>
        /// Deletes a file and sends delete linked file requests to all recipient if specified
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        protected async Task<IActionResult> DeleteFile(DeleteFileRequest request)
        {
            var driveId = OdinContext.PermissionsContext.GetDriveId(request.File.TargetDrive);
            var requestRecipients = request.Recipients;

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            var result = new DeleteLinkedFileResult()
            {
                RecipientStatus = new Dictionary<string, DeleteLinkedFileStatus>(),
                LocalFileDeleted = false
            };

            var fs = _fileSystemResolver.ResolveFileSystem(file);

            var header = await fs.Storage.GetServerFileHeader(file);
            if (header == null)
            {
                result.LocalFileNotFound = true;
                return new JsonResult(result);
            }

            var recipients = requestRecipients ?? new List<string>();
            if (recipients.Any())
            {
                if (header.FileMetadata.GlobalTransitId.HasValue)
                {
                    var remoteGlobalTransitIdentifier = new GlobalTransitIdFileIdentifier()
                    {
                        GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                        TargetDrive = request.File.TargetDrive
                    };

                    //send the deleted file
                    var map = await _transitService.SendDeleteLinkedFileRequest(remoteGlobalTransitIdentifier,
                        new SendFileOptions()
                        {
                            FileSystemType = header.ServerMetadata.FileSystemType,
                            TransferFileType = TransferFileType.Normal
                        },
                        recipients);

                    foreach (var (key, value) in map)
                    {
                        switch (value)
                        {
                            case TransitResponseCode.AcceptedIntoInbox:
                                result.RecipientStatus.Add(key, DeleteLinkedFileStatus.RequestAccepted);
                                break;

                            case TransitResponseCode.Rejected:
                            case TransitResponseCode.QuarantinedPayload:
                            case TransitResponseCode.QuarantinedSenderNotConnected:
                                result.RecipientStatus.Add(key, DeleteLinkedFileStatus.RequestRejected);
                                break;

                            default:
                                throw new OdinSystemException($"Unknown TransitResponseCode {value}");
                        }
                    }
                }
            }

            await fs.Storage.SoftDeleteLongTermFile(file);
            result.LocalFileDeleted = true;

            if (result.LocalFileNotFound)
            {
                return NotFound();
            }

            return new JsonResult(result);
        }
        
        protected async Task<DeletePayloadResult> DeletePayload(DeletePayloadRequest request)
        {
            DriveFileUtility.AssertValidPayloadKey(request?.Key);
            
            var file = MapToInternalFile(request.File);
            var fs = this.GetFileSystemResolver().ResolveFileSystem();

            return new DeletePayloadResult()
            {
                NewVersionTag = await fs.Storage.DeletePayload(file, request.Key)
            };
        }
    }
}