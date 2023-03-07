using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class PendingTransfersService : IPendingTransfersService
    {
        private IdentityDatabase _db;  // TODO: This looks incorrect, it should fetch the DB object from somewhere shouldn't it?

        public PendingTransfersService(string dataPath)
        {
            Guard.Argument(dataPath, nameof(dataPath)).NotNull().NotEmpty();
            var finalPath = PathUtil.OsIfy(dataPath);
            
            if (!Directory.Exists(finalPath))
            {
                Directory.CreateDirectory(finalPath!);
                Utils.ShellExecute($"chmod -R +rw {finalPath}");

            }
            var filePath = PathUtil.OsIfy($"{dataPath}{Path.PathSeparator}xfer.db");
            
            Utils.ShellExecute($"chmod -R +rw {filePath}");

            _db = new IdentityDatabase($"Data Source={filePath}");
            _db.CreateDatabase(false);
        }

        public void Dispose()
        {
            _db?.Dispose();
        }

        public void EnsureIdentityIsPending(OdinId sender)
        {
            //Note: I use sender here because boxId has a unique constraint; and we only a sender in this table once.
            //I swallow the exception because there's no direct way to see if a record exists for this sender already
            // byte[] fileId = new byte[] { 1, 1, 2, 3, 5 };
            Guid fileId = Guid.NewGuid();
            try
            {
                // _db.tblOutbox.InsertRow(sender.ToGuidIdentifier().ToByteArray(), fileId, 0, sender.Id.ToLower().ToUtf8ByteArray());
                _db.tblOutbox.Insert(new OutboxItem() { boxId = sender.ToHashId(), fileId = fileId, recipient = sender.Id.ToLower(), priority = 0, value = sender.Id.ToLower().ToUtf8ByteArray() });
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                //ignore constraint error code as it just means we tried to insert the sender twice.
                //it's only needed once
                if (ex.ErrorCode != 19) //constraint
                {
                    throw;
                }
            }
        }

        public async Task<(IEnumerable<OdinId>, Guid marker)> GetIdentities()
        {
            var records = _db.tblOutbox.PopAll(out var marker);

            var senders = records.Select(item => new OdinId(item.value.ToStringFromUtf8Bytes())).ToList();

            return (senders, marker);
        }

        public void MarkComplete(Guid marker)
        {
            _db.tblOutbox.PopCommit(marker);
        }

        public void MarkFailure(Guid marker)
        {
            _db.tblOutbox.PopCancel(marker);
        }
    }
}