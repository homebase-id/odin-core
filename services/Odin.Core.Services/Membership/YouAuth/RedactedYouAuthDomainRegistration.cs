using System;
using System.Collections.Generic;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Time;

namespace Odin.Core.Services.Membership.YouAuth
{
    public class RedactedYouAuthDomainRegistration
    {
        public string Domain { get; set; }

        public string Name { get; set; }

        public bool IsRevoked { get; set; }

        public Int64 Created { get; set; }

        public Int64 Modified { get; set; }
        
        public string CorsHostName { get; set; }
        
        public List<RedactedCircleGrant> CircleGrants { get; set; }
        
        public ConsentRequirements ConsentRequirements { get; set; }
    }
}