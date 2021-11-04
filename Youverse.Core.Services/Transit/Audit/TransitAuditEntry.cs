using System;
using Dawn;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit.Audit
{
    /// <summary>
    /// 
    /// </summary>
    public class TransitAuditEntry
    {
        
        //TODO: revisit how to get litedb serialization working correctly with these strict ctors
        // public TransitAuditEntry(Guid id)
        // {
        //     Guard.Argument(id, nameof(id)).NotEqual(Guid.Empty);
        //
        //     this.Id = id;
        //     this.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // }
        
        public Guid Id { get; init; }
        
        public DotYouIdentity Sender { get; set; }
        
        public Int64 Timestamp { get; set; }

        public int EventId { get; set; }

        public Guid? FilterId { get; set; }

        public FilterAction? FilterRecommendation { get; set; }
    }
}