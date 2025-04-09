using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Membership.YouAuth
{
    public class YouAuthDomainRegistration
    {
        public AsciiDomainName Domain { get; set; }

        public string Name { get; set; }
        
        public bool IsRevoked { get; set; }

        public UnixTimeUtc Created { get; set; }

        public UnixTimeUtc Modified { get; set; }
        
        public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }
        
        /// <summary>
        /// The permissions granted from a given circle.  The key is the circle Id.
        /// </summary>
        public Dictionary<Guid, CircleGrant> CircleGrants { get; set; }

        public string CorsHostName { get; set; }

        public ConsentRequirements ConsentRequirements { get; set; }
        

        public RedactedYouAuthDomainRegistration Redacted()
        {
            //NOTE: we do not share critical information like encryption keys
            return new RedactedYouAuthDomainRegistration()
            {
                Domain = this.Domain.DomainName,
                Name = this.Name,
                IsRevoked = this.IsRevoked,
                Created = this.Created,
                Modified = this.Modified,
                CorsHostName = this.CorsHostName,
                CircleGrants = this.CircleGrants.Values.Select(cg => cg.Redacted()).ToList(),
                ConsentRequirements = this.ConsentRequirements
            };
        }
    }
}