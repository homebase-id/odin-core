﻿using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;

namespace Youverse.Hosting.Tests.AppAPI
{
    public class TestSampleAppContext
    {
        public DotYouIdentity Identity { get; set; }
        public Guid AppId { get; set; }
        public ClientAuthToken ClientAuthToken { get; set; }
        public byte[] AppSharedSecretKey { get; set; }
        
        public Guid DriveAlias { get; set; }
    }
}