using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Outbox
{
    /// <summary>
    /// A global queue (singleton) list of senders who need
    /// their outboxes stoked (sounds kinky =)
    /// </summary>
    public interface IPendingTransfersService
    {
        /// <summary>
        /// Adds the <see cref="DotYouIdentity"/> to the pending transfer queue to be processed by the background job
        /// </summary>
        public void EnsureSenderIsPending(DotYouIdentity sender);

        public Task<IEnumerable<DotYouIdentity>> GetSenders();
    }
}