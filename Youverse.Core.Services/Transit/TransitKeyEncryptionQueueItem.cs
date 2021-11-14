using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit
{
    public class TransitKeyEncryptionQueueItem
    {
        public string AppId { get; set; }
        public Guid FileId { get; set; }
        public DotYouIdentity Recipient { get; set; }
        public int Attempts { get; set; }
        public UInt64 LastAttemptTimestampMs { get; set; }
        public UInt64 FirstAddedTimestampMs { get; set; }
    }
}