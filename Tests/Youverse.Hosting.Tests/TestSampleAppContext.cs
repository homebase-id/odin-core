using System;
using Youverse.Core.Services.Authentication;

namespace Youverse.Hosting.Tests.AppAPI
{
    public class TestSampleAppContext
    {
        public Guid AppId { get; set; }
        public DotYouAuthenticationResult AuthResult { get; set; }
        public byte[] AppSharedSecretKey { get; set; }
    }
}