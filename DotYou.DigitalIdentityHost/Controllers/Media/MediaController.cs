using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.MediaService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
        public async Task<IActionResult> SaveImage([FromForm(Name = "file")] IFormFile file)
        {
            if (file == null)
            {
                return new JsonResult(new { imageId = Guid.Empty });
            }

            Console.WriteLine("Save image called");
            Guid id = Guid.NewGuid();

            MemoryStream stream = new MemoryStream();
            await file.CopyToAsync(stream);

            var metaData = new MediaMetaData()
            {
                Id = id,
                MimeType = file.ContentType,
            };

            var bytes = stream.ToArray();
            Console.WriteLine($"{bytes.Length} uploaded");

            await _mediaService.SaveImage(metaData, bytes);
            return new JsonResult(new { imageId = id });
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
    }
}