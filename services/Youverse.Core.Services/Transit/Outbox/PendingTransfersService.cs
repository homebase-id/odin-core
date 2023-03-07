using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;
using Youverse.Core.Storage.Sqlite.ServerDatabase;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class PendingTransfersService : IPendingTransfersService
    {
        private readonly ServerSystemStorage _serverSystemStorage;

        public PendingTransfersService(ServerSystemStorage serverSystemStorage)
        {
            _serverSystemStorage = serverSystemStorage;
        }
        
        public void EnsureIdentityIsPending(OdinId sender)
        {
            try
            {
                _serverSystemStorage.tblCron.Insert(new CronRecord()
                {
                    identityId = sender,
                    type = 1,
                    data = sender.DomainName.ToLower().ToUtf8ByteArray(),
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
            var records = _serverSystemStorage.tblCron.Pop(1, out var marker);

            var senders = records.Select(item => new OdinId(item.data.ToStringFromUtf8Bytes())).ToList();

            return (senders, marker);
        }

        public void MarkComplete(Guid marker)
        {
            _serverSystemStorage.tblCron.PopCommitList(new List<Guid>() { marker });
        }

        public void MarkFailure(Guid marker)
        {
            _serverSystemStorage.tblCron.PopCancelList(new List<Guid>() { marker });
        }
    }
}