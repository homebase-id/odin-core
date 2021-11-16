using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// A global queue (singleton) list of senders who need
    /// their outboxes stoked (sounds kinky =)
    /// </summary>
    public interface IPendingTransfersQueueService
    {
        /// <summary>
        /// Adds an item to this queue for the current tenant
        /// </summary>
        public void EnqueueSender();

        public IEnumerable<PendingTransferQueueItem> GetSenders();
    }

    public class PendingTransfersQueueService : DotYouServiceBase, IPendingTransfersQueueService
    {
        private readonly Queue<PendingTransferQueueItem> _queue;
        public PendingTransfersQueueService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
            _queue = new Queue<PendingTransferQueueItem>();
        }

        public void EnqueueSender()
        {
            this._queue.Enqueue(new PendingTransferQueueItem()
            {
                Sender = this.Context.HostDotYouId, 
                AddedTimeStamp = DateTimeExtensions.UnixTimeMilliseconds()
            });
        }

        public IEnumerable<PendingTransferQueueItem> GetSenders()
        {
            return this._queue.ToArray();
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