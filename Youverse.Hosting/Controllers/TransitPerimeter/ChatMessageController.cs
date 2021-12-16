using System;
using System.IO;
using System.Threading.Tasks;
using BrunoZell.ModelBinding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Hosting.Authentication.TransitPerimeter;
using Youverse.Services.Messaging;
using Youverse.Services.Messaging.Chat;

namespace Youverse.Hosting.Controllers.TransitPerimeter
{
    [ApiController]
    [Route("api/perimeter")]
    [Authorize(Policy = TransitPerimeterPolicies.MustBeIdentifiedPolicyName, AuthenticationSchemes = TransitPerimeterAuthConstants.TransitAuthScheme)]
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
            Stream stream = Stream.Null;
            if (media?.Length > 0)
            {
                stream = media.OpenReadStream();
            }

            await _chatService.ReceiveMessage(envelope, metaData, stream);

            return new JsonResult(new NoResultResponse(true));
        }
    }
}