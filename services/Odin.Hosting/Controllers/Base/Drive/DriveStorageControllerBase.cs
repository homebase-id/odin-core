using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Odin.Core.Exceptions;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Authentication.YouAuth;

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

            AddGuestApiCacheHeader();

            return new JsonResult(result);
        }

        /// <summary>
        /// Returns the payload for a given file
        /// </summary>
        protected async Task<IActionResult> GetPayloadStream(GetPayloadRequest request)
        {
            var file = MapToInternalFile(request.File);

            var fs = this.GetFileSystemResolver().ResolveFileSystem();

            var payload = await fs.Storage.GetPayloadStream(file, request.Key, request.Chunk);
            if (payload == Stream.Null)
            {
                return NotFound();
            }

            var header = await fs.Storage.GetSharedSecretEncryptedHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata.PayloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, header.FileMetadata.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader64);
            if (null != request.Chunk)
            {
                var to = request.Chunk.Start + request.Chunk.Length - 1;
                HttpContext.Response.Headers.Add("Content-Range",
                    new ContentRangeHeaderValue(request.Chunk.Start, Math.Min(to, header.FileMetadata.PayloadSize), header.FileMetadata.PayloadSize)
                        .ToString());
            }

            AddGuestApiCacheHeader();

            var result = new FileStreamResult(payload, header.FileMetadata.PayloadIsEncrypted
                ? "application/octet-stream"
                : header.FileMetadata.ContentType);

            return result;
        }

        /// <summary>
        /// Returns the thumbnail matching the width and height.  Note: you should get the content type from the file header
        /// </summary>
        protected async Task<IActionResult> GetThumbnail(GetThumbnailRequest request)
        {
            var file = MapToInternalFile(request.File);

            var fs = this.GetFileSystemResolver().ResolveFileSystem();

            var (thumbPayload, thumbHeader) =
                await fs.Storage.GetThumbnailPayloadStream(file, request.Width, request.Height,
                    request.DirectMatchOnly);
            if (thumbPayload == Stream.Null)
            {
                return NotFound();
            }

            var header = await fs.Storage.GetSharedSecretEncryptedHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            if (header == null)
            {
                //TODO: need to throw a better exception when we have a thumbnail but no header
                throw new OdinClientException("Missing header", OdinClientErrorCode.UnknownId);
            }

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted,
                header.FileMetadata!.PayloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, thumbHeader.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64,
                encryptedKeyHeader64);

            AddGuestApiCacheHeader();

            var result = new FileStreamResult(thumbPayload, header.FileMetadata.PayloadIsEncrypted
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

        protected async Task<DeleteThumbnailResult> DeleteThumbnail(DeleteThumbnailRequest request)
        {
            var file = MapToInternalFile(request.File);

            var fs = this.GetFileSystemResolver().ResolveFileSystem();
            return new DeleteThumbnailResult()
            {
                NewVersionTag = await fs.Storage.DeleteThumbnail(file, request.Width, request.Height)
            };
        }

        protected async Task<DeletePayloadResult> DeletePayload(DeletePayloadRequest request)
        {
            var file = MapToInternalFile(request.File);
            var fs = this.GetFileSystemResolver().ResolveFileSystem();

            return new DeletePayloadResult()
            {
                NewVersionTag = await fs.Storage.DeletePayload(file, request.Key)
            };
        }
    }
}