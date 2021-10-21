using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Services that manages items in a given Tenant's outbox
    /// </summary>
    public class OutboxQueueService: DotYouServiceBase, IOutboxQueueService
    {
        private readonly Queue<TransferQueueItem> _queue;
        
        public OutboxQueueService(DotYouContext context, ILogger logger, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) : base(context, logger, notificationHub, fac)
        {
            _queue = new Queue<TransferQueueItem>();
        }

        /// <summary>
        /// Adds an item to be encrypted and moved to the outbox
        /// </summary>
        /// <param name="item"></param>
        public void Enqueue(TransferQueueItem item)
        {
            if (!_queue.Contains(item))
            {
                _queue.Enqueue(item);
            }
        }

        /// <summary>
        /// Add and item back the queue due to a failure
        /// </summary>
        public void EnqueueFailure(TransferQueueItem item, TransferFailureReason reason)
        {
            //TODO: check all other fields on the item;
            
            item.Attempts.Add(new TransferAttempt()
            {
                TransferFailureReason = reason,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            
            //TODO:this puts it at the end of the queue however we need to decide if we want to push it forward for various reasons (i.e. it's a chat message, etc.)
            Enqueue(item);
        }

        public IEnumerable<TransferQueueItem> GetNextBatch()
        {
            //TODO: add in batch processing
            var item = _queue.Dequeue();
            if (null != item)
            {
                return new List<TransferQueueItem>(new[] { item }).AsEnumerable();
            }
    
            return Array.Empty<TransferQueueItem>();
        }

        /// <summary>
        /// Gets all the items currently in the queue w/o making changes to it 
        /// </summary>
        /// <returns></returns>
        public IEnumerable<TransferQueueItem> GetPendingItems()
        {
            return _queue.ToArray();
        }
    }
}