using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator;

namespace Youverse.Core.Services.Transit.Incoming
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class InboxService : IInboxService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IMediator _mediator;
        private readonly DotYouContext _context;

        public InboxService(ILogger<ITransitBoxService> logger, ISystemStorage systemStorage, IMediator mediator, DotYouContext context)
        {
            _systemStorage = systemStorage;
            _mediator = mediator;
            _context = context;
        }


        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        public Task Add(InboxItem item)
        {
            item.AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds();
            _systemStorage.WithTenantSystemStorage<InboxItem>(GetAppCollectionName(), s => s.Save(item));

            _mediator.Publish(new NewInboxItemNotification()
            {
                InboxItemId = item.Id,
                Sender = item.Sender,
                AppId = item.AppId,
                TempFile = item.File
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets all the items currently in the queue w/o making changes to it 
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<InboxItem>> GetPendingItems(PageOptions pageOptions)
        {
            return await _systemStorage.WithTenantSystemStorageReturnList<InboxItem>(GetAppCollectionName(), s => s.GetList(pageOptions));
        }

        public async Task Remove(DotYouIdentity recipient, DriveFileId file)
        {
            //TODO: need to make a better queue here
            Expression<Func<TransferBoxItem, bool>> predicate = item => item.Sender == recipient && item.TempFile == file;
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<TransferBoxItem>(GetAppCollectionName(), s => s.FindOne(predicate));
            _systemStorage.WithTenantSystemStorage<TransferBoxItem>(GetAppCollectionName(), s => s.Delete(item.Id));
        }

        public async Task<InboxItem> GetItem(Guid id)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<InboxItem>(GetAppCollectionName(), s => s.Get(id));
            return item;
        }

        public Task RemoveItem(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<TransferBoxItem>(GetAppCollectionName(), s => s.Delete(id));
            return Task.CompletedTask;
        }

        private string GetAppCollectionName()
        {
            return $"ibx_{_context.AppContext.AppId:N}";
        }
    }
}