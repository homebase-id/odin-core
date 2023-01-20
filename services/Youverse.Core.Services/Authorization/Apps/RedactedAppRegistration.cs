using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class RedactedAppRegistration
    {
        public GuidId AppId { get; set; }

        public string Name { get; set; }

        public bool IsRevoked { get; set; }

        public UInt64 Created { get; set; }

        public UInt64 Modified { get; set; }

        public RedactedExchangeGrant Grant { get; set; }
        public List<Guid> AuthorizedCircles { get; set; }
        public RedactedExchangeGrant CircleMemberGrant { get; set; }
    }
}