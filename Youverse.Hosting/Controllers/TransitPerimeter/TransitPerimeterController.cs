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
using Youverse.Hosting.Authentication.TransitPerimeter;

namespace Youverse.Hosting.Controllers.TransitPerimeter
{
    /// <summary>
    /// Receives incoming data transfers from other hosts
    /// </summary>
    [ApiController]
    [Route("/api/perimeter/transit/host")]
    [Authorize(Policy = TransitPerimeterPolicies.MustBeIdentifiedPolicyName, AuthenticationSchemes = TransitPerimeterAuthConstants.TransitAuthScheme)]
    public class TransitPerimeterController : ControllerBase
    {
        private readonly ITransitPerimeterService _perimeterService;
        private readonly ITransitQuarantineService _quarantineService;

        public TransitPerimeterController(ITransitPerimeterService perimeterService, ITransitQuarantineService quarantineService)
        {
            _perimeterService = perimeterService;
            _quarantineService = quarantineService;
        }

        [HttpGet("tpk")]
        public async Task<JsonResult> GetTransitPublicKey()
        {
            var key = await _perimeterService.GetTransitPublicKey();
            return new JsonResult(key);
        }

        [HttpPost("stream")]
        public async Task<IActionResult> AcceptHostToHostTransfer()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new InvalidDataException("Data is not multi-part content");
            }

            //TODO: support for validating the app id is specified and this host has authorized the app

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);
            var section = await reader.ReadNextSectionAsync();

            var expectedFilePart = GetNextExpectedFilePart(null);
            var trackerId = await _perimeterService.CreateFileTracker();

            while (section != null)
            {
                var name = GetSectionName(section.ContentDisposition);
                var part = GetFilePart(name);

                if (null == expectedFilePart)
                {
                    throw new InvalidDataException("Multipart - provided part is not expected");
                }

                if (part != expectedFilePart)
                {
                    throw new InvalidDataException("Multipart order is invalid.  It must be 1) Header, 2) Metadata, 3) Payload");
                }

                //TODO: determine if the filter needs to decide if its result should be sent back to the sender
                var response = await _perimeterService.ApplyFirstStageFilterToPart(trackerId, part, section.Body);
                if (response.FilterAction == FilterAction.Reject)
                {
                    HttpContext.Abort(); //TODO:does this abort also kill the response?
                    throw new InvalidDataException("Transmission Aborted");
                }

                section = await reader.ReadNextSectionAsync();
                expectedFilePart = GetNextExpectedFilePart(part);
            }

            if (!_perimeterService.IsFileValid(trackerId))
            {
                throw new InvalidDataException("Upload does not contain all required parts.");
            }

            var result = await _perimeterService.FinalizeTransfer(trackerId);
            if (result.Code == FinalFilterAction.Rejected)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new InvalidDataException("Transmission Aborted");
            }

            return new JsonResult(result);
        }

        private FilePart? GetNextExpectedFilePart(FilePart? part)
        {
            if (!part.HasValue)
            {
                return FilePart.Header;
            }

            switch (part.GetValueOrDefault())
            {
                case FilePart.Header:
                    return FilePart.Metadata;
                case FilePart.Metadata:
                    return FilePart.Payload;
                case FilePart.Payload:
                    return null;
                default:
                    throw new ArgumentOutOfRangeException();
            }
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