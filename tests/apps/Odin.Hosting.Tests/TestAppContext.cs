using System;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests
{
    public class TestAppContext
    {
        public OdinId Identity { get; set; }
        
        public Guid AppId { get; set; }
        public ClientAuthenticationToken ClientAuthenticationToken { get; set; }
        public byte[] SharedSecret { get; set; }
        
        /// <summary>
        /// Data used when the identity using this app sends connection requests
        /// </summary>
        public ContactRequestData ContactData { get; set; }
        
        public TargetDrive TargetDrive { get; set; }
    }
}