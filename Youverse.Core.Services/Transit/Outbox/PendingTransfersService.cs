using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class PendingTransfersService : IPendingTransfersService
    {
        private readonly TableOutbox _table;

        public PendingTransfersService(string dataPath)
        {
            Guard.Argument(dataPath, nameof(dataPath)).NotNull().NotEmpty();
            var finalPath = PathUtil.OsIfy(dataPath);
            
            if (!Directory.Exists(finalPath))
            {
                Directory.CreateDirectory(finalPath!);
            }
            var filePath = PathUtil.OsIfy($"{dataPath}\\xfer.db");
            var db = new KeyValueDatabase($"URI=file:{filePath}");
            db.CreateDatabase(false);
            _table = new TableOutbox(db);
        }

        public void EnsureSenderIsPending(DotYouIdentity sender)
        {
            //Note: I use sender here because boxId has a unique constraint; and we only a sender in this table once.
            //I swallow the exception because there's no direct way to see if a record exists for this sender already
            // byte[] fileId = new byte[] { 1, 1, 2, 3, 5 };
            byte[] fileId = Guid.NewGuid().ToByteArray();
            try
            {
                _table.InsertRow(sender.ToGuidIdentifier().ToByteArray(), fileId, 0, sender.Id.ToLower().ToUtf8ByteArray());
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                //ignore constraint error code as it just means we tried to insert the sender twice.
                //it's only needed once
                if (ex.ErrorCode != 19) //constraint
                {
                    throw;
                }
            }
        }

        public async Task<(IEnumerable<DotYouIdentity>, byte[] marker)> GetSenders()
        {
            var records = _table.PopAll(out var marker);

            var senders = records.Select(item => new DotYouIdentity(item.value.ToStringFromUtf8Bytes())).ToList();

            return (senders, marker);
        }

        public void MarkComplete(byte[] marker)
        {
            _table.PopCommit(marker);
        }

        public void MarkFailure(byte[] marker)
        {
            _table.PopCancel(marker);
        }
    }
}