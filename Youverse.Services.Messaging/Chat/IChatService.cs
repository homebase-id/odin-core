using System;
using System.IO;
using System.Threading.Tasks;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.MediaService;
using DotYou.Types;
using DotYou.Types.Admin;
using DotYou.Types.Messaging;
using Youverse.Core;
using Youverse.Core.Identity;

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
        /// <returns></returns>
        //public Task<bool> ReceiveMessage(ChatMessageEnvelope envelope, MediaData mediaData);
        public Task<bool> ReceiveMessage(ChatMessageEnvelope envelope, MediaMetaData metaData, Stream mediaStream);

        /// <summary>
        /// Returns a list of recent messages across all connected contacts
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        public Task<PagedResult<RecentChatMessageHeader>> GetRecentMessages(PageOptions pageOptions);

        /// <summary>
        /// Returns the chat history for a given <see cref="DotYouIdentity"/> based on the date range specified
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <param name="startDateTimeOffsetSeconds"></param>
        /// <param name="endDateTimeOffsetSeconds"></param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<DateRangePagedResult<ChatMessageEnvelope>> GetHistory(DotYouIdentity dotYouId, Int64 startDateTimeOffsetSeconds, Int64 endDateTimeOffsetSeconds, PageOptions pageOptions);
    }
}