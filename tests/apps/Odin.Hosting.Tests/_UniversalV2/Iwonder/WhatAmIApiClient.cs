using System;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests._UniversalV2.Iwonder
{
    public class WhatAmIApiClient
    {
        private readonly TestIdentity _identity;
        private readonly OwnerApiTestUtils _ownerApi;

        public WhatAmIApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity, Guid? systemApiKey = null)
        {
            _ownerApi = ownerApi;
            _identity = identity;

            var t = ownerApi.GetOwnerAuthContext(identity.OdinId).GetAwaiter().GetResult();
            var factory = new OwnerApiClientFactory(t.AuthenticationResult, t.SharedSecret.GetKey());

            DriveRedux = new UniversalDriveApiClient(identity.OdinId, factory);
        }

        public OwnerAuthTokenContext GetTokenContext()
        {
            var t = this._ownerApi.GetOwnerAuthContext(_identity.OdinId).ConfigureAwait(false).GetAwaiter().GetResult();
            return t;
        }

        public TestIdentity Identity => _identity;

        public OdinId OdinId => Identity.OdinId;

        public UniversalDriveApiClient DriveRedux { get; }

    }
}