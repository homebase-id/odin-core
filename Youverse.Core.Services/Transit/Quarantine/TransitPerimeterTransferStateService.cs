using System;
using System.Data.Entity.Migrations.Model;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPerimeterTransferStateService : ITransitPerimeterTransferStateService
    {
        private readonly ITenantSystemStorage _tenantSystemStorage;
        private readonly IDriveService _driveService;
        private readonly DotYouContextAccessor _contextAccessor;

        public TransitPerimeterTransferStateService(ITenantSystemStorage tenantSystemStorage, IDriveService driveService, DotYouContextAccessor contextAccessor)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _driveService = driveService;
            _contextAccessor = contextAccessor;
        }

        public async Task<Guid> CreateTransferStateItem(RsaEncryptedRecipientTransferInstructionSet transferInstructionSet)
        {
            //caller must have write permission to the drive in which they are transferring the file
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(transferInstructionSet.TargetDrive);
            // _contextAccessor.GetCurrent().PermissionsContext.HasDrivePermission(driveId, DrivePermission.Write);

            //notice here: we always create a new file Id when receiving a new file.
            Guid id = Guid.NewGuid();
            var file = _driveService.CreateInternalFileId(driveId);
            var item = new IncomingTransferStateItem(id, file);
            
            //write the instruction set to disk
            await using var stream = new MemoryStream(DotYouSystemSerializer.Serialize(transferInstructionSet).ToUtf8ByteArray());
            await _driveService.WriteTempStream(file, MultipartHostTransferParts.TransferKeyHeader.ToString().ToLower(), stream);

            item.SetFilterState(MultipartHostTransferParts.TransferKeyHeader, FilterAction.Accept);

            this.Save(item);
            return id;
        }

        public async Task<IncomingTransferStateItem> GetStateItem(Guid id)
        {
            var item = _tenantSystemStorage.SingleKeyValueStorage.Get<IncomingTransferStateItem>(id);

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
            _tenantSystemStorage.SingleKeyValueStorage.Delete(transferStateItemId);
            return Task.CompletedTask;
        }

        private void Save(IncomingTransferStateItem stateItem)
        {
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(stateItem.Id, stateItem);
        }
    }
}