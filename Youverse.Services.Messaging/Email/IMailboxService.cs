using System;
using System.Threading.Tasks;
using DotYou.Types;
using DotYou.Types.Messaging;
using Youverse.Core;

namespace DotYou.Kernel.Services.Messaging.Email
{
    /// <summary>
    /// Methods for managing a message folder (Sent items, drafts, inbox, etc)
    /// </summary>
    public interface IMailboxService
    {
        /// <summary>
        /// Returns a message by its Id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<Message> Get(Guid id);

        Task<PagedResult<Message>> GetList(string folder, PageOptions page);

        Task Delete(Guid id);

        /// <summary>
        /// Saves a message to the specified recipient's message store.
        /// </summary>
        /// <param name="message"></param>
        Task Save(Message message);

        Task<PagedResult<Message>> Find(string text);
        
        string FolderName { get; }
    }
}