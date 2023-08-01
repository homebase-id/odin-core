using System;
using Odin.Core.Services.Authorization.ExchangeGrants;

namespace Odin.Core.Services.Authorization.YouAuth
{
    public class RedactedYouAuthDomainRegistration
    {
        public string Domain { get; set; }

        public string Name { get; set; }

        public bool IsRevoked { get; set; }

        public Int64 Created { get; set; }

        public Int64 Modified { get; set; }
        
        public string CorsHostName { get; set; }
        
        public RedactedExchangeGrant Grant { get; set; }

    }
}