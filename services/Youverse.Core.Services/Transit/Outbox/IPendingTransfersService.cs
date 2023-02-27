using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Outbox
{
    /// <summary>
    /// A global queue (singleton) list of senders who need
    /// their outboxes stoked (sounds kinky =)
    /// </summary>
    public interface IPendingTransfersService : IDisposable
    {
        /// <summary>
        /// Adds the <see cref="OdinId"/> to the pending transfer queue to be processed by the background job
        /// </summary>
        public void EnsureIdentityIsPending(OdinId sender);

        public Task<(IEnumerable<OdinId>, Guid marker)> GetIdentities();

        public void MarkComplete(Guid marker);
        public void MarkFailure(Guid marker);
    }
}