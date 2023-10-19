using System;
using System.Net.Http;
using Odin.Core.Storage;

namespace Odin.Hosting.Tests.Anonymous.ApiClient
{
    public class AnonymousApiClient
    {
        private readonly TestIdentity _identity;

        public AnonymousApiClient(TestIdentity identity)
        {
            _identity = identity;
        }

        public TestIdentity Identity => _identity;

        
    }
}