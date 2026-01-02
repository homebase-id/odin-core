using System;
using System.Diagnostics;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.DataConversion;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Follower;
using Odin.Hosting.Tests._Universal.ApiClient.Notifications;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.AccountManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.AppManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Direct;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Query;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Version;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner
{
    /// <summary>
    /// Owner API client v2 - transitioning to this one
    /// </summary>
    [DebuggerDisplay("Owner client for {Identity.OdinId}")]
    public class OwnerApiClientRedux
    {
        private readonly TestIdentity _identity;
        private readonly OwnerApiTestUtils _ownerApi;

        public OwnerApiClientRedux(OwnerApiTestUtils ownerApi, TestIdentity identity, Guid? systemApiKey = null)
        {
            _ownerApi = ownerApi;
            _identity = identity;

            var t = ownerApi.GetOwnerAuthContext(identity.OdinId);
            var factory = new OwnerApiClientFactory(t.AuthenticationResult, t.SharedSecret.GetKey());

            Security = new SecurityApiClient(ownerApi, identity);
            AppManager = new AppManagementApiClient(ownerApi, identity);
            DriveManager = new DriveManagementApiClient(ownerApi, identity);
            Configuration = new OwnerConfigurationApiClient(ownerApi, identity);

            // Network = new CircleNetworkApiClient(ownerApi, identity);
            Network = new(identity.OdinId, factory);
            YouAuth = new YouAuthDomainApiClient(ownerApi, identity);

            DriveRedux = new UniversalDriveApiClient(identity.OdinId, factory);
            PeerQuery = new UniversalPeerQueryApiClient(identity.OdinId, factory);
            PeerDirect = new UniversalPeerDirectApiClient(identity.OdinId, factory);

            StaticFilePublisher = new UniversalStaticFileApiClient(identity.OdinId, factory);

            Follower = new UniversalFollowerApiClient(identity.OdinId, factory);
            Reactions = new UniversalDriveReactionClient(identity.OdinId, factory);

            AppNotifications = new AppNotificationsApiClient(identity.OdinId, factory);

            Connections = new UniversalCircleNetworkRequestsApiClient(identity.OdinId, factory);

            AccountManagement = new OwnerAccountManagementApiClient(ownerApi, identity);

            DataConversion = new UniversalDataConversionApiClient(identity.OdinId, factory);
            
            VersionClient = new VersionApiClient(ownerApi, identity);
        }

        public OwnerAuthTokenContext GetTokenContext()
        {
            var t = this._ownerApi.GetOwnerAuthContext(_identity.OdinId);
            return t;
        }

        public TestIdentity Identity => _identity;

        public OdinId OdinId => _identity.OdinId;

        public SecurityApiClient Security { get; }

        public UniversalFollowerApiClient Follower { get; }

        public UniversalDriveReactionClient Reactions { get; }

        public UniversalPeerQueryApiClient PeerQuery { get; }

        public UniversalPeerDirectApiClient PeerDirect { get; }

        public AppManagementApiClient AppManager { get; }

        public AppNotificationsApiClient AppNotifications { get; }

        public DriveManagementApiClient DriveManager { get; }

        public UniversalDriveApiClient DriveRedux { get; }

        public UniversalStaticFileApiClient StaticFilePublisher { get; }

        public OwnerConfigurationApiClient Configuration { get; }

        public UniversalCircleNetworkApiClient Network { get; }

        public UniversalCircleNetworkRequestsApiClient Connections { get; }

        public OwnerAccountManagementApiClient AccountManagement { get; }

        public YouAuthDomainApiClient YouAuth { get; }

        public UniversalDataConversionApiClient DataConversion { get; }
        
        public VersionApiClient VersionClient { get; }
    }
}