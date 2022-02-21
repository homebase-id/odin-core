using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Mediator.ClientNotifications;

namespace Youverse.Core.Services.Transit.Incoming
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class TransitBoxService : ITransitBoxService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IMediator _mediator;

        public TransitBoxService(ILogger<ITransitBoxService> logger, ISystemStorage systemStorage, IMediator mediator)
        {
            _systemStorage = systemStorage;
            _mediator = mediator;
        }
        
        public Task Add(TransferBoxItem item)
        {
            item.AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds();
            _systemStorage.WithTenantSystemStorage<TransferBoxItem>(GetAppCollectionName(item.AppId), s => s.Save(item));

            _mediator.Publish(new NewInboxItemNotification()
            {
                InboxItemId = item.Id,
                Sender = item.Sender,
                AppId = item.AppId,
                TempFile = item.TempFile
            });

            return Task.CompletedTask;
        }

        public async Task<PagedResult<TransferBoxItem>> GetPendingItems(Guid appId, PageOptions pageOptions)
        {
            return await _systemStorage.WithTenantSystemStorageReturnList<TransferBoxItem>(GetAppCollectionName(appId), s => s.GetList(pageOptions));
        }
        
        public async Task<TransferBoxItem> GetItem(Guid appId, Guid id)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<TransferBoxItem>(GetAppCollectionName(appId), s => s.Get(id));
            return item;
        }

        public Task Remove(Guid appId, Guid id)
        {
            _systemStorage.WithTenantSystemStorage<TransferBoxItem>(GetAppCollectionName(appId), s => s.Delete(id));
            return Task.CompletedTask;
        }

        private string GetAppCollectionName(Guid appId)
        {
            return $"tbox_{appId:N}";
        }
    }
}