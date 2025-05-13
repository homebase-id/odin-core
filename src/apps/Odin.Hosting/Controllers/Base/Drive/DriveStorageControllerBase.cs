using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;
using Odin.Hosting.ApiExceptions.Client;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Controllers.Base.Drive
{
    /// <summary>
    /// Base class for any endpoint reading drive storage
    /// </summary>
    public abstract class DriveStorageControllerBase(
        // ILogger logger,
        PeerOutgoingTransferService peerOutgoingTransferService) : OdinControllerBase
    {
        // private readonly ILogger _logger = logger;

        /// <summary>
        /// Returns the file header
        /// </summary>
        protected async Task<IActionResult> GetFileHeader(ExternalFileIdentifier request)
        {
            var result = await this.GetHttpFileSystemResolver().ResolveFileSystem().Storage
                .GetSharedSecretEncryptedHeader(MapToInternalFile(request), WebOdinContext);

            if (result == null)
            {
                return NotFound();
            }

            // No caching on header
            // AddGuestApiCacheHeader();

            return new JsonResult(result);
        }

        /// <summary>
        /// Returns the file header
        /// </summary>
        protected async Task<IActionResult> GetFileHeaderByGlobalTransitId(GlobalTransitIdFileIdentifier request)
        {
            var driveId = WebOdinContext.PermissionsContext.GetDriveId(new TargetDrive()
            {
                Alias = request.TargetDrive.Alias,
                Type = request.TargetDrive.Type
            });

            WebOdinContext.PermissionsContext.AssertCanReadDrive(driveId);

            var queryService = GetHttpFileSystemResolver().ResolveFileSystem().Query;
            var fileIdResult = await queryService.ResolveFileId(request, WebOdinContext);

            if (fileIdResult == null)
            {
                return NotFound();
            }

            var result = await this.GetHttpFileSystemResolver().ResolveFileSystem().Storage
                .GetSharedSecretEncryptedHeader((InternalDriveFileId)fileIdResult, WebOdinContext);

            if (result == null)
            {
                return NotFound();
            }

            // No caching on header
            // AddGuestApiCacheHeader();

            return new JsonResult(result);
        }

        protected async Task<FileTransferHistoryResponse> GetFileTransferHistory(ExternalFileIdentifier file)
        {
            var storage = GetHttpFileSystemResolver().ResolveFileSystem().Storage;
            var (count, history) = await storage.GetTransferHistory(this.MapToInternalFile(file), WebOdinContext);
            if (history == null)
            {
                return null;
            }

            return new FileTransferHistoryResponse()
            {
                OriginalRecipientCount = count,
                History = history
            };
        }

        /// <summary>
        /// Returns the payload for a given file
        /// </summary>
        protected async Task<IActionResult> GetPayloadStream(GetPayloadRequest request)
        {
            TenantPathManager.AssertValidPayloadKey(request.Key);

            var file = MapToInternalFile(request.File);
            var fs = GetHttpFileSystemResolver().ResolveFileSystem();

            var (header, payloadDescriptor, encryptedKeyHeader, fileExists) =
                await fs.Storage.GetPayloadSharedSecretEncryptedKeyHeaderAsync(file, request.Key, WebOdinContext);

            if (!fileExists)
            {
                return NotFound();
            }

            var payloadStream = await fs.Storage.GetPayloadStreamAsync(file, request.Key, request.Chunk, WebOdinContext);
            if (payloadStream == null)
            {
                return NotFound();
            }

            // Indicates the payload is missing
            if (payloadStream.Stream == Stream.Null)
            {
                return StatusCode((int)HttpStatusCode.Gone);
            }

            HttpContext.Response.Headers.Append(HttpHeaderConstants.AcceptRanges, "bytes");
            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata.IsEncrypted.ToString());
            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadKey, payloadStream.Key);
            HttpContext.Response.Headers.LastModified = payloadDescriptor.GetLastModifiedHttpHeaderValue();
            HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, payloadStream.ContentType);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader.ToBase64());

            if (null != request.Chunk)
            {
                var payloadSize = header.FileMetadata.Payloads.SingleOrDefault(p => p.KeyEquals(request.Key))?.BytesWritten ??
                                  throw new OdinSystemException("Invalid payload key");

                var to = request.Chunk.Length == int.MaxValue ? payloadSize - 1 : request.Chunk.Start + request.Chunk.Length - 1;

                // Sanity
                if (to >= payloadSize)
                {
                    throw new RequestedRangeNotSatisfiableException($"{to} >= {payloadSize}");
                }

                HttpContext.Response.Headers.Append("Content-Range",
                    new ContentRangeHeaderValue(request.Chunk.Start, to, payloadSize)
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
            TenantPathManager.AssertValidPayloadKey(request.PayloadKey);

            var file = MapToInternalFile(request.File);
            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();

            var (header, payloadDescriptor, encryptedKeyHeaderForPayload, fileExists) =
                await fs.Storage.GetPayloadSharedSecretEncryptedKeyHeaderAsync(file, request.PayloadKey, WebOdinContext);

            if (!fileExists)
            {
                return NotFound();
            }

            //Note: this second read of the payload could be going to network storage

            var (thumbPayload, thumbHeader) = await fs.Storage.GetThumbnailPayloadStreamAsync(file,
                request.Width, request.Height, request.PayloadKey, payloadDescriptor.Uid, WebOdinContext, request.DirectMatchOnly);

            if (thumbPayload == Stream.Null)
            {
                return NotFound();
            }

            HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata!.IsEncrypted.ToString());
            HttpContext.Response.Headers.LastModified = payloadDescriptor.GetLastModifiedHttpHeaderValue();
            HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, thumbHeader.ContentType);
            HttpContext.Response.Headers.Append(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeaderForPayload.ToBase64());

            AddGuestApiCacheHeader();

            var result = new FileStreamResult(thumbPayload, header.FileMetadata.IsEncrypted
                ? "application/octet-stream"
                : thumbHeader.ContentType);

            return result;
        }

        protected async Task<SendReadReceiptResult> SendReadReceipt(SendReadReceiptRequest request)
        {
            if (null == request?.Files)
            {
                throw new OdinClientException("Files not specified");
            }

            var internalFiles = request.Files.Select(MapToInternalFile).ToList();
            return await peerOutgoingTransferService.SendReadReceipt(internalFiles, WebOdinContext,
                this.GetHttpFileSystemResolver().GetFileSystemType());
        }

        /// <summary>
        /// Deletes a file and sends delete linked file requests to all recipient if specified
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        protected async Task<IActionResult> DeleteFile(DeleteFileRequest request)
        {
            var result = await PerformFileDelete(request);
            return new JsonResult(result);
        }

        protected async Task<IActionResult> DeleteFilesByGroupIdBatch(DeleteFilesByGroupIdBatchRequest batchRequest)
        {
            var deleteBatchFinalResult = new DeleteFilesByGroupIdBatchResult()
            {
                Results = new List<DeleteFileByGroupIdResult>()
            };

            //Firstly resolve all drives to ensure we have access to do a complete deletion
            foreach (var request in batchRequest.Requests)
            {
                var driveId = WebOdinContext.PermissionsContext.GetDriveId(request.TargetDrive);
                WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            }

            foreach (var request in batchRequest.Requests)
            {
                var groupIdToBeDeleted = request.GroupId;

                var qp = new FileQueryParams()
                {
                    TargetDrive = request.TargetDrive,
                    GroupId = new List<Guid>() { groupIdToBeDeleted }
                };

                var options = new QueryBatchResultOptions()
                {
                    IncludeHeaderContent = false,
                    MaxRecords = int.MaxValue
                };

                var driveId = WebOdinContext.PermissionsContext.GetDriveId(request.TargetDrive);

                var queryResults = await GetHttpFileSystemResolver().ResolveFileSystem()
                    .Query.GetBatch(driveId, qp, options, WebOdinContext);

                //
                // Delete the batch resulting from the query
                //
                var deleteBatch = new DeleteFileIdBatchRequest()
                {
                    Requests = queryResults.SearchResults.Select(sr => new DeleteFileRequest()
                    {
                        File = new ExternalFileIdentifier()
                        {
                            FileId = sr.FileId,
                            TargetDrive = request.TargetDrive
                        },
                        Recipients = request.Recipients
                    }).ToList()
                };

                var batchResults = await PerformDeleteFileIdBatch(deleteBatch);

                deleteBatchFinalResult.Results.Add(new DeleteFileByGroupIdResult()
                {
                    GroupId = groupIdToBeDeleted,
                    DeleteFileResults = batchResults.Results
                });
            }

            return new JsonResult(deleteBatchFinalResult);
        }

        protected async Task<IActionResult> DeleteFileIdBatch(DeleteFileIdBatchRequest batchRequest)
        {
            //Firstly resolve all drives to ensure we have access to do a complete deletion
            foreach (var request in batchRequest.Requests)
            {
                var driveId = WebOdinContext.PermissionsContext.GetDriveId(request.File.TargetDrive);
                WebOdinContext.PermissionsContext.AssertCanWriteToDrive(driveId);
            }

            var batchResult = await PerformDeleteFileIdBatch(batchRequest);
            return new JsonResult(batchResult);
        }

        protected async Task<DeletePayloadResult> DeletePayload(DeletePayloadRequest request)
        {
            if (null == request)
            {
                throw new OdinClientException("Invalid delete payload request");
            }

            TenantPathManager.AssertValidPayloadKey(request.Key);
            if (request.VersionTag == null)
            {
                throw new OdinClientException("Missing version tag", OdinClientErrorCode.MissingVersionTag);
            }

            var file = MapToInternalFile(request.File);
            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();

            return new DeletePayloadResult()
            {
                NewVersionTag = await fs.Storage.DeletePayload(file, request.Key, request.VersionTag.GetValueOrDefault(), WebOdinContext)
            };
        }

        protected async Task<IActionResult> HardDeleteFile([FromBody] DeleteFileRequest request)
        {
            var driveId = WebOdinContext.PermissionsContext.GetDriveId(request.File.TargetDrive);

            if (request.Recipients != null && request.Recipients.Any())
            {
                throw new OdinClientException("Cannot specify recipients when hard-deleting a file", OdinClientErrorCode.InvalidRecipient);
            }

            var file = new InternalDriveFileId()
            {
                DriveId = driveId,
                FileId = request.File.FileId
            };

            await GetHttpFileSystemResolver().ResolveFileSystem().Storage.HardDeleteLongTermFile(file, WebOdinContext);
            return Ok();
        }

        private async Task<DeleteFileIdBatchResult> PerformDeleteFileIdBatch(DeleteFileIdBatchRequest batchRequest)
        {
            var results = new List<DeleteFileResult>();
            foreach (var request in batchRequest.Requests)
            {
                var r = await PerformFileDelete(request);
                results.Add(r);
            }

            var batchResult = new DeleteFileIdBatchResult()
            {
                Results = results
            };

            return batchResult;
        }

        private async Task<DeleteFileResult> PerformFileDelete(DeleteFileRequest request)
        {
            var driveId = WebOdinContext.PermissionsContext.GetDriveId(request.File.TargetDrive);
            var requestRecipients = request.Recipients;

            OdinValidationUtils.AssertValidRecipientList(request.Recipients, allowEmpty: true);

            var file = new InternalDriveFileId()
            {
                FileId = request.File.FileId,
                DriveId = driveId
            };

            var result = new DeleteFileResult()
            {
                File = request.File,
                RecipientStatus = new Dictionary<string, DeleteLinkedFileStatus>(),
                LocalFileDeleted = false
            };

            //TODO: consider - this requires the caller for delete to have read access to get the file
            // when in-fact the caller only needs write access to delete a file.

            // var patchedContext = OdinContextUpgrades.UpgradeForFileDelete(WebOdinContext, file.DriveId);
            // var fs = await fileSystemResolver.ResolveFileSystem(file, WebOdinContext);
            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();
            var header = await fs.Storage.GetServerFileHeaderForWriting(file, WebOdinContext);
            if (header == null)
            {
                result.LocalFileNotFound = true;
                return result;
            }

            var recipients = requestRecipients ?? new List<string>();
            if (recipients.Any())
            {
                //send the deleted file
                var responses = await peerOutgoingTransferService.SendDeleteFileRequest(file,
                    new FileTransferOptions()
                    {
                        FileSystemType = header.ServerMetadata.FileSystemType,
                        TransferFileType = TransferFileType.Normal
                    },
                    recipients, WebOdinContext);

                result.RecipientStatus = responses;
            }

            await fs.Storage.SoftDeleteLongTermFile(file, WebOdinContext, null);
            result.LocalFileDeleted = true;
            return result;
        }
    }
}