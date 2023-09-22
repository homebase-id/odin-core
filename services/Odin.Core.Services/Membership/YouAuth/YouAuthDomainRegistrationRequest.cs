using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Core.Services.Membership.YouAuth
{
    public class YouAuthDomainRegistrationRequest
    {
        public string Domain { get; set; }

        public string Name { get; set; }
        
        /// <summary>
        /// The host name used for CORS to allow the app to access the identity from a browser
        /// </summary>
        public string CorsHostName { get; set; }
        
        /// <summary>
        /// The circles to be granted to the domain
        /// </summary>
        public List<GuidId> CircleIds { get; set; }
        
        public ConsentRequirement ConsentRequirement { get; set; }
        
        /// <summary>
        /// Some time in the future for consent to expire
        /// </summary>
        public UnixTimeUtc ConsentExpirationDateTime { get; set; }
        
    }
}