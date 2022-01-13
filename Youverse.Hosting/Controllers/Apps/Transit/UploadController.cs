using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.App;

namespace Youverse.Hosting.Controllers.Apps.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.TransitV1)]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.SchemeName)]
    public class UploadController : ControllerBase
    {
        private readonly ITransitService _transitService;
        private readonly IMultipartPackageStorageWriter _packageStorageWriter;
        private readonly DotYouContext _context;

        public UploadController(IMultipartPackageStorageWriter packageStorageWriter, ITransitService transitService, DotYouContext context)
        {
            _packageStorageWriter = packageStorageWriter;
            _transitService = transitService;
            _context = context;
        }

        // [AliasAs("instructions")] StreamPart instructionSet,
        // [AliasAs("metaData")] StreamPart metaData,
        // [AliasAs("payload")] StreamPart payload);
        [HttpPost("upload")]
        public async Task<IActionResult> Upload()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new UploadException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync();
            if (!Enum.TryParse<MultipartUploadParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != MultipartUploadParts.Instructions)
            {
                throw new UploadException($"First part must be {Enum.GetName(MultipartUploadParts.Instructions)}");
            }

            var packageId = await _packageStorageWriter.CreatePackage(section.Body);

            bool isComplete = false;
            section = await reader.ReadNextSectionAsync();
            while (section != null || !isComplete)
            {
                var partName = GetSectionName(section!.ContentDisposition);
                var partStream = section.Body;
                isComplete = await _packageStorageWriter.AddPart(packageId, partName, partStream);
                section = await reader.ReadNextSectionAsync();
            }

            if (!isComplete)
            {
                throw new UploadException("Upload does not contain all required parts.");
            }

            var package = await _packageStorageWriter.GetPackage(packageId);
            var status = await _transitService.AcceptUpload(package);
            return new JsonResult(status);
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