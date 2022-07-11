using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// A transfer item that needs to be encrypted. 
    /// </summary>
    public class TransitKeyEncryptionQueueItem
    {
        public Guid FileId { get; set; }
        public DotYouIdentity Recipient { get; set; }
        public int Attempts { get; set; }
        public UInt64 LastAttemptTimestampMs { get; set; }
        public UInt64 FirstAddedTimestampMs { get; set; }
    }
}