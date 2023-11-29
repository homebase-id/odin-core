using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Configuration;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Notifications;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.AppManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
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

        private readonly OwnerConfigurationApiClient _ownerConfigurationApiClient;
        private readonly AppManagementApiClient _appManagerApiClient;
        private readonly CircleMembershipApiClient _circleMembershipApiClient;
        private readonly YouAuthDomainApiClient _youAuthDomainApiClient;
        private readonly UniversalDriveApiClient _driveApiClientRedux;
        private readonly AppNotificationsApiClient _appNotificationsApi;

        public OwnerApiClientRedux(OwnerApiTestUtils ownerApi, TestIdentity identity, Guid? systemApiKey = null)
        {
            _ownerApi = ownerApi;
            _identity = identity;

            var t = ownerApi.GetOwnerAuthContext(identity.OdinId).GetAwaiter().GetResult();
            var factory = new OwnerApiClientFactory(t.AuthenticationResult, t.SharedSecret.GetKey());

            _appManagerApiClient = new AppManagementApiClient(ownerApi, identity);
            _driveManagementApiClient = new DriveManagementApiClient(ownerApi, identity);
            _ownerConfigurationApiClient = new OwnerConfigurationApiClient(ownerApi, identity);

            _circleMembershipApiClient = new CircleMembershipApiClient(ownerApi, identity);
            _youAuthDomainApiClient = new YouAuthDomainApiClient(ownerApi, identity);

            _driveApiClientRedux = new UniversalDriveApiClient(identity.OdinId, factory);
            _appNotificationsApi = new AppNotificationsApiClient(identity.OdinId, factory);
        }

        public OwnerAuthTokenContext GetTokenContext()
        {
            var t = this._ownerApi.GetOwnerAuthContext(_identity.OdinId).ConfigureAwait(false).GetAwaiter().GetResult();
            return t;
        }

        public TestIdentity Identity => _identity;

        public AppManagementApiClient AppManager => _appManagerApiClient;

        public AppNotificationsApiClient AppNotifications => _appNotificationsApi;
        
        public DriveManagementApiClient DriveManager => _driveManagementApiClient;

        public UniversalDriveApiClient DriveRedux => _driveApiClientRedux;

        public OwnerConfigurationApiClient Configuration => _ownerConfigurationApiClient;

        public CircleMembershipApiClient Membership => _circleMembershipApiClient;

        public YouAuthDomainApiClient YouAuth => _youAuthDomainApiClient;
    }
}