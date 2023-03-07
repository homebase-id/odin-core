using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;
using Youverse.Core.Storage.Sqlite.ServerDatabase;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class PendingTransfersService : IPendingTransfersService
    {
        private readonly ServerDatabase _db;

        public PendingTransfersService(string dataPath)
        {
            Guard.Argument(dataPath, nameof(dataPath)).NotNull().NotEmpty();
            var finalPath = PathUtil.OsIfy(dataPath);

            if (!Directory.Exists(finalPath))
            {
                Directory.CreateDirectory(finalPath!);
                // Utils.ShellExecute($"chmod -R +rw {finalPath}");
            }

            var filePath = PathUtil.OsIfy($"{dataPath}{Path.PathSeparator}xfer.db");

            // Utils.ShellExecute($"chmod -R +rw {filePath}");

            // _db = new IdentityDatabase($"Data Source={filePath}");
            _db = new ServerDatabase($"Data Source={filePath}");
            _db.CreateDatabase(false);
        }

        public void Dispose()
        {
            _db?.Dispose();
        }

        public void EnsureIdentityIsPending(OdinId sender)
        {
            try
            {
                _db.tblCron.Insert(new CronItem()
                {
                    identityId = sender,
                    type = 1,
                    data = sender.Id.ToLower().ToUtf8ByteArray(),
                });
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
            var records = _db.tblCron.Pop(1, out var marker);

            var senders = records.Select(item => new OdinId(item.data.ToStringFromUtf8Bytes())).ToList();

            return (senders, marker);
        }

        public void MarkComplete(Guid marker)
        {
            _db.tblCron.PopCommitList(new List<Guid>() { marker });
        }

        public void MarkFailure(Guid marker)
        {
            _db.tblCron.PopCancelList(new List<Guid>() { marker });
        }
    }
}