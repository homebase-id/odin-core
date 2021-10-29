using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core;
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
        public async Task<DataStreamResponse> AcceptDataStream()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new InvalidDataException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);
            var section = await reader.ReadNextSectionAsync();

            var fileId = await _perimeterService.StartIncomingFile();

            while (section != null)
            {
                var name = GetSectionName(section.ContentDisposition);
                var part = GetFilePart(name);

                var handlerResponse = await _perimeterService.AddPart(fileId, part, section.Body);
                //if (handlerResponse == FilterResult.ShouldAbortDangerousPayload)
                // {
                //     //TODO:does this abort also kill the response?
                //     HttpContext.Abort();
                //     return new DataStreamResponse()
                //     {
                //         Success = AcceptDataStreamReason.AbortedDangerousPayload
                //     };
                // }

                section = await reader.ReadNextSectionAsync();
            }

            if (!_perimeterService.IsFileComplete(fileId))
            {
                throw new InvalidDataException("Upload does not contain all required parts.");
            }

            return new DataStreamResponse()
            {
                Success = AcceptDataStreamReason.Success
            };
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

    /// <summary>
    /// Response used to describe the result of transferring a data stream between two hosts.
    /// </summary>
    public class DataStreamResponse
    {
        /// <summary>
        /// Specifies if the transfer was successfully received
        /// </summary>
        public AcceptDataStreamReason Success { get; set; }

        /// <summary>
        /// Additional details on the reason the data was quarantined
        /// </summary>
        public string QuarantinedReason { get; set; }
    }

    public enum AcceptDataStreamReason
    {
        Success = 1,
        QuarantinedPayload = 2,
        AbortedDangerousPayload = 2,
    }
}