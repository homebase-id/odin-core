using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;

namespace Youverse.Hosting.Tests.AppAPI
{
    public class TestSampleAppContext
    {
        public DotYouIdentity Identity { get; set; }
        public Guid AppId { get; set; }
        public DotYouAuthenticationResult AuthResult { get; set; }
        public byte[] AppSharedSecretKey { get; set; }
        
        public Guid DefaultDrivePublicId { get; set; }
    }
}