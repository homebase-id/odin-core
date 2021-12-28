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
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Apps.Transit
{
    [ApiController]
    [Route("/api/transit/client")]
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

        /// <summary>
        /// Accepts a multipart upload.  The 'name' parameter in the upload must be specified.  The following parts are required:
        ///
        /// name: "recipients": an encrypted list of recipients in json format. { recipients:["recipient1", "recipient2"] } 
        /// name: "metadata": an encrypted object of metadata information in json format (fields/format is TBD as of oct 27, 2021)
        /// name: "payload": the encrypted payload of data
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidDataException"></exception>
        [HttpPost("SendPackage")]
        public async Task<IActionResult> SendPackage()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new InvalidDataException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            //NOTE: the first section MUST BE the app id so we can validate it
            var section = await reader.ReadNextSectionAsync();

            //Note: the _packageStorageWriter exists so we have a service that holds
            //the logic and routing of tenant-specific data.  We don't
            //want that in the http controllers

            //TODO: determine which is the right drive to use
            var driveId = _context.AppContext.DriveId;
            var packageId = await _packageStorageWriter.CreatePackage(driveId.GetValueOrDefault());
            bool isComplete = false;
            while (section != null || !isComplete)
            {
                var partName = GetSectionName(section.ContentDisposition);
                var partStream = section.Body;
                isComplete = await _packageStorageWriter.AddPart(packageId, partName, partStream);
                section = await reader.ReadNextSectionAsync();
            }

            if (!isComplete)
            {
                throw new InvalidDataException("Upload does not contain all required parts.");
            }

            var package = await _packageStorageWriter.GetPackage(packageId);
            var status = await _transitService.PrepareTransfer(package);

            return new JsonResult(status);
        }

        private static bool IsMultipartContentType(string contentType)
        {
            return
                !string.IsNullOrEmpty(contentType) &&
                contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetBoundary(string contentType)
        {
            var elements = contentType.Split(' ');
            var element = elements.First(entry => entry.StartsWith("boundary="));
            var boundary = element.Substring("boundary=".Length);
            // Remove quotes
            if (boundary.Length >= 2 && boundary[0] == '"' &&
                boundary[boundary.Length - 1] == '"')
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