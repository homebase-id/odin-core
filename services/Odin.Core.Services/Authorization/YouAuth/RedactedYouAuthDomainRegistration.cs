using System;
using System.Collections.Generic;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Util;

namespace Odin.Core.Services.Authorization.YouAuth
{
    public class RedactedYouAuthDomainRegistration
    {
        public AsciiDomainName Domain { get; set; }

        public string Name { get; set; }

        public bool IsRevoked { get; set; }

        public Int64 Created { get; set; }

        public Int64 Modified { get; set; }

        public RedactedExchangeGrant Grant { get; set; }
        public List<Guid> AuthorizedCircles { get; set; }
        public PermissionSetGrantRequest CircleMemberPermissionSetGrantRequest { get; set; }
        public string CorsHostName { get; set; }
    }
}