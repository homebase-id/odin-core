using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.FileSystem;
using Odin.Core.Services.Transit.Encryption;
using Odin.Core.Services.Transit.ReceivingHost.Incoming;

namespace Odin.Core.Services.Transit.ReceivingHost.Quarantine
{
    public class TransitPerimeterTransferStateService : ITransitPerimeterTransferStateService
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly IDriveFileSystem _fileSystem;
        private readonly ConcurrentDictionary<Guid, IncomingTransferStateItem> _state = new();

        public TransitPerimeterTransferStateService(IDriveFileSystem fileSystem, OdinContextAccessor contextAccessor)
        {
            _fileSystem = fileSystem;
            _contextAccessor = contextAccessor;
        }

        public async Task<Guid> CreateTransferStateItem(EncryptedRecipientTransferInstructionSet transferInstructionSet)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(transferInstructionSet.TargetDrive);

            //notice here: we always create a new file Id when receiving a new file.
            Guid id = Guid.NewGuid();
            var file = _fileSystem.Storage.CreateInternalFileId(driveId);
            var item = new IncomingTransferStateItem(id, file, transferInstructionSet);

            //write the instruction set to disk
            await using var stream = new MemoryStream(OdinSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray());
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
                throw new YouverseSystemException("Invalid perimeter state item");
            }

            return await Task.FromResult(item);
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

        private void Save(IncomingTransferStateItem stateItem)
        {
            _state.TryAdd(stateItem.Id.Value, stateItem);
            // _tenantSystemStorage.SingleKeyValueStorage.Upsert(stateItem.Id, stateItem);
        }
    }
}