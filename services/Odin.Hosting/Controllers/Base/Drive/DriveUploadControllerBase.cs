using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Drives.FileSystem.Base.Upload.Attachments;

namespace Odin.Hosting.Controllers.Base.Drive
{
    /// <summary>
    /// Base API Controller for uploading multi-part streams
    /// </summary>
    public abstract class DriveUploadControllerBase : OdinControllerBase
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

            var driveUploadService = this.GetFileSystemResolver().ResolveFileSystemWriter();

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);
            try
            {
                await driveUploadService.StartUpload(section!.Body);
            }
            catch (JsonException e)
            {
                throw new OdinClientException($"JSON error: {e.Message}", e);
            }

            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Metadata);
            await driveUploadService.AddMetadata(section!.Body);

            //
            section = await reader.ReadNextSectionAsync();

            //backwards compat
            bool requirePayloadSection = driveUploadService.Package.InstructionSet.StorageOptions.StorageIntent == StorageIntent.NewFileOrOverwrite;
            if (section == null && requirePayloadSection)
            {
                throw new OdinClientException("Missing Payload section", OdinClientErrorCode.InvalidPayload);
            }

            if (null != section)
            {
                AssertIsPart(section, MultipartUploadParts.Payload);
                await driveUploadService.AddPayload(section!.Body);
            }

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                AssertIsValidThumbnailPart(section, out var fileSection, out var width, out var height);
                await driveUploadService.AddThumbnail(width, height, fileSection.Section.ContentType, fileSection.FileStream);
                section = await reader.ReadNextSectionAsync();
            }

            var status = await driveUploadService.FinalizeUpload();
            return status;
        }

        /// <summary>
        /// Receives the stream for a new thumbnail being added to an existing file
        /// </summary>
        protected async Task<UploadAttachmentsResult> ReceiveAttachmentStream()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var writer = this.GetFileSystemResolver().ResolveAttachmentStreamWriter();

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.ThumbnailInstructions);

            await writer.StartUpload(section!.Body);


            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                //TODO: parse payload or thumbnail
                AssertIsValidThumbnailPart(section, out var fileSection, out var width, out var height);
                await writer.AddThumbnail(width, height, fileSection.Section.ContentType, fileSection.FileStream);
                section = await reader.ReadNextSectionAsync();
            }

            //
            // section = await reader.ReadNextSectionAsync();
            // AssertIsPart(section, MultipartUploadParts.Payload);
            // await writer.AddPayload(section!.Body);

            var status = await writer.FinalizeUpload();
            return status;
        }

        private protected void AssertIsPart(MultipartSection section, MultipartUploadParts expectedPart)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new OdinClientException($"Part must be {Enum.GetName(expectedPart)}", OdinClientErrorCode.MissingUploadData);
            }
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


        private protected void AssertIsPayloadPart(MultipartSection section , out FileMultipartSection fileSection,
            out string payloadKey)
        {

            var expectedPart = MultipartUploadParts.Payload;
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new OdinClientException($"Payloads have name of {Enum.GetName(expectedPart)}", OdinClientErrorCode.InvalidPayloadName);
            }

            fileSection = section.AsFileSection();
            var filename = fileSection?.FileName;
            if (string.IsNullOrEmpty(filename) || string.IsNullOrWhiteSpace(filename))
            {
                throw new OdinClientException("Payloads must include filename with no spaces. i.e. ('image_data' is valid where as 'image data' is not)",
                    OdinClientErrorCode.InvalidPayload);
            }

            payloadKey = filename;
        }

        private protected void AssertIsValidThumbnailPart(MultipartSection section, out FileMultipartSection fileSection,
            out int width, out int height)
        {
            var expectedPart = MultipartUploadParts.Thumbnail;
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new OdinClientException($"Thumbnails have name of {Enum.GetName(expectedPart)}", OdinClientErrorCode.InvalidThumnbnailName);
            }

            fileSection = section.AsFileSection();
            if (null == fileSection)
            {
                throw new OdinClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')",
                    OdinClientErrorCode.InvalidThumnbnailName);
            }

            string[] parts = fileSection.FileName.Split('x');
            if (!Int32.TryParse(parts[0], out width) || !Int32.TryParse(parts[1], out height))
            {
                throw new OdinClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')",
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