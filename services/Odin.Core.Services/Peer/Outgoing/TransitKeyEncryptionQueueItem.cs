using System;
using Odin.Core.Identity;

namespace Odin.Core.Services.Peer.Outgoing
{
    /// <summary>
    /// A transfer item that needs to be encrypted. 
    /// </summary>
    public class TransitKeyEncryptionQueueItem
    {
        public GuidId Id { get; set; }
        public Guid FileId { get; set; }
        public OdinId Recipient { get; set; }
        public int Attempts { get; set; }
        public Int64 LastAttemptTimestampMs { get; set; }
        public Int64 FirstAddedTimestampMs { get; set; }
    }
}