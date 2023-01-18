using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistration
    {
        public AppRegistration()
        {
        }

        public GuidId AppId { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// List of circles defining whose members can work with your identity via this app
        /// </summary>
        public List<Guid> AuthorizedCircles { get; set; }

        /// <summary>
        /// Permissions and drives granted to this app
        /// </summary>
        public ExchangeGrant Grant { get; set; }

        public RedactedAppRegistration Redacted()
        {
            //NOTE: we're not sharing the encrypted app dek, this is crucial
            return new RedactedAppRegistration()
            {
                AppId = this.AppId,
                Name = this.Name,
                IsRevoked = this.Grant.IsRevoked,
                Created = this.Grant.Created,
                AuthorizedCircles = this.AuthorizedCircles,
                Modified = this.Grant.Modified,
                Grant = this.Grant.Redacted()
            };
        }
    }
}