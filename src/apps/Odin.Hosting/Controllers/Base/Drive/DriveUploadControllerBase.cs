using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;

namespace Odin.Hosting.Controllers.Base.Drive
{
    /// <summary>
    /// Base API Controller for uploading multi-part streams
    /// </summary>
    public abstract class DriveUploadControllerBase(ILogger logger) : OdinControllerBase
    {
        /// <summary>
        /// Receives a stream for a new file being uploaded or existing file being overwritten
        /// </summary>
        protected async Task<UploadResult> ReceiveFileStream()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var driveUploadService = this.GetHttpFileSystemResolver().ResolveFileSystemWriter();

            try
            {
                return await ProcessUpload(reader, driveUploadService);
            }
            catch
            {
                await driveUploadService.CleanupTempFiles(WebOdinContext);
                throw;
            }
        }

        private async Task<UploadResult> ProcessUpload(MultipartReader reader, FileSystemStreamWriterBase driveUploadService)
        {
            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);
            try
            {
                logger.LogDebug("ReceiveFileStream: StartUpload");
                await driveUploadService.StartUpload(section!.Body, WebOdinContext);
            }
            catch (JsonException e)
            {
                throw new OdinClientException($"JSON error: {e.Message}", e);
            }

            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Metadata);
            logger.LogDebug("ReceiveFileStream: AddMetadata");
            await driveUploadService.AddMetadata(section!.Body);

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsPayloadPart(section))
                {
                    AssertIsPayloadPart(section, out var fileSection, out var payloadKey, out var contentTypeFromMultiPartSection);
                    logger.LogDebug("ReceiveFileStream: AddPayload");
                    await driveUploadService.AddPayload(payloadKey, contentTypeFromMultiPartSection, fileSection.FileStream,
                        WebOdinContext);
                }

                if (IsThumbnail(section))
                {
                    AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey,
                        out var contentTypeFromMultiPartSection);
                    logger.LogDebug("ReceiveFileStream: AddThumbnail");
                    await driveUploadService.AddThumbnail(thumbnailUploadKey, contentTypeFromMultiPartSection, fileSection.FileStream,
                        WebOdinContext);
                }

                section = await reader.ReadNextSectionAsync();
            }

            logger.LogDebug("ReceiveFileStream: FinalizeUploadAsync");
            var status = await driveUploadService.FinalizeUploadAsync(WebOdinContext);
            return status;
        }

        /// <summary>
        /// Receives the stream for a new thumbnail being added to an existing file
        /// </summary>
        protected async Task<UploadPayloadResult> ReceivePayloadStream()
        {
            logger.LogWarning("files/uploadpayload endpoint used.  auth-context: {authContext}", WebOdinContext.AuthContext);
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var writer = this.GetHttpFileSystemResolver().ResolvePayloadStreamWriter();

            try
            {
                var section = await reader.ReadNextSectionAsync();
                AssertIsPart(section, MultipartUploadParts.PayloadUploadInstructions);

                await writer.StartUpload(section!.Body, WebOdinContext);

                //
                section = await reader.ReadNextSectionAsync();
                while (null != section)
                {
                    if (IsPayloadPart(section))
                    {
                        AssertIsPayloadPart(section, out var fileSection, out var payloadKey, out var contentType);
                        await writer.AddPayload(payloadKey, contentType, fileSection.FileStream, WebOdinContext);
                    }

                    if (IsThumbnail(section))
                    {
                        AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out var contentType);
                        await writer.AddThumbnail(thumbnailUploadKey, contentType, fileSection.FileStream, WebOdinContext);
                    }

                    section = await reader.ReadNextSectionAsync();
                }

                var status = await writer.FinalizeUpload(WebOdinContext);
                return status;
            }
            catch
            {
                try
                {
                    await writer.CleanupTempFiles(WebOdinContext);
                }
                catch(Exception e) 
                {
                    // Console.WriteLine("HERE--->> {0}", e.Message);
                    logger.LogError(e, " HERE -->");
                }
                throw;
            }
        }

        private protected void AssertIsPart(MultipartSection section, MultipartUploadParts expectedPart)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) ||
                part != expectedPart)
            {
                throw new OdinClientException($"Part must be {Enum.GetName(expectedPart)}", OdinClientErrorCode.MissingUploadData);
            }
        }

        private protected bool IsMetadataPart(MultipartSection section)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part))
            {
                throw new OdinClientException("Section does not match a known MultipartSection", OdinClientErrorCode.InvalidUpload);
            }

            return part == MultipartUploadParts.Metadata;
        }

        private protected bool IsPayloadPart(MultipartSection section)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part))
            {
                throw new OdinClientException("Section does not match a known MultipartSection", OdinClientErrorCode.InvalidUpload);
            }

            return part == MultipartUploadParts.Payload;
        }

        private protected bool IsThumbnail(MultipartSection section)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part))
            {
                throw new OdinClientException("Section does not match a known MultipartSection", OdinClientErrorCode.InvalidUpload);
            }

            return part == MultipartUploadParts.Thumbnail;
        }

        private protected void AssertIsPayloadPart(MultipartSection section, out FileMultipartSection fileSection,
            out string payloadKey, out string contentTypeFromMultiPartSection)
        {
            var expectedPart = MultipartUploadParts.Payload;
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) ||
                part != expectedPart)
            {
                throw new OdinClientException($"Payloads have name of {Enum.GetName(expectedPart)}",
                    OdinClientErrorCode.InvalidPayloadNameOrKey);
            }

            fileSection = section.AsFileSection();
            contentTypeFromMultiPartSection = section.ContentType;

            TenantPathManager.AssertValidPayloadKey(fileSection?.FileName);
            payloadKey = fileSection?.FileName;
        }

        private protected void AssertIsValidThumbnailPart(MultipartSection section, out FileMultipartSection fileSection,
            out string thumbnailUploadKey, out string contentTypeFromMultiPartSection)
        {
            var expectedPart = MultipartUploadParts.Thumbnail;
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) ||
                part != expectedPart)
            {
                throw new OdinClientException($"Thumbnails have name of {Enum.GetName(expectedPart)}",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }

            fileSection = section.AsFileSection();
            if (null == fileSection)
            {
                throw new OdinClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }

            contentTypeFromMultiPartSection = section.ContentType;

            thumbnailUploadKey = fileSection?.FileName;
            if (string.IsNullOrEmpty(thumbnailUploadKey) || string.IsNullOrWhiteSpace(thumbnailUploadKey))
            {
                throw new OdinClientException(
                    "Thumbnails must include the thumbnailKey, which matches the key in the InstructionSet.UploadManifest.",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }
        }

        private protected static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private protected static string GetBoundary(string contentType)
        {
            var elements = contentType.Split(' ');
            var element = elements.First(entry => entry.StartsWith("boundary="));
            var boundary = element.Substring("boundary=".Length);
            // Remove quotes
            if (boundary.Length >= 2 && boundary[0] == '"' &&
                boundary[^1] == '"')
            {
                boundary = boundary.Substring(1, boundary.Length - 2);
            }

            return boundary;
        }

        private string GetSectionName(string contentDisposition)
        {
            var cd = ContentDispositionHeaderValue.Parse(contentDisposition);
            return cd.Name?.Trim('"');
        }
    }
}