using System;
using System.Collections.Generic;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Util;

namespace Odin.Core.Services.Authorization.YouAuth
{
    public class YouAuthDomainRegistration
    {
        public AsciiDomainName Domain { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// Permissions and drives granted to this app and only this app as used by the Identity Owner
        /// </summary>
        public ExchangeGrant Grant { get; set; }

        public string CorsHostName { get; set; }
        
        public ConsentRequirement DeviceRegistrationConsentRequirement { get; set; }

        public RedactedYouAuthDomainRegistration Redacted()
        {
            //NOTE: we do not share critcal information like encryption keys
            return new RedactedYouAuthDomainRegistration()
            {
                Domain = this.Domain.DomainName,
                Name = this.Name,
                IsRevoked = this.Grant.IsRevoked,
                Created = this.Grant.Created,
                Modified = this.Grant.Modified,
                CorsHostName = this.CorsHostName,
                Grant = this.Grant.Redacted()
            };
        }
    }
}