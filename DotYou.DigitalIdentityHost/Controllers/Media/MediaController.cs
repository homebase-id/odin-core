using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.MediaService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Media
{
    public class ValueFile
    {
        public IEnumerable<IFormFile> Files { get; set; }
    }

    [ApiController]
    [Route("api/media")]
    //[Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class MediaController : Controller
    {
        private readonly IMediaService _mediaService;

        public MediaController(IMediaService mediaService)
        {
            _mediaService = mediaService;
        }

        [HttpPost("images")]
        public async Task<Guid> SaveImage([FromForm(Name = "file")] IFormFile file)
        {
            // var file = files.Files.FirstOrDefault();
            Console.WriteLine("Save image Http Post called");
            if (file == null)
            {
                return Guid.Empty;
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
            return id;
        }

        [HttpGet("images/{id}")]
        public async Task<HttpResponseMessage> GetImage(Guid id)
        {
            var result = await _mediaService.GetImage(id);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            MemoryStream ms = new MemoryStream(result.Bytes);
            response.Content = new StreamContent(ms);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(result.MimeType);
            this.Response.ContentLength = result.Bytes.Length;
            return response;
        }
    }
}