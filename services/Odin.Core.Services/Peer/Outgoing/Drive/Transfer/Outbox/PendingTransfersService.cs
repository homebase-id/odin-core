using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class PendingTransfersService(ServerSystemStorage serverSystemStorage) : IPendingTransfersService
    {
        public void EnsureIdentityIsPending(OdinId sender)
        {
            serverSystemStorage.EnqueueJob(sender, CronJobType.PendingTransitTransfer, sender.DomainName.ToLower().ToUtf8ByteArray());
        }

        public async Task<(IEnumerable<OdinId>, Guid marker)> GetIdentities()
        {
            var records = serverSystemStorage.tblCron.Pop(1);

            if (!records.Any())
            {
                return (new List<OdinId>(), Guid.Empty);
            }

            var senders = records.Select(item => new OdinId(item.data.ToStringFromUtf8Bytes())).ToList();

            var result = (senders, records.First().popStamp.GetValueOrDefault());
            return await Task.FromResult(result);
        }

        public void MarkComplete(Guid marker)
        {
            serverSystemStorage.tblCron.PopCommitList(new List<Guid>() { marker });
        }

        public void MarkFailure(Guid marker)
        {
            serverSystemStorage.tblCron.PopCancelList(new List<Guid>() { marker });
        }
    }
}