using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Membership.YouAuth
{
    public class RedactedYouAuthDomainRegistration
    {
        public string Domain { get; set; }

        public string Name { get; set; }

        public bool IsRevoked { get; set; }

        public UnixTimeUtc Created { get; set; }

        public UnixTimeUtc Modified { get; set; }
        
        public string CorsHostName { get; set; }
        
        public List<RedactedCircleGrant> CircleGrants { get; set; }
        
        public ConsentRequirements ConsentRequirements { get; set; }
    }
}