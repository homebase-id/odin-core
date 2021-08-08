using System;

namespace DotYou.Types.Admin
{
    public class AvailabilityStatus
    {
        public HumanConnectionProfile HumanConnectionProfile { get; set; }
        
        public Int64 Updated { get; set; }

        public bool IsChatAvailable { get; set; }
        
    }
}