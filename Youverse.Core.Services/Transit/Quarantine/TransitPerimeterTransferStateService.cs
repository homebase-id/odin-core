using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPerimeterTransferStateService : ITransitPerimeterTransferStateService
    {
        private const string IncomingTransferStateItemCollection = "transit_incoming";

        private readonly ISystemStorage _systemStorage;
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;

        public TransitPerimeterTransferStateService(ISystemStorage systemStorage, IDriveService driveService, DotYouContextAccessor contextAccessor)
        {
            _systemStorage = systemStorage;
            _driveService = driveService;
            _contextAccessor = contextAccessor;
        }

        public async Task<Guid> CreateTransferStateItem(RsaEncryptedRecipientTransferKeyHeader rsaKeyHeader)
        {
            Guid id = Guid.NewGuid();

            var file = _driveService.CreateFileId(_contextAccessor.GetCurrent().AppContext.DefaultDriveId.GetValueOrDefault());
            var item = new IncomingTransferStateItem(id, file);

            await using var stream = new MemoryStream(JsonConvert.SerializeObject(rsaKeyHeader).ToUtf8ByteArray());
            await _driveService.WriteTempStream(file, MultipartHostTransferParts.TransferKeyHeader.ToString().ToLower(), stream);

            item.SetFilterState(MultipartHostTransferParts.TransferKeyHeader, FilterAction.Accept);

            this.Save(item);

            return id;
        }

        public async Task<IncomingTransferStateItem> GetStateItem(Guid id)
        {
            var item = await _systemStorage.WithTenantSystemStorageReturnSingle<IncomingTransferStateItem>(IncomingTransferStateItemCollection, s => s.Get(id));

            if (null == item)
            {
                throw new TransitException("Invalid perimeter state item");
            }

            return item;
        }

        public async Task AcceptPart(Guid transferStateItemId, MultipartHostTransferParts part, Stream data)
        {
            var item = await this.GetStateItem(transferStateItemId);
            item.SetFilterState(part, FilterAction.Accept);

            await _driveService.WriteTempStream(item.TempFile, part.ToString().ToLower(), data);
            this.Save(item);
        }

        public async Task Quarantine(Guid transferStateItemId, MultipartHostTransferParts part, Stream data)
        {
            var item = await this.GetStateItem(transferStateItemId);
            item.SetFilterState(part, FilterAction.Quarantine);
            await _driveService.WriteTempStream(item.TempFile, part.ToString().ToLower(), data);
            this.Save(item);
        }

        public async Task Reject(Guid transferStateItemId, MultipartHostTransferParts part)
        {
            var item = await this.GetStateItem(transferStateItemId);
            item.SetFilterState(part, FilterAction.Reject);

            //Note: we remove all temp files if a single part is rejected
            await _driveService.DeleteTempFiles(item.TempFile);

            this.Save(item);
        }

        public Task RemoveStateItem(Guid transferStateItemId)
        {
            _systemStorage.WithTenantSystemStorage<IncomingTransferStateItem>(IncomingTransferStateItemCollection, s => s.Delete(transferStateItemId));
            return Task.CompletedTask;
        }

        private void Save(IncomingTransferStateItem stateItem)
        {
            _systemStorage.WithTenantSystemStorage<IncomingTransferStateItem>(IncomingTransferStateItemCollection, s => s.Save(stateItem));
        }
    }
}