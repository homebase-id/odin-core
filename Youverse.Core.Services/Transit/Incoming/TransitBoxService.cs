using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Mediator.ClientNotifications;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Transit.Incoming
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class TransitBoxService : ITransitBoxService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IMediator _mediator;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;


        public TransitBoxService(ILogger<ITransitBoxService> logger, ISystemStorage systemStorage, IMediator mediator, DotYouContextAccessor contextAccessor, IDriveService driveService)
        {
            _systemStorage = systemStorage;
            _mediator = mediator;
            _contextAccessor = contextAccessor;
            _driveService = driveService;
        }

        public Task Add(TransferBoxItem item)
        {
            item.AddedTimestamp = DateTimeExtensions.UnixTimeMilliseconds();
            
            _systemStorage.WithTenantSystemStorage<TransferBoxItem>(GetAppCollectionName(item.TempFile.DriveId), s => s.Save(item));

            var ext =  new ExternalFileIdentifier()
            {
                TargetDrive = _driveService.GetDrive(item.TempFile.DriveId).Result.TargetDriveInfo,
                FileId = item.TempFile.FileId
            };
            
            _mediator.Publish(new NewInboxItemNotification()
            {
                InboxItemId = item.Id,
                Sender = item.Sender,
                TempFile = ext
            });

            return Task.CompletedTask;
        }

        public async Task<PagedResult<TransferBoxItem>> GetPendingItems(Guid driveId, PageOptions pageOptions)
        {
            return await _systemStorage.WithTenantSystemStorageReturnList<TransferBoxItem>(GetAppCollectionName(driveId), s => s.GetList(pageOptions));
        }

        public async Task<TransferBoxItem> GetItem(Guid driveId, Guid id)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<TransferBoxItem>(GetAppCollectionName(driveId), s => s.Get(id));
            return item;
        }

        public Task Remove(Guid driveId, Guid id)
        {
            _systemStorage.WithTenantSystemStorage<TransferBoxItem>(GetAppCollectionName(driveId), s => s.Delete(id));
            return Task.CompletedTask;
        }

        private string GetAppCollectionName(Guid driveId)
        {
            return $"tbox_{driveId:N}";
        }
    }
}