using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core;
using Youverse.Core.Services.Base;

namespace Youverse.Services.Messaging.Email
{
    /// <summary>
    /// Stores messages in the given folder name using LiteDb.
    /// </summary>
    public class SimpleMailboxService : DotYouServiceBase<IMailboxService>, IMailboxService
    {
        private readonly string _folderName;

        public SimpleMailboxService(DotYouContext context, string folderName) : base(context, null, null, null)
        {
            _folderName = folderName;
        }

        public Task Save(Message message)
        {
            WithTenantSystemStorage<Message>(FolderName, storage => storage.Save(message));
            return Task.CompletedTask;
        }

        public async Task<Message> Get(Guid id)
        {
            return await WithTenantSystemStorageReturnSingle<Message>(FolderName, storage => storage.Get(id));
        }

        public Task<PagedResult<Message>> GetList(string folderPath, PageOptions page)
        {
            
            var result = WithTenantSystemStorageReturnList<Message>(FolderName, storage => storage.Find(msg=>msg.Folder.StartsWith(folderPath, StringComparison.InvariantCultureIgnoreCase), page));
            return result;
        }

        public Task Delete(Guid id)
        {
            WithTenantSystemStorage<Message>(FolderName, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public Task<PagedResult<Message>> Find(string text)
        {
            throw new NotImplementedException();
        }

        public string FolderName
        {
            get { return _folderName; }
        }
    }
}