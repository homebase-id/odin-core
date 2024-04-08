using System;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Drive.Reaction;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Follower;
using Odin.Hosting.Tests._Universal.ApiClient.Notifications;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.AccountManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.AppManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.CircleMembership;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Cron;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Query;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
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

        public OwnerApiClientRedux(OwnerApiTestUtils ownerApi, TestIdentity identity, Guid? systemApiKey = null)
        {
            _ownerApi = ownerApi;
            _identity = identity;

            var t = ownerApi.GetOwnerAuthContext(identity.OdinId).GetAwaiter().GetResult();
            var factory = new OwnerApiClientFactory(t.AuthenticationResult, t.SharedSecret.GetKey());

            AppManager = new AppManagementApiClient(ownerApi, identity);
            DriveManager = new DriveManagementApiClient(ownerApi, identity);
            Configuration = new OwnerConfigurationApiClient(ownerApi, identity);

            Network = new CircleNetworkApiClient(ownerApi, identity);
            YouAuth = new YouAuthDomainApiClient(ownerApi, identity);

            DriveRedux = new UniversalDriveApiClient(identity.OdinId, factory);
            PeerQuery = new UniversalPeerQueryApiClient(identity.OdinId, factory);

            StaticFilePublisher = new UniversalStaticFileApiClient(identity.OdinId, factory);

            Follower = new UniversalFollowerApiClient(identity.OdinId, factory);
            Reactions = new UniversalLocalDriveReactionClient(identity.OdinId, factory);

            AppNotifications = new AppNotificationsApiClient(identity.OdinId, factory);

            Cron = new CronApiClient(ownerApi, identity);
            Connections = new CircleNetworkRequestsApiClient(ownerApi, identity);

            AccountManagement = new OwnerAccountManagementApiClient(ownerApi, identity);
        }

        public OwnerAuthTokenContext GetTokenContext()
        {
            var t = this._ownerApi.GetOwnerAuthContext(_identity.OdinId).ConfigureAwait(false).GetAwaiter().GetResult();
            return t;
        }

        public TestIdentity Identity => _identity;

        public CronApiClient Cron { get; }

        public UniversalFollowerApiClient Follower { get; }

        public UniversalLocalDriveReactionClient Reactions { get; }

        public UniversalPeerQueryApiClient PeerQuery { get; }

        public AppManagementApiClient AppManager { get; }

        public AppNotificationsApiClient AppNotifications { get; }

        public DriveManagementApiClient DriveManager { get; }

        public UniversalDriveApiClient DriveRedux { get; }

        public UniversalStaticFileApiClient StaticFilePublisher { get; }

        public OwnerConfigurationApiClient Configuration { get; }

        public CircleNetworkApiClient Network { get; }

        public CircleNetworkRequestsApiClient Connections { get; }

        public OwnerAccountManagementApiClient AccountManagement { get; }

        public YouAuthDomainApiClient YouAuth { get; }
    }
}