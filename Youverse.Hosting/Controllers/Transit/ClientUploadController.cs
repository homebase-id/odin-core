using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Controllers.Transit
{
    [ApiController]
    [Route("/api/transit/client")]
    public class ClientUploadController : ControllerBase
    {
        private readonly ITransitService _svc;
        private readonly IMultipartParcelBuilder _parcelBuilder;

        public ClientUploadController(IMultipartParcelBuilder parcelBuilder, ITransitService svc)
        {
            _parcelBuilder = parcelBuilder;
            _svc = svc;
        }

        [HttpPost("sendparcel")]
        public async Task<IActionResult> SendParcel()
        {
            try
            {
                if (!IsMultipartContentType(HttpContext.Request.ContentType))
                {
                    throw new InvalidDataException("Data is not multi-part content");
                }

                var boundary = GetBoundary(HttpContext.Request.ContentType);
                var reader = new MultipartReader(boundary, HttpContext.Request.Body);
                var section = await reader.ReadNextSectionAsync();

                //Note: the _parcelBuilder exists so we have a service that holds
                //the logic and routing of tenant-specific data.  We don't
                //want that in the http controllers
                
                var packageId = await _parcelBuilder.CreateParcel();
                bool isComplete = false;
                while (section != null || !isComplete)
                {
                    var name = GetSectionName(section.ContentDisposition);
                    isComplete = await _parcelBuilder.AddItem(packageId, name, section.Body);
                    section = await reader.ReadNextSectionAsync();
                }

                if (!isComplete)
                {
                    throw new InvalidDataException("Upload does not contain all required parts.");
                }
                
                //TODO: need to decide if some other mechanism starts the data transfer for queued items
                var parcel = await _parcelBuilder.GetParcel(packageId);
                var result = _svc.Send(parcel);

                return new JsonResult(result);
            }
            catch (InvalidDataException e)
            {
                return new JsonResult(new NoResultResponse(false, e.Message));
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