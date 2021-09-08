using DotYou.Kernel.Services.MediaService;
using DotYou.Types.Messaging;

namespace DotYou.Kernel.HttpClient
{
    public class PerimeterChatMessageEnvelope : ChatMessageEnvelope
    {
        public MediaData ImageData { get; set; }
    }
}