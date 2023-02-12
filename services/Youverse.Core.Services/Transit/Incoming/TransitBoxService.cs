using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.Incoming
{
    /// <summary>
    /// Manages items incoming to a DI that have not yet been processed (pre-inbox)
    /// </summary>
    public class TransitBoxService : ITransitBoxService
    {
        private readonly ITenantSystemStorage _tenantSystemStorage;
        private readonly IMediator _mediator;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly DriveManager _driveManager;
        
        public TransitBoxService(ILogger<ITransitBoxService> logger, ITenantSystemStorage tenantSystemStorage, IMediator mediator, DotYouContextAccessor contextAccessor, DriveManager driveManager)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _mediator = mediator;
            _contextAccessor = contextAccessor;
            _driveManager = driveManager;
        }

        public Task Add(TransferBoxItem item)
        {
            item.AddedTimestamp = UnixTimeUtc.Now();

            var state = DotYouSystemSerializer.Serialize(item).ToUtf8ByteArray();
            _tenantSystemStorage.Inbox.InsertRow(item.DriveId.ToByteArray(), item.FileId.ToByteArray(), 1, state);

            _mediator.Publish(new TransitFileReceivedNotification()
            {
                // InboxItemId = item.Id,
                // Sender = item.Sender,
                TempFile = new ExternalFileIdentifier()
                {
                    TargetDrive = _driveManager.GetDrive(item.DriveId).Result.TargetDriveInfo,
                    FileId = item.FileId
                }
            });

            return Task.CompletedTask;
        }

        public async Task<List<TransferBoxItem>> GetPendingItems(Guid driveId)
        {
            //CRITICAL NOTE: we can only get back one item since we want to make sure the marker is for that one item in-case the operation fails
            var records = this._tenantSystemStorage.Inbox.Pop(driveId.ToByteArray(), 1, out var marker);

            var record = records.SingleOrDefault();
            if (null == record)
            {
                return new List<TransferBoxItem>();
            }

            var items = records.Select(r =>
            {
                var item = DotYouSystemSerializer.Deserialize<TransferBoxItem>(r.value.ToStringFromUtf8Bytes());

                item.Priority = (int)r.priority;
                item.AddedTimestamp = r.timeStamp;
                item.DriveId = new Guid(r.boxId);
                item.FileId = new Guid(r.fileId);
                
                return item;
                
                // return new TransferBoxItem()
                // {
                //     Sender = (DotYouIdentity)item.Sender,
                //     Priority = (int)r.priority,
                //     AddedTimestamp = r.timeStamp,
                //     DriveId = new Guid(r.boxId),
                //     FileId = new Guid(r.fileId),
                //     GlobalTransitId = item.GlobalTransitId,
                //     Marker = marker,
                //     Id = item.Id,
                //     PublicKeyCrc = item.PublicKeyCrc
                // };
            }).ToList();

            return items;
        }

        public Task MarkComplete(Guid driveId, byte[] marker)
        {
            _tenantSystemStorage.Inbox.PopCommit(marker);
            return Task.CompletedTask;
        }

        public Task MarkFailure(Guid driveId, byte[] marker)
        {
            _tenantSystemStorage.Inbox.PopCancel(marker);
            return Task.CompletedTask;
        }
    }
}