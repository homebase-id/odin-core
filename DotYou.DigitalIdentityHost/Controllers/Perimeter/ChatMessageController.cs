using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BrunoZell.ModelBinding;
using DotYou.Kernel.Services.MediaService;
using DotYou.Kernel.Services.Messaging.Chat;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter
{
    [ApiController]
    [Route("api/perimeter")]
    public class ChatMessageController : ControllerBase
    {
        private readonly IChatService _chatService;

        public ChatMessageController(IChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> ReceiveIncomingChatMessage(
            [ModelBinder(BinderType = typeof(JsonModelBinder))] [FromForm(Name = "envelope")] ChatMessageEnvelope envelope,
            [ModelBinder(BinderType = typeof(JsonModelBinder))] [FromForm(Name = "metaData")] MediaMetaData metaData,
            [FromForm(Name = "media")] IFormFile media)
        {
            Console.WriteLine($"ReceiveIncomingChatMessage perimeter ChatMessageController called.  bytes len:{media?.Length ?? 0}");
            MediaData mediaData = null;

            if (media?.Length > 0)
            {
                var stream = new MemoryStream();
                await media.CopyToAsync(stream);
                
                mediaData = new MediaData()
                {
                    Id = metaData.Id,
                    MimeType = metaData.MimeType,
                    Bytes = stream.ToArray()
                };
            }

            await _chatService.ReceiveIncomingMessage(envelope, mediaData);

            return new JsonResult(new NoResultResponse(true));
        }
    }
}