using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using Youverse.Core.Services.Transit;
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
        private Guid _stateItemId;

        public TransitPerimeterController(ITransitPerimeterService perimeterService)
        {
            _perimeterService = perimeterService;
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
            //Note: the app id is validated in the Transit Authentication handler (aka certificate auth handler)

            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new HostToHostTransferException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);
            var section = await reader.ReadNextSectionAsync();

            this._stateItemId = await ProcessTransferKeyHeader(section);

            // 

            await ProcessMetadataSection(await reader.ReadNextSectionAsync());

            //

            await ProcessPayloadSection(await reader.ReadNextSectionAsync());

            if (!await _perimeterService.IsFileValid(_stateItemId))
            {
                throw new HostToHostTransferException("Transfer does not contain all required parts.");
            }

            var result = await _perimeterService.FinalizeTransfer(this._stateItemId);
            if (result == FilterAction.Reject)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }

            return new JsonResult(result);
        }

        private async Task<Guid> ProcessTransferKeyHeader(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.TransferKeyHeader);

            string json = await new StreamReader(section.Body).ReadToEndAsync();
            var transferKeyHeader = JsonConvert.DeserializeObject<RsaEncryptedRecipientTransferKeyHeader>(json);

            var transferStateItemId = await _perimeterService.InitializeIncomingTransfer(transferKeyHeader);
            return transferStateItemId;
        }

        private async Task ProcessMetadataSection(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.Metadata);

            //TODO: determine if the filter needs to decide if its result should be sent back to the sender
            var response = await _perimeterService.ApplyFirstStageFiltering(this._stateItemId, MultipartHostTransferParts.Metadata, section.Body);
            if (response.FilterAction == FilterAction.Reject)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }
        }

        private async Task ProcessPayloadSection(MultipartSection section)
        {
            AssertIsPart(section, MultipartHostTransferParts.Payload);

            //TODO: determine if the filter needs to decide if its result should be sent back to the sender
            var response = await _perimeterService.ApplyFirstStageFiltering(this._stateItemId, MultipartHostTransferParts.Payload, section.Body);
            if (response.FilterAction == FilterAction.Reject)
            {
                HttpContext.Abort(); //TODO:does this abort also kill the response?
                throw new HostToHostTransferException("Transmission Aborted");
            }
        }

        private void AssertIsPart(MultipartSection section, MultipartHostTransferParts expectedPart)
        {
            if (!Enum.TryParse<MultipartHostTransferParts>(GetSectionName(section!.ContentDisposition), true, out var part) || part != expectedPart)
            {
                throw new HostToHostTransferException($"Part must be {Enum.GetName(expectedPart)}");
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