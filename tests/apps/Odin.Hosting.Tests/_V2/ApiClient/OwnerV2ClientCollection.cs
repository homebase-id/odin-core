using System;
using System.Diagnostics;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Odin.Services.Authentication.Owner;

namespace Odin.Hosting.Tests._V2.ApiClient
{
    /// <summary>
    /// Collection of clients which run as the owner.  Useful for Test setup and tear down
    /// </summary>
    [DebuggerDisplay("V2 Client context for {Identity.OdinId}")]
    public class OwnerV2ClientCollection
    {
        private readonly TestIdentity _identity;
        private readonly OwnerApiTestUtils _ownerApi;

        public OwnerV2ClientCollection(OwnerApiTestUtils ownerApi, TestIdentity identity)
        {
            _ownerApi = ownerApi;
            _identity = identity;

            var t = ownerApi.GetOwnerAuthContext(identity.OdinId);
            var factory = new ApiClientFactoryV2(OwnerAuthConstants.CookieName, t.AuthenticationResult, t.SharedSecret.GetKey());
            DriveWriter = new DriveWriterV2Client(identity.OdinId, factory);
            DriveReader = new DriveReaderV2Client(identity.OdinId, factory);
        }

        public OwnerAuthTokenContext GetTokenContext()
        {
            var t = this._ownerApi.GetOwnerAuthContext(_identity.OdinId);
            return t;
        }

        public TestIdentity Identity => _identity;

        public OdinId OdinId => _identity.OdinId;

        public DriveReaderV2Client DriveReader { get; }
        public DriveWriterV2Client DriveWriter { get; }
        
    }
}