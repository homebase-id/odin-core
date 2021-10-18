using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Identity
{
    public class AvailabilityStatus
    {
        public DotYouIdentity DotYouId { get; set; }
        
        public string DisplayName { get; set; }
        
        public string StatusMessage { get; set; }
        
        public Int64 Updated { get; set; }

        public bool IsChatAvailable { get; set; }
        
    }
}