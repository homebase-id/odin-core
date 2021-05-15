using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Messaging.Email
{
    public class MessagingService : DotYouServiceBase, IMessagingService
    {
        const string MESSAGE_COLLECTION = "messages";

        public MessagingService(DotYouContext context, ILogger<MessagingService> logger) : base(context, logger, null) { }
        
        public Task SaveMessage(Message message)
        {
            WithTenantStorage<Message>(MESSAGE_COLLECTION, storage => storage.Save(message));

            return Task.CompletedTask;
        }

        public async Task<Message> Get(Guid id)
        {
            return await WithTenantStorageReturnSingle<Message>(MESSAGE_COLLECTION, storage => storage.Get(id));
        }

        public Task<PagedResult<Message>> GetList(PageOptions page)
        {
            return WithTenantStorageReturnList<Message>(MESSAGE_COLLECTION, storage => storage.GetList(page));
        }

        public Task Delete(Guid id)
        {
            WithTenantStorage<Message>(MESSAGE_COLLECTION, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public Task SendMessage(Message message)
        {
            //TODO: you have to divide the recipients into those wth YF identities and those without.
            var client = new HttpClient();

            foreach (var recipient in message.Recipients)
            {
                var b = new UriBuilder();
                b.Path = "api/incoming/messages";
                b.Host = recipient;
                b.Scheme = "https";

                //Note: the casting is required to ensure the fields are sent. (I don't
                // know why, perhaps it's something to do with the json formatting)
                client.PostAsJsonAsync<Message>(b.Uri, (Message)message);
            }

            return Task.CompletedTask;
        }

    }
}