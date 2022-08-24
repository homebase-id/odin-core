using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Storage;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Transit.Outbox
{
    public class PendingTransfersService : IPendingTransfersService
    {
        private class PendingTransferItem : DotYouIdBase
        {
        }

        private readonly ILogger<IPendingTransfersService> _logger;

        private readonly string _dataPath;
        private const string PendingTransferCollection = "ptc";


        public PendingTransfersService(ILogger<IPendingTransfersService> logger)
        {
            //TODO: get from injection?
            _dataPath = PathUtil.OsIfy("\\tmp\\dotyou\\system\\");
            _logger = logger;
        }

        public void EnsureSenderIsPending(DotYouIdentity sender)
        {
            using (var storage = new LiteDBSingleCollectionStorage<PendingTransferItem>(_logger, _dataPath, PendingTransferCollection))
            {
                storage.Save(new PendingTransferItem() { DotYouId = sender });
            }
            // _logger.LogInformation($"Added sender [{sender}] to the Pending Transfer Queue");
        }

        public async Task<IEnumerable<DotYouIdentity>> GetSenders()
        {
            
            using var storage = new LiteDBSingleCollectionStorage<PendingTransferItem>(_logger, _dataPath, PendingTransferCollection);
            DotYouIdentity[] senders = (await storage.GetList(new PageOptions(1, 1000))).Results.Select(p => p.DotYouId).ToArray();
            await storage.DeleteAll();
            return senders;
        }
    }
}