using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Time;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer
{
    /// <summary>
    /// A transfer item that needs to be encrypted. 
    /// </summary>
    public class PeerKeyEncryptionQueueItem
    {
        public GuidId Id { get; set; }
        public Guid FileId { get; set; }
        public OdinId Recipient { get; set; }
        public int Attempts { get; set; }
        public UnixTimeUtc LastAttemptTimestampMs { get; set; }
        public UnixTimeUtc FirstAddedTimestampMs { get; set; }
    }
}