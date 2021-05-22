using System.Net.Security;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Messaging.Email;
using DotYou.Types;
using DotYou.Types.Admin;
using DotYou.Types.Messaging;

namespace DotYou.Kernel.Services.Messaging.Chat
{
    public interface IChatService
    {
        
        /// <summary>
        /// Returns a list of contacts available for chat
        /// </summary>
        /// <remarks>Contacts are available when the following are true:
        ///     1) Their Digital Identity Host returns an 'online' status
        ///     2) They are marked as connected
        ///     3) They have not been blocked
        /// </remarks>
        /// <returns></returns>
        public Task<PagedResult<AvailabilityStatus>> GetAvailableContacts(PageOptions options);

        /// <summary>
        /// Sends the specific chat message
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public Task<bool> SendMessage(ChatMessageEnvelope message);

        /// <summary>
        /// Accepts an incoming chat message from another Digital Identity
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public Task<bool> ReceiveIncomingMessage(ChatMessageEnvelope message);
    }
}