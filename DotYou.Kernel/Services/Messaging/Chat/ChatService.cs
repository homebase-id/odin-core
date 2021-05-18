using System.Linq;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Contacts;
using DotYou.Types;
using DotYou.Types.Messaging;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Messaging.Chat
{
    public class ChatService : DotYouServiceBase, IChatService
    {
        private const string CHAT_MESSAGE_STORAGE = "ChatMessages";
        private readonly IContactService _contactService;

        public ChatService(DotYouContext context, ILogger<ChatService> logger, IHubContext<NotificationHub, INotificationHub> hub, DotYouHttpClientFactory fac, IContactService contactService) : base(context, logger, hub, fac)
        {
            _contactService = contactService;
        }

        public async Task<PagedResult<Contact>> GetAvailableContacts()
        {
            var page = await _contactService.GetContacts(PageOptions.Default);
            
            //TODO: filter to those only with a DotYouId (this should be in the get contacts function)
            //TODO: ping the contacts DI servers to see if they are online (also need to ache this ping for some time)
            
            return page;
        }

        public async Task<bool> SendMessage(ChatMessageEnvelope message)
        {
            //look up recipient's public key from contacts
            var contact = await _contactService.GetByDotYouId(message.Recipient);

            if (null == contact || ValidationUtil.HasNonWhitespaceValue(contact.PublicKeyCertificate))
            {
                throw new MessageSendException($"Cannot find public key certificate for {message.Recipient}");
            }

            message.Body = Cryptography.Encrypt.UsingPublicKey(contact.PublicKeyCertificate, message.Body);

            var client = this.CreateOutgoingHttpClient(message.Recipient);

            var response = await client.SendChatMessage(message);

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ReceiveIncomingMessage(ChatMessageEnvelope message)
        {
        
            WithTenantStorage<ChatMessageEnvelope>(CHAT_MESSAGE_STORAGE, s=>s.Save(message));

            await this.Notify.NewChatMessageReceived(message);

            return true;
        }
    }
}