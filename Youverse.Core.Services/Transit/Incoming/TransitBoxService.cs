using System;
using System.IO;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit.Incoming
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class TransitBoxService : ITransitBoxService
    {
        private const string TransitBoxItemsCollection = "transit_box";

        private readonly ISystemStorage _systemStorage;
        private readonly IMediator _mediator;
        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;
        
        public TransitBoxService(ILogger<ITransitBoxService> logger, ISystemStorage systemStorage, IMediator mediator, DotYouContext context, IDriveService driveService)
        {
            _systemStorage = systemStorage;
            _mediator = mediator;
            _context = context;
            _driveService = driveService;
        }


        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        public Task Add(TransferBoxItem item)
        {
            item.AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds();
            _systemStorage.WithTenantSystemStorage<TransferBoxItem>(TransitBoxItemsCollection, s => s.Save(item));

            _mediator.Publish(new NewInboxItemNotification()
            {
                InboxItemId = item.Id,
                Sender = item.Sender,
                AppId = item.AppId,
                TempFile = item.TempFile
            });

            return Task.CompletedTask;
        }


        /// <summary>
        /// Gets all the items currently in the queue w/o making changes to it 
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<TransferBoxItem>> GetPendingItems(PageOptions pageOptions)
        {
            return await _systemStorage.WithTenantSystemStorageReturnList<TransferBoxItem>(TransitBoxItemsCollection, s => s.GetList(pageOptions));
        }

        public async Task Remove(DotYouIdentity recipient, DriveFileId file)
        {
            //TODO: need to make a better queue here
            Expression<Func<TransferBoxItem, bool>> predicate = item => item.Sender == recipient && item.TempFile == file;
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<TransferBoxItem>(TransitBoxItemsCollection, s => s.FindOne(predicate));
            _systemStorage.WithTenantSystemStorage<TransferBoxItem>(TransitBoxItemsCollection, s => s.Delete(item.Id));
        }

        public async Task<TransferBoxItem> GetItem(Guid id)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<TransferBoxItem>(TransitBoxItemsCollection, s => s.Get(id));
            return item;
        }

        public Task RemoveItem(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<TransferBoxItem>(GetAppCollectionName(), s => s.Delete(id));
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Gets the collection name for a given app
        /// </summary>
        /// <param name="appId"></param>
        /// <returns></returns>
        private string GetAppCollectionName()
        {
            var appId = _context.AppContext?.AppId ?? _context.TransitContext.AppId;
            return $"ibx_{appId:N}";
        }
    }
}