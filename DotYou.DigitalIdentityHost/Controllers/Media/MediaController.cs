using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.MediaService;
using DotYou.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

//Multi-part streaming code from: https://stackoverflow.com/questions/36437282/dealing-with-large-file-uploads-on-asp-net-core-1-0

namespace DotYou.DigitalIdentityHost.Controllers.Media
{
    [ApiController]
    [Route("api/media")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class MediaController : Controller
    {
        private readonly IMediaService _mediaService;

        public MediaController(IMediaService mediaService)
        {
            _mediaService = mediaService;
        }

        [HttpPost("images")]
        public async Task<IActionResult> SaveMedia([FromForm(Name = "file")] IFormFile file)
        {
            if (file == null)
            {
                return new JsonResult(new { id = Guid.Empty });
            }

            Console.WriteLine("Save image called");
            
            Guid id = Guid.NewGuid();
            MemoryStream stream = new MemoryStream();
            await file.CopyToAsync(stream);
            var bytes = stream.ToArray();
            
            var mediaData = new MediaData()
            {
                Id = id,
                MimeType = file.ContentType,
                Bytes = bytes
            };

            await _mediaService.SaveImage(mediaData);
            // Console.WriteLine($"{bytes.Length} uploaded");

            return new JsonResult(new { id = id });
        }

        [HttpGet("images/{id}")]
        public async Task<IActionResult> GetImage(Guid id)
        {
            var result = await _mediaService.GetImage(id);

            if (null == result || result.Bytes.Length == 0)
            {
                return NotFound(id);
            }

            return File(new MemoryStream(result.Bytes), result.MimeType);
        }

        [HttpPost("mediastream")]
        public async Task<IActionResult> AcceptMediaStream()
        {
           
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

                using (var stream = new FileStream(fileName, FileMode.Append))
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

        private string GetFileName(string contentDisposition)
        {
            return contentDisposition
                .Split(';')
                .SingleOrDefault(part => part.Contains("filename"))
                ?.Split('=')
                .Last()
                .Trim('"');
        }
    }
}