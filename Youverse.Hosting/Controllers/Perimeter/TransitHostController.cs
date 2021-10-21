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

namespace Youverse.Hosting.Controllers.Perimeter
{
    [ApiController]
    [Route("/api/perimeter/transit/host")]
    public class TransitHostController : ControllerBase
    {
        private readonly TransitService _svc;

        private readonly JsonSerializerOptions _jsonOptions;

        public TransitHostController(TransitService svc)
        {
            _svc = svc;

            _jsonOptions = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            };
        }
        
        [HttpPost("stream")]
        public async Task<IActionResult> AcceptDataStream()
        {
            string root = $@"\tmp\dotyoutransit\tenants\{HttpContext.Request.Host.Host}\incoming";
            Directory.CreateDirectory(root);

            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                return new JsonResult(new NoResultResponse(false, "Must be Multipart content"));
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync();
            while (section != null)
            {
                // process each image
                const int chunkSize = 1024;
                var buffer = new byte[chunkSize];
                var bytesRead = 0;

                var fileName = GetFileName(section.ContentDisposition);

                await using (var stream = new FileStream(Path.Combine(root, fileName), FileMode.Append))
                {
                    do
                    {
                        bytesRead = await section.Body.ReadAsync(buffer, 0, buffer.Length);
                        stream.Write(buffer, 0, bytesRead);
                    } while (bytesRead > 0);
                }

                section = await reader.ReadNextSectionAsync();
            }

            return new JsonResult(new NoResultResponse(true));
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