using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Tests
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