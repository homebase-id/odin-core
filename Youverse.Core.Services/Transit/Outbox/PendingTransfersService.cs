using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class PendingTransfersService : IPendingTransfersService
    {
        private readonly ILogger<IPendingTransfersService> _logger;
        private readonly string _dataPath;
        private KeyValueDatabase _db;
        private TableOutbox _table;


        public PendingTransfersService(ILogger<IPendingTransfersService> logger)
        {
            //TODO: get from injection?
            _dataPath = PathUtil.OsIfy("\\tmp\\dotyou\\system\\");
            _logger = logger;

            _db = new KeyValueDatabase($"URI=file:{_dataPath}\\xfer.db");
            _db.CreateDatabase(false);
            _table = new TableOutbox(_db);
        }

        public void EnsureSenderIsPending(DotYouIdentity sender)
        {
            //Note: I use sender here because boxId has a unique constraint; and we only a sender in this table once.
            //I swallow the exception because there's no direct way to see if a record exists for this sender already
            byte[] fileId = new byte[] { 1, 1, 2, 3, 5 };
            try
            {
                _table.InsertRow(sender.Id.ToLower().ToUtf8ByteArray(), fileId, 0, sender.Id.ToLower().ToUtf8ByteArray());
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