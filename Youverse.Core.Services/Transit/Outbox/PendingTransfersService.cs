using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class PendingTransfersService : IPendingTransfersService
    {
        private readonly ILogger<IPendingTransfersService> _logger;
        private readonly string _dataPath;
        private const string PendingTransferCollection = "ptc";
        private KeyValueDatabase _db;
        private TableOutbox _table;

        private readonly byte[] _boxId = new byte[] { 1, 1, 2, 3, 5 };
        private readonly byte[] _fileId = new byte[] { 1, 1, 2, 3, 5 };

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
            _table.InsertRow(_boxId, _fileId, 0, sender.Id.ToLower().ToUtf8ByteArray());
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