using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Apps
{
    public class RedactedAppRegistration
    {
        public GuidId AppId { get; set; }

        public string Name { get; set; }

        public bool IsRevoked { get; set; }

        public UnixTimeUtc Created { get; set; }

        public UnixTimeUtc Modified { get; set; }

        public RedactedExchangeGrant Grant { get; set; }
        public List<Guid> AuthorizedCircles { get; set; }
        public PermissionSetGrantRequest CircleMemberPermissionSetGrantRequest { get; set; }
        public string CorsHostName { get; set; }
    }
}