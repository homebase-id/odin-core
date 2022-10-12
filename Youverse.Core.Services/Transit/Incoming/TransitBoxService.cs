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
using Youverse.Core.Services.Mediator.ClientNotifications;
using Youverse.Core.Storage;

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
            item.AddedTimestamp = UnixTimeUtcSeconds.Now();

            var state = DotYouSystemSerializer.Serialize(item).ToUtf8ByteArray();
            _systemStorage.Inbox.InsertRow(item.TempFile.DriveId.ToByteArray(), item.TempFile.FileId.ToByteArray(), 1, state);

            _mediator.Publish(new NewInboxItemNotification()
            {
                InboxItemId = item.Id,
                Sender = item.Sender,
                TempFile = new ExternalFileIdentifier()
                {
                    TargetDrive = _driveService.GetDrive(item.TempFile.DriveId).Result.TargetDriveInfo,
                    FileId = item.TempFile.FileId
                }
            });

            return Task.CompletedTask;
        }

        public async Task<List<TransferBoxItem>> GetPendingItems(Guid driveId)
        {
            //CRITICAL NOTE: we can only get back one item since we want to make sure the marker is for that one item in-case the operation fails
            var records = this._systemStorage.Inbox.Pop(driveId.ToByteArray(), 1, out var marker);

            var record = records.SingleOrDefault();
            if (null == record)
            {
                return new List<TransferBoxItem>();
            }

            var items = records.Select(r =>
            {
                var item = DotYouSystemSerializer.Deserialize<TransferBoxItem>(r.value.ToStringFromUtf8Bytes());
                return new TransferBoxItem()
                {
                    Sender = (DotYouIdentity)item.Sender,
                    Priority = (int)r.priority,
                    AddedTimestamp = r.timeStamp,
                    TempFile = new InternalDriveFileId()
                    {
                        DriveId = new Guid(r.boxId),
                        FileId = new Guid(r.fileId)
                    },
                    Marker = marker
                };
            }).ToList();

            return items;
        }

        public Task MarkComplete(Guid driveId, byte[] marker)
        {
            _systemStorage.Inbox.PopCommit(marker);
            return Task.CompletedTask;
        }

        public Task MarkFailure(Guid driveId, byte[] marker)
        {
            _systemStorage.Inbox.PopCancel(marker);
            return Task.CompletedTask;
        }
    }
}