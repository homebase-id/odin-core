using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Tests
{
    public class TestSampleAppContext
    {
        public DotYouIdentity Identity { get; set; }
        public Guid AppId { get; set; }
        public ClientAuthenticationToken ClientAuthenticationToken { get; set; }
        public byte[] SharedSecret { get; set; }
        
        public TargetDrive TargetDrive { get; set; }
    }
}