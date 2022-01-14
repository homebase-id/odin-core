﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator;

namespace Youverse.Core.Services.Transit.Inbox
{
    /// <summary>
    /// Services that manages items in a given Tenant's Inbox
    /// </summary>
    public class InboxService : IInboxService
    {
        private const string InboxItemsCollection = "inbxitems";

        private readonly ISystemStorage _systemStorage;
        private readonly IMediator _mediator;
        private readonly DotYouContext _context;
        private readonly IDriveService _driveService;
        private readonly IAppService _appService;

        public InboxService(ILogger<IInboxService> logger, ISystemStorage systemStorage, IMediator mediator, DotYouContext context, IDriveService driveService, IAppService appService)
        {
            _systemStorage = systemStorage;
            _mediator = mediator;
            _context = context;
            _driveService = driveService;
            _appService = appService;
        }

        /// <summary>
        /// Adds an item to be encrypted and moved to the Inbox
        /// </summary>
        /// <param name="item"></param>
        public Task Add(InboxItem item)
        {
            item.AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds();
            _systemStorage.WithTenantSystemStorage<InboxItem>(InboxItemsCollection, s => s.Save(item));

            _mediator.Publish(new NewInboxItemNotification()
            {
                InboxItemId = item.Id,
                Sender = item.Sender,
                AppId = item.AppId,
                TempFile = item.TempFile
            });

            return Task.CompletedTask;
        }

        public Task Add(IEnumerable<InboxItem> items)
        {
            foreach (var item in items)
            {
                this.Add(item);
            }

            return Task.CompletedTask;
        }

        public async Task ProcessTransfers()
        {
            var items = await GetPendingItems(PageOptions.All);
            foreach (var item in items.Results)
            {
                var stream = _driveService.GetTempStream(item.TempFile, MultipartHostTransferParts.TransferKeyHeader.ToString());

                _appService.WriteTransferKeyHeader(item.TempFile,)
            }
        }

        public Task<PagedResult<InboxItem>> GetNextBatch()
        {
            throw new NotImplementedException();
            //return Array.Empty<InboxItem>();
        }

        /// <summary>
        /// Gets all the items currently in the queue w/o making changes to it 
        /// </summary>
        /// <returns></returns>
        public async Task<PagedResult<InboxItem>> GetPendingItems(PageOptions pageOptions)
        {
            return await _systemStorage.WithTenantSystemStorageReturnList<InboxItem>(InboxItemsCollection, s => s.GetList(pageOptions));
        }

        public async Task Remove(DotYouIdentity recipient, DriveFileId file)
        {
            //TODO: need to make a better queue here
            Expression<Func<InboxItem, bool>> predicate = item => item.Sender == recipient && item.TempFile == file;
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<InboxItem>(InboxItemsCollection, s => s.FindOne(predicate));
            _systemStorage.WithTenantSystemStorage<InboxItem>(InboxItemsCollection, s => s.Delete(item.Id));
        }

        public async Task<InboxItem> GetItem(Guid id)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<InboxItem>(InboxItemsCollection, s => s.Get(id));
            return item;
        }

        public Task RemoveItem(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<InboxItem>(GetAppCollectionName(), s => s.Delete(id));
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