using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;
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
                throw new HostToHostTransferException("Data is not multi-part content");
            }

            //Note: the app id is validated in the Transit Authentication handler (aka certificate auth handler)

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);
            var section = await reader.ReadNextSectionAsync();
            
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != MultipartHostTransferParts.TransferKeyHeader)
            {
                throw new HostToHostTransferException($"First part must be {Enum.GetName(MultipartHostTransferParts.TransferKeyHeader)}");
            }

            string json = await new StreamReader(section.Body).ReadToEndAsync();
            var recipientKeyHeader = JsonConvert.DeserializeObject<EncryptedRecipientTransferKeyHeader>(json);
            
            var trackerId = await _perimeterService.CreateFileTracker(recipientKeyHeader);
            
            //
            
            section = await reader.ReadNextSectionAsync();
            var expectedPart = GetNextExpectedFilePart(part);
            while (section != null)
            {
                var name = GetSectionName(section.ContentDisposition);
                part = GetFilePart(name);

                if (null == expectedPart)
                {
                    throw new HostToHostTransferException("Multipart - provided part is not expected");
                }

                if (part != expectedPart)
                {
                    throw new HostToHostTransferException("Multipart order is invalid.  It must be 1) Header, 2) Metadata, 3) Payload");
                }
                
                //TODO: determine if the filter needs to decide if its result should be sent back to the sender
                var response = await _perimeterService.ApplyFirstStageFilter(trackerId, part, section.Body);
                if (response.FilterAction == FilterAction.Reject)
                {
                    HttpContext.Abort(); //TODO:does this abort also kill the response?
                    throw new HostToHostTransferException("Transmission Aborted");
                }

                section = await reader.ReadNextSectionAsync();
                expectedPart = GetNextExpectedFilePart(part);
            }

            if (!_perimeterService.IsFileValid(trackerId))
            {
                throw new HostToHostTransferException("Transfer does not contain all required parts.");
            }

            var result = await _perimeterService.FinalizeTransfer(trackerId);
            if (result.Code == FinalFilterAction.Rejected)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }

            return new JsonResult(result);
        }

        private MultipartHostTransferParts? GetNextExpectedFilePart(MultipartHostTransferParts? part)
        {
            if (!part.HasValue)
            {
                return MultipartHostTransferParts.TransferKeyHeader;
            }

            switch (part.GetValueOrDefault())
            {
                case MultipartHostTransferParts.TransferKeyHeader:
                    return MultipartHostTransferParts.Metadata;
                case MultipartHostTransferParts.Metadata:
                    return MultipartHostTransferParts.Payload;
                case MultipartHostTransferParts.Payload:
                    return null; //nothing after payload
                default:
                    throw new HostToHostTransferException("Unknown MultipartHostTransferParts");
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

        private MultipartHostTransferParts GetFilePart(string name)
        {
            if (Enum.TryParse<MultipartHostTransferParts>(name, true, out var part) == false)
            {
                throw new HostToHostTransferException("Unknown MultipartHostTransferPart");
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