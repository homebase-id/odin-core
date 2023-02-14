using System;
using System.Collections.Concurrent;
using System.Data.Entity.Migrations.Model;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPerimeterTransferStateService : ITransitPerimeterTransferStateService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly StandardFileSystem _fileSystem;

        public TransitPerimeterTransferStateService(StandardFileSystem fileSystem, DotYouContextAccessor contextAccessor)
        {
            _fileSystem = fileSystem;
            _contextAccessor = contextAccessor;
        }

        public async Task<Guid> CreateTransferStateItem(RsaEncryptedRecipientTransferInstructionSet transferInstructionSet)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(transferInstructionSet.TargetDrive);

            //notice here: we always create a new file Id when receiving a new file.
            Guid id = Guid.NewGuid();
            var file = _fileSystem.Storage.CreateInternalFileId(driveId);
            var item = new IncomingTransferStateItem(id, file);
            
            //write the instruction set to disk
            await using var stream = new MemoryStream(DotYouSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray());
            await _fileSystem.Storage.WriteTempStream(file, MultipartHostTransferParts.TransferKeyHeader.ToString().ToLower(), stream);

            item.SetFilterState(MultipartHostTransferParts.TransferKeyHeader, FilterAction.Accept);

            this.Save(item);
            return id;
        }

        public async Task<IncomingTransferStateItem> GetStateItem(Guid id)
        {
            // var item = _tenantSystemStorage.SingleKeyValueStorage.Get<IncomingTransferStateItem>(id);
            _state.TryGetValue(id, out var item);
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

            await _fileSystem.Storage.WriteTempStream(item.TempFile, fileExtension, data);
            this.Save(item);
        }

        public async Task Quarantine(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data)
        {
            var item = await this.GetStateItem(transferStateItemId);
            item.SetFilterState(part, FilterAction.Quarantine);
            await _fileSystem.Storage.WriteTempStream(item.TempFile, fileExtension, data);
            this.Save(item);
        }

        public async Task Reject(Guid transferStateItemId, MultipartHostTransferParts part)
        {
            var item = await this.GetStateItem(transferStateItemId);
            item.SetFilterState(part, FilterAction.Reject);

            //Note: we remove all temp files if a single part is rejected
            await _fileSystem.Storage.DeleteTempFiles(item.TempFile);

            this.Save(item);
        }

        public Task RemoveStateItem(Guid transferStateItemId)
        {
            // _tenantSystemStorage.SingleKeyValueStorage.Delete(transferStateItemId);
            _state.TryRemove(transferStateItemId, out var _);
            return Task.CompletedTask;
        }

        private readonly ConcurrentDictionary<Guid, IncomingTransferStateItem> _state = new ();
        
        private void Save(IncomingTransferStateItem stateItem)
        {
            _state.TryAdd(stateItem.Id.Value, stateItem);
            // _tenantSystemStorage.SingleKeyValueStorage.Upsert(stateItem.Id, stateItem);
        }
    }
}