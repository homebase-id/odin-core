using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Services.Peer.Incoming.Drive.Reactions.Inbox
{
    public class PeerReactionInbox
    {
        private readonly byte[] _queueDataType = Guid.Parse("8cef58cc-6b35-442e-94d8-1caf0991d8ce").ToByteArray();

        //Note: the data type will be the global transit Id, this allows us to save multiple as they come
        // we can then later query the datatype by this global transit id
        private readonly TwoKeyValueStorage _queueStorage;

        public PeerReactionInbox(TenantSystemStorage tenantSystemStorage)
        {
            const string reactionQueueContextKey = "6cbdccf6-4912-4472-9e35-6077038c3fc4";
            _queueStorage = tenantSystemStorage.CreateTwoKeyValueStorage(Guid.Parse(reactionQueueContextKey));
        }

        public Task EnqueueAddReaction(GlobalTransitIdFileIdentifier file, SharedSecretEncryptedTransitPayload payload, AddRemoteReactionRequest request)
        {
            var item = new PeerReactionQueueItem
            {
                File = file,
                Payload = payload
            };

            var key = ByteArrayUtil.ReduceSHA256Hash(request.Reaction);
            _queueStorage.Upsert(key, file.GlobalTransitId.ToByteArray(), item);
            return Task.CompletedTask;
        }

        public Task EnqueueDeleteReaction(GlobalTransitIdFileIdentifier file, SharedSecretEncryptedTransitPayload payload,
            DeleteReactionRequestByGlobalTransitId request)
        {
            var item = new PeerReactionQueueItem
            {
                File = file,
                Payload = payload
            };

            var key = ByteArrayUtil.ReduceSHA256Hash(request.Reaction);
            _queueStorage.Delete(key);
            return Task.CompletedTask;
        }

        public Task<List<PeerReactionQueueItem>> GetItems(Guid globalTransitId)
        {
            var items = _queueStorage.GetByDataType<PeerReactionQueueItem>(globalTransitId.ToByteArray());
            return Task.FromResult(items.ToList());
        }
    }
}