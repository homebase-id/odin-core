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
        private readonly TransitService _svc;

        private readonly JsonSerializerOptions _jsonOptions;

        public ClientUploadController(TransitService svc)
        {
            _svc = svc;

            _jsonOptions = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            };
        }

        [HttpPost("upload")]
        public async Task<IActionResult> SendPayload()
        {
            try
            {
                var (recipients, spec) = await RouteDataToTenant();
                var result = _svc.StartDataTransfer(recipients, spec);

                //we need to tell the caller the result.  how many items were
                return new JsonResult(result);
            }
            catch (InvalidDataException e)
            {
                return new JsonResult(new NoResultResponse(false, e.Message));
            }
        }

        private async Task<(RecipientList recipients, TransferSpec spec)> RouteDataToTenant()
        {
            string root = $@"\tmp\dotyoutransit\tenants\{HttpContext.Request.Host.Host}\sent";
            Directory.CreateDirectory(root);

            var spec = new TransferSpec();
            spec.Id = Guid.NewGuid();
            spec.File = new TenantFile(root);

            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new InvalidDataException("Data is not multi-part content");
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);
            var section = await reader.ReadNextSectionAsync();
            RecipientList recipients = null;

            var sectionCount = 0;
            while (section != null)
            {
                var name = GetSectionName(section.ContentDisposition);

                if (string.Equals(name, SectionNames.Header, StringComparison.InvariantCultureIgnoreCase))
                {
                    var json = await section.ReadAsStringAsync();
                    spec.Header = JsonSerializer.Deserialize<KeyHeader>(json, _jsonOptions);

                    await System.IO.File.WriteAllTextAsync(spec.File.HeaderPath, json);
                    sectionCount++;
                }
                else if (string.Equals(name, SectionNames.Recipients, StringComparison.InvariantCultureIgnoreCase))
                {
                    //todo: convert to streaming for memory reduction if needed.
                    string json = await section.ReadAsStringAsync();
                    recipients = JsonSerializer.Deserialize<RecipientList>(json, _jsonOptions);
                    if (recipients?.Recipients?.Length <= 0)
                    {
                        throw new Exception("No recipients specified");
                    }

                    sectionCount++;
                }
                else if (string.Equals(name, SectionNames.Metadata, StringComparison.InvariantCultureIgnoreCase))
                {
                    await WriteFile(spec.File.MetaDataPath, section.Body);
                    sectionCount++;
                }
                else if (string.Equals(name, SectionNames.Payload, StringComparison.InvariantCultureIgnoreCase))
                {
                    //spec.PayloadContentType = section.ContentType;
                    await WriteFile(spec.File.DataFilePath, section.Body);
                    sectionCount++;
                }
                else
                {
                    throw new Exception($"Invalid FormPart included in stream: Name: [{name}]");
                }

                section = await reader.ReadNextSectionAsync();
            }

            if (sectionCount != 4)
            {
                throw new InvalidDataException("Invalid payload");
            }

            return (recipients, spec);
        }

        private async Task WriteFile(string filePath, Stream stream)
        {
            const int chunkSize = 1024;
            var buffer = new byte[chunkSize];
            var bytesRead = 0;

            await using var output = new FileStream(filePath, FileMode.Append);
            do
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);
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