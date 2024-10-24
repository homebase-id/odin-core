using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Follower;
using Odin.Hosting.Tests._Universal.ApiClient.Notifications;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.AppNotifications;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Direct;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Query;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests._Universal.ApiClient.App
{
    /// <summary>
    /// Api client for working with the /api/apps endpoints
    /// </summary>
    public class AppApiClientRedux
    {
        private readonly OdinId _identity;

        public AppApiClientRedux(OdinId identity, ClientAuthenticationToken cat, byte[] sharedSecret)
        {
            _identity = identity;

            //create an app client

            var factory = new AppApiClientFactory(cat, sharedSecret);

            PeerAppNotification = new UniversalPeerAppNotificationApiClient(identity, factory);

            DriveRedux = new UniversalDriveApiClient(identity, factory);
            PeerQuery = new UniversalPeerQueryApiClient(identity, factory);
            PeerDirect = new UniversalPeerDirectApiClient(identity, factory);

            StaticFilePublisher = new UniversalStaticFileApiClient(identity, factory);

            Follower = new UniversalFollowerApiClient(identity, factory);
            Reactions = new UniversalDriveReactionClient(identity, factory);

            //TODO: rename to universal client
            AppNotifications = new AppNotificationsApiClient(identity, factory);

            //TODO convert to universal
            // Connections = new CircleNetworkRequestsApiClient(ownerApi, identity);
        }

        public OdinId OdinId => _identity;

        public UniversalPeerAppNotificationApiClient PeerAppNotification { get; }

        public UniversalFollowerApiClient Follower { get; }

        public UniversalDriveReactionClient Reactions { get; }

        public UniversalPeerQueryApiClient PeerQuery { get; }

        public UniversalPeerDirectApiClient PeerDirect { get; }

        public AppNotificationsApiClient AppNotifications { get; }

        public UniversalDriveApiClient DriveRedux { get; }

        public UniversalStaticFileApiClient StaticFilePublisher { get; }
    }
}