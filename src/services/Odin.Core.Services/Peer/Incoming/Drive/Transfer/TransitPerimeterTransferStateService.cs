using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.FileSystem;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.Incoming.Drive.Transfer.InboxStorage;

namespace Odin.Core.Services.Peer.Incoming.Drive.Transfer
{
    public class TransitPerimeterTransferStateService(IDriveFileSystem fileSystem, OdinContextAccessor contextAccessor) : ITransitPerimeterTransferStateService
    {
        private readonly ConcurrentDictionary<Guid, IncomingTransferStateItem> _state = new();

        public async Task<Guid> CreateTransferStateItem(EncryptedRecipientTransferInstructionSet transferInstructionSet)
        {
            var driveId = contextAccessor.GetCurrent().PermissionsContext.GetDriveId(transferInstructionSet.TargetDrive);

            // Notice here: we always create a new file Id when receiving a new file.
            Guid id = Guid.NewGuid();
            var file = fileSystem.Storage.CreateInternalFileId(driveId);
            var item = new IncomingTransferStateItem(id, file, transferInstructionSet);

            // Write the instruction set to disk
            await using var stream = new MemoryStream(OdinSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray());
            await fileSystem.Storage.WriteTempStream(file, MultipartHostTransferParts.TransferKeyHeader.ToString().ToLower(), stream);
            
            this.Save(item);
            return id;
        }

        public async Task<IncomingTransferStateItem> GetStateItem(Guid id)
        {
            // var item = _tenantSystemStorage.SingleKeyValueStorage.Get<IncomingTransferStateItem>(id);
            _state.TryGetValue(id, out var item);
            if (null == item)
            {
                throw new OdinSystemException("Invalid perimeter state item");
            }

            return await Task.FromResult(item);
        }

        public async Task AcceptPart(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data)
        {
            var item = await this.GetStateItem(transferStateItemId);
            await fileSystem.Storage.WriteTempStream(item.TempFile, fileExtension, data);
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