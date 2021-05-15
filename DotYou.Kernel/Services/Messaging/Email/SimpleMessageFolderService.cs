using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Messaging.Email
{
    /// <summary>
    /// Stores messages in the given folder name using LiteDb.
    /// </summary>
    public class SimpleMessageFolderService : DotYouServiceBase, IMessageFolderService
    {
        private readonly string _folderName;

        public SimpleMessageFolderService(DotYouContext context, string folderName, ILogger<SimpleMessageFolderService> logger) : base(context, null, null)
        {
            _folderName = folderName;
        }
        
        public Task Save(Message message)
        {
            WithTenantStorage<Message>(FolderName, storage => storage.Save(message));

            return Task.CompletedTask;
        }

        public async Task<Message> Get(Guid id)
        {
            return await WithTenantStorageReturnSingle<Message>(FolderName, storage => storage.Get(id));
        }

        public Task<PagedResult<Message>> GetList(PageOptions page)
        {
            return WithTenantStorageReturnList<Message>(FolderName, storage => storage.GetList(page));
        }

        public Task Delete(Guid id)
        {
            WithTenantStorage<Message>(FolderName, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public Task<PagedResult<Message>> Find(string text)
        {
            throw new NotImplementedException();
        }

        public string FolderName
        {
            get
            {
                return _folderName;
            }
        }

    }
}