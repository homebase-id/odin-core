using System;
using System.Data.Entity.Migrations.Model;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPerimeterTransferStateService : ITransitPerimeterTransferStateService
    {
        private const string _context = "tss";

        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;

        public TransitPerimeterTransferStateService(ISystemStorage systemStorage, IDriveService driveService)
        {
            _systemStorage = systemStorage;
            _driveService = driveService;
        }

        public async Task<Guid> CreateTransferStateItem(RsaEncryptedRecipientTransferInstructionSet transferInstructionSet)
        {
            Guid id = Guid.NewGuid();

            var driveId = (await _driveService.GetDriveIdByAlias(transferInstructionSet.Drive, true))!.Value;

            //notice here: we always create a new file Id when receiving a new file.
            //we might need to add a feature that lets multiple identities collaborate on
            //a the same file.  not sure who this will go.
            var file = _driveService.CreateInternalFileId(driveId);
            var item = new IncomingTransferStateItem(id, file);

            await using var stream = new MemoryStream(DotYouSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray());
            await _driveService.WriteTempStream(file, MultipartHostTransferParts.TransferKeyHeader.ToString().ToLower(), stream);

            item.SetFilterState(MultipartHostTransferParts.TransferKeyHeader, FilterAction.Accept);

            this.Save(item);
            return id;
        }

        public async Task<IncomingTransferStateItem> GetStateItem(Guid id)
        {
            var item = _systemStorage.SingleKeyValueStorage.Get<IncomingTransferStateItem>(id, _context);

            if (null == item)
            {
                throw new TransitException("Invalid perimeter state item");
            }

            return item;
        }

        public async Task AcceptPart(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data)
        {
            var item = await this.GetStateItem(transferStateItemId);
            item.SetFilterState(part, FilterAction.Accept);

            await _driveService.WriteTempStream(item.TempFile, fileExtension, data);
            this.Save(item);
        }

        public async Task Quarantine(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data)
        {
            var item = await this.GetStateItem(transferStateItemId);
            item.SetFilterState(part, FilterAction.Quarantine);
            await _driveService.WriteTempStream(item.TempFile, fileExtension, data);
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
            _systemStorage.SingleKeyValueStorage.Delete(transferStateItemId, _context);
            return Task.CompletedTask;
        }

        private void Save(IncomingTransferStateItem stateItem)
        {
            _systemStorage.SingleKeyValueStorage.Upsert(stateItem.Id, stateItem, _context);
        }
    }
}