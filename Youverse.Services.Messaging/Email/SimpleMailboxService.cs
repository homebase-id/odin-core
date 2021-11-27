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
    public class SimpleMailboxService : IMailboxService
    {
        private readonly string _folderName;
        private readonly ISystemStorage _systemStorage;

        public SimpleMailboxService(DotYouContext context, string folderName, ISystemStorage systemStorage)
        {
            _folderName = folderName;
            _systemStorage = systemStorage;
        }

        public Task Save(Message message)
        {
            _systemStorage.WithTenantSystemStorage<Message>(FolderName, storage => storage.Save(message));
            return Task.CompletedTask;
        }

        public async Task<Message> Get(Guid id)
        {
            return await _systemStorage.WithTenantSystemStorageReturnSingle<Message>(FolderName, storage => storage.Get(id));
        }

        public Task<PagedResult<Message>> GetList(string folderPath, PageOptions page)
        {
            
            var result = _systemStorage.WithTenantSystemStorageReturnList<Message>(FolderName, storage => storage.Find(msg=>msg.Folder.StartsWith(folderPath, StringComparison.InvariantCultureIgnoreCase), page));
            return result;
        }

        public Task Delete(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<Message>(FolderName, s => s.Delete(id));
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