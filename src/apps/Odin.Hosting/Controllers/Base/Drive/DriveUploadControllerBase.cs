﻿using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;

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

            var driveUploadService = this.GetHttpFileSystemResolver().ResolveFileSystemWriter();

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);
            try
            {
                await driveUploadService.StartUpload(section!.Body, WebOdinContext);
            }
            catch (JsonException e)
            {
                throw new OdinClientException($"JSON error: {e.Message}", e);
            }

            section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Metadata);
            await driveUploadService.AddMetadata(section!.Body, WebOdinContext);

            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsPayloadPart(section))
                {
                    AssertIsPayloadPart(section, out var fileSection, out var payloadKey, out var contentTypeFromMultiPartSection);
                    await driveUploadService.AddPayload(payloadKey, contentTypeFromMultiPartSection, fileSection.FileStream, WebOdinContext);
                }

                if (IsThumbnail(section))
                {
                    AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out var contentTypeFromMultiPartSection);
                    await driveUploadService.AddThumbnail(thumbnailUploadKey, contentTypeFromMultiPartSection, fileSection.FileStream, WebOdinContext);
                }

                section = await reader.ReadNextSectionAsync();
            }

            var status = await driveUploadService.FinalizeUploadAsync(WebOdinContext);
            return status;
        }
        

        private protected void AssertIsPart(MultipartSection section, MultipartUploadParts expectedPart)
        {
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
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
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new OdinClientException($"Payloads have name of {Enum.GetName(expectedPart)}", OdinClientErrorCode.InvalidPayloadNameOrKey);
            }

            fileSection = section.AsFileSection();
            contentTypeFromMultiPartSection = section.ContentType;

            DriveFileUtility.AssertValidPayloadKey(fileSection?.FileName);
            payloadKey = fileSection?.FileName;
        }

        private protected void AssertIsValidThumbnailPart(MultipartSection section, out FileMultipartSection fileSection,
            out string thumbnailUploadKey, out string contentTypeFromMultiPartSection)
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