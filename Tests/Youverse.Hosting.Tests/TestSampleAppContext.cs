using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Tests.AppAPI
{
    public class TestSampleAppContext
    {
        public DotYouIdentity Identity { get; set; }
        public Guid AppId { get; set; }
        public ClientAuthenticationToken ClientAuthenticationToken { get; set; }
        public byte[] AppSharedSecretKey { get; set; }
        
        public TargetDrive TargetDrive { get; set; }
    }
}