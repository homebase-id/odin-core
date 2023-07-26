﻿using System;
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
        /// List of circles defining whose members can work with your identity via this app
        /// </summary>
        public List<Guid> AuthorizedCircles { get; set; }
        
        /// <summary>
        /// Permissions granted to members of the <see cref="AuthorizedCircles"/>
        /// </summary>
        public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }
        
        /// <summary>
        /// Permissions and drives granted to this app and only this app as used by the Identity Owner
        /// </summary>
        public ExchangeGrant Grant { get; set; }

        public string CorsHostName { get; set; }
        
        public ConsentRequirement DeviceRegistrationConsentRequirement { get; set; }

        public RedactedYouAuthDomainRegistration Redacted()
        {
            //NOTE: we're not sharing the encrypted app dek, this is crucial
            return new RedactedYouAuthDomainRegistration()
            {
                Domain = this.Domain,
                Name = this.Name,
                IsRevoked = this.Grant.IsRevoked,
                Created = this.Grant.Created,
                AuthorizedCircles = this.AuthorizedCircles,
                CircleMemberPermissionSetGrantRequest = this.CircleMemberPermissionGrant,
                Modified = this.Grant.Modified,
                CorsHostName = this.CorsHostName,
                Grant = this.Grant.Redacted()
            };
        }
    }
}