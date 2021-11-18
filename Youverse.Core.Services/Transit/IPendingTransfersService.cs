using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit
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

        public IEnumerable<DotYouIdentity> GetSenders();
    }

    public class PendingTransfersService : IPendingTransfersService
    {
        private readonly ILogger<IPendingTransfersService> _logger;
        private readonly IDictionary<DotYouIdentity,object> _senders;
        public PendingTransfersService(ILogger<IPendingTransfersService> logger)
        {
            _logger = logger;
            //_queue = new Queue<PendingTransferQueueItem>();
            _senders = new Dictionary<DotYouIdentity, object>();
        }

        public void EnsureSenderIsPending(DotYouIdentity sender)
        {
            if (this._senders.TryAdd(sender, null))
            {
                _logger.LogInformation($"Added sender [{sender}] to the Pending Transfer Queue");
            }
        }

        public IEnumerable<DotYouIdentity> GetSenders()
        {
            //todo: remove the entries that were returned
            return this._senders.Keys.ToArray();
        }
    }

    /// <summary>
    /// Items that needs to be transferred
    /// </summary>
    public class PendingTransferQueueItem
    {
        public DotYouIdentity Sender { get; set; }

        public System.UInt64 AddedTimeStamp { get; set; }
    }
}