﻿using System;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
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
using Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Connections.t;
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

            // Connections = new CircleNetworkRequestsApiClient(ownerApi, identity);
            Connections = new UniversalCircleNetworkRequestsApiClient(identity.OdinId, factory);

            AccountManagement = new OwnerAccountManagementApiClient(ownerApi, identity);
        }

        public OwnerAuthTokenContext GetTokenContext()
        {
            var t = this._ownerApi.GetOwnerAuthContext(_identity.OdinId).ConfigureAwait(false).GetAwaiter().GetResult();
            return t;
        }

        public TestIdentity Identity => _identity;

        public OdinId OdinId => _identity.OdinId;

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

        // public CircleNetworkApiClient Network { get; }
        public UniversalCircleNetworkApiClient Network { get; }

        public UniversalCircleNetworkRequestsApiClient Connections { get; }

        public OwnerAccountManagementApiClient AccountManagement { get; }

        public YouAuthDomainApiClient YouAuth { get; }
    }
}