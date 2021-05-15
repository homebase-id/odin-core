using System;
using System.Threading.Tasks;
using DotYou.Types;
using DotYou.Types.Messaging;

namespace DotYou.Kernel.Services.Messaging.Email
{
    public interface IMessagingService 
    {

        /// <summary>
        /// Returns a message by its Id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<Message> Get(Guid id);


        Task<PagedResult<Message>> GetList(PageOptions page);

        Task Delete(Guid id);

        /// <summary>
        /// Saves a message to the specified recipient's message store.
        /// </summary>
        /// <param name="message"></param>
        Task SaveMessage(Message message);


        /// <summary>
        /// Sends the specified message to all recipients
        /// </summary>
        /// <param name="message"></param>
        Task SendMessage(Message message);

    }
}
