using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Configuration;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Cron;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Follower;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.CircleMembership;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Rsa;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit.Query;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner
{
    /// <summary>
    /// Owner API client v2 - transitioning to this one
    /// </summary>
    public class OwnerApiClientRedux
    {
        private readonly TestIdentity _identity;
        private readonly OwnerApiTestUtils _ownerApi;

        private readonly DriveManagementApiClient _driveManagementApiClient;

        private readonly DriveApiClient _driveApiClient;
        private readonly DriveApiClientRedux _driveApiClientRedux;
        private readonly OwnerConfigurationApiClient _ownerConfigurationApiClient;

        public OwnerApiClientRedux(OwnerApiTestUtils ownerApi, TestIdentity identity, Guid? systemApiKey = null)
        {
            _ownerApi = ownerApi;
            _identity = identity;

            _driveManagementApiClient = new DriveManagementApiClient(ownerApi, identity);
            _ownerConfigurationApiClient = new OwnerConfigurationApiClient(ownerApi, identity);
        }

        public OwnerAuthTokenContext GetTokenContext()
        {
            var t = this._ownerApi.GetOwnerAuthContext(_identity.OdinId).ConfigureAwait(false).GetAwaiter().GetResult();
            return t;
        }

        public TestIdentity Identity => _identity;

        public DriveManagementApiClient DriveManager => _driveManagementApiClient;

        public OwnerConfigurationApiClient Configuration => _ownerConfigurationApiClient;
    }
}