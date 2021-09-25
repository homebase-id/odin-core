using System;
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

        [HttpPost]
        public async Task<IActionResult> SaveMedia([FromForm(Name = "file")] IFormFile file)
        {
            //Note: IFormFile references the part of the Body stream which holds the file content

            if (file == null)
            {
                return new JsonResult(new { id = Guid.Empty });
            }

            Console.WriteLine("Save media called - streaming edition");

            Guid id = Guid.NewGuid();

            var metaData = new MediaMetaData()
            {
                Id = id,
                MimeType = file.ContentType
            };

            var stream = file.OpenReadStream();
            await _mediaService.SaveMedia(metaData, stream);
            // Console.WriteLine($"{bytes.Length} uploaded");

            return new JsonResult(new { id = id });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> StreamMedia(Guid id)
        {
            Console.WriteLine($"Stream media called for id [{id}]");
            var result = await _mediaService.GetMetaData(id);
            if (null == result)
            {
                Console.WriteLine($"Meta data not found [{id}]");
                return NotFound(id);
            }

            Console.WriteLine($"Mimetype is [{result.MimeType}]");
            var stream = await _mediaService.GetMediaStream(id);

            if (stream == null || stream.Length == 0)
            {
                Console.WriteLine($"File not found[{id}]");
                return NotFound(id);
            }

            return File(stream, result.MimeType);
        }
    }
}