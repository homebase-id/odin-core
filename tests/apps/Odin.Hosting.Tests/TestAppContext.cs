using System;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections.Requests;

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