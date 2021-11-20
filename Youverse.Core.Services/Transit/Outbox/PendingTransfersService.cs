using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Outbox
{
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
}