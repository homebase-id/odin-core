using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class RedactedAppRegistration
    {
        public GuidId AppId { get; set; }

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