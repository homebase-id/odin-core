using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Storage;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Hosting.Controllers.Perimeter
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/host")]
    [Authorize(Policy = DotYouPolicyNames.MustBeIdentified)]
    public class TransitPerimeterController : ControllerBase
    {
        private readonly ITransitPerimeterService _perimeterService;
        private readonly ITransitQuarantineService _quarantineService;

        public TransitPerimeterController(ITransitPerimeterService perimeterService, ITransitQuarantineService quarantineService)
        {
            _perimeterService = perimeterService;
            _quarantineService = quarantineService;
        }

        [HttpPost("stream")]
        public async Task<IActionResult> AcceptHostToHostTransfer()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new InvalidDataException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);
            var section = await reader.ReadNextSectionAsync();

            var trackerId = await _perimeterService.CreateFileTracker();

            while (section != null)
            {
                var name = GetSectionName(section.ContentDisposition);
                var part = GetFilePart(name);

                var response = await _perimeterService.FilterFilePart(trackerId, part, section.Body);
                if (response.FilterAction == FilterAction.Reject)
                {
                    HttpContext.Abort(); //TODO:does this abort also kill the response?
                    throw new InvalidDataException("Transmission Aborted");
                }

                section = await reader.ReadNextSectionAsync();
            }

            if (!_perimeterService.IsFileValid(trackerId))
            {
                throw new InvalidDataException("Upload does not contain all required parts.");
            }

            var result = await _perimeterService.GetFinalFilterResult(trackerId);

            return new JsonResult(result);
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

        private FilePart GetFilePart(string name)
        {
            if (Enum.TryParse<FilePart>(name, true, out var part) == false)
            {
                throw new InvalidDataException("Unknown file part");
            }

            return part;
        }

        private string GetSectionName(string contentDisposition)
        {
            var cd = ContentDispositionHeaderValue.Parse(contentDisposition);
            return cd.Name?.Trim('"');
        }

        private string GetFileName(string contentDisposition)
        {
            var cd = ContentDispositionHeaderValue.Parse(contentDisposition);
            var filename = cd.FileName?.Trim('"');

            if (null != filename)
            {
                return filename;
            }

            return GetSectionName(contentDisposition);
        }
    }
}