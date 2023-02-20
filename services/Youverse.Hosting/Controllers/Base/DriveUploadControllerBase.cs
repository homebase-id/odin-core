using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Controllers.Base
{
    /// <summary>
    /// Base API Controller for uploading multi-part streams
    /// </summary>
    public abstract class DriveUploadControllerBase : YouverseControllerBase
    {
        /// <summary>
        /// Uploads a file
        /// </summary>
        protected async Task<UploadResult> ReceiveStream()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new YouverseClientException("Data is not multi-part content", YouverseClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var driveUploadService = this.GetFileSystemResolver().ResolveFileSystemWriter();

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);
            var packageId = await driveUploadService.CreatePackage(section!.Body);

            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Metadata);
            await driveUploadService.AddMetadata(packageId, section!.Body);

            //
            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Payload);
            await driveUploadService.AddPayload(packageId, section!.Body);

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                AssertIsValidThumbnailPart(section, MultipartUploadParts.Thumbnail, out var fileSection, out var width, out var height);
                await driveUploadService.AddThumbnail(packageId, width, height, fileSection.Section.ContentType, fileSection.FileStream);
                section = await reader.ReadNextSectionAsync();
            }

            var status = await driveUploadService.FinalizeUpload(packageId);
            return status;
        }

        private void AssertIsPart(MultipartSection section, MultipartUploadParts expectedPart)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new YouverseClientException($"Part must be {Enum.GetName(expectedPart)}", YouverseClientErrorCode.MissingUploadData);
            }
        }

        private void AssertIsValidThumbnailPart(MultipartSection section, MultipartUploadParts expectedPart, out FileMultipartSection fileSection, out int width, out int height)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new YouverseClientException($"Thumbnails have name of {Enum.GetName(expectedPart)}", YouverseClientErrorCode.InvalidThumnbnailName);
            }

            fileSection = section.AsFileSection();
            if (null == fileSection)
            {
                throw new YouverseClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')", YouverseClientErrorCode.InvalidThumnbnailName);
            }

            string[] parts = fileSection.FileName.Split('x');
            if (!Int32.TryParse(parts[0], out width) || !Int32.TryParse(parts[1], out height))
            {
                throw new YouverseClientException("Thumbnails must include a filename formatted as 'WidthXHeight' (i.e. '400x200')", YouverseClientErrorCode.InvalidThumnbnailName);
            }
        }

        private static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetBoundary(string contentType)
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