using Autofac;
using MediatR;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.Data;
using Odin.Services.AppNotifications.Push;
using Odin.Services.AppNotifications.SystemNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.Transit;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.DataSubscription;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.Comment.Attachments;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.FileSystem.Standard.Attachments;
using Odin.Services.Drives.Management;
using Odin.Services.Drives.Reactions;
using Odin.Services.Drives.Statistics;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Mediator;
using Odin.Services.Mediator.Owner;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Membership.YouAuth;
using Odin.Services.Optimization.Cdn;
using Odin.Services.Peer.Incoming.Drive.Reactions;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Tenant;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Services.Background;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.LinkMetaExtractor;
using Odin.Services.Peer.Incoming.Drive.Reactions.Group;

namespace Odin.Hosting
{
    /// <summary>
    /// Set up per-tenant services
    /// </summary>
    public static class TenantServices
    {
        internal static void ConfigureMultiTenantServices(ContainerBuilder cb, Tenant tenant)
        {
            cb.RegisterType<TenantSystemStorage>().AsSelf().SingleInstance();

            cb.RegisterType<NotificationListService>().AsSelf().SingleInstance();

            cb.RegisterType<PushNotificationService>()
                .As<INotificationHandler<ConnectionRequestReceived>>()
                .As<INotificationHandler<ConnectionRequestAccepted>>()
                .AsSelf()
                .SingleInstance();
            
            cb.RegisterType<LinkMetaExtractor>().As<ILinkMetaExtractor>();

            cb.RegisterType<PushNotificationOutboxAdapter>()
                .As<INotificationHandler<PushNotificationEnqueuedNotification>>()
                .AsSelf().SingleInstance();

            cb.RegisterType<FeedNotificationMapper>()
                .As<INotificationHandler<ReactionContentAddedNotification>>()
                .As<INotificationHandler<NewFeedItemReceived>>()
                .As<INotificationHandler<NewFollowerNotification>>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .AsSelf()
                .SingleInstance();

            cb.RegisterType<AppNotificationHandler>()
                .As<INotificationHandler<FileAddedNotification>>()
                .As<INotificationHandler<ConnectionRequestReceived>>()
                .As<INotificationHandler<ConnectionRequestAccepted>>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<InboxItemReceivedNotification>>()
                .As<INotificationHandler<NewFollowerNotification>>()
                .As<INotificationHandler<ReactionContentAddedNotification>>()
                .As<INotificationHandler<ReactionContentDeletedNotification>>()
                .As<INotificationHandler<ReactionPreviewUpdatedNotification>>()
                .As<INotificationHandler<AppNotificationAddedNotification>>()
                .AsSelf()
                .SingleInstance();

            cb.RegisterType<TenantConfigService>().AsSelf().SingleInstance();
            cb.RegisterType<TenantContext>().AsSelf().SingleInstance();

            cb.RegisterType<OdinContext>().As<IOdinContext>().AsSelf().InstancePerLifetimeScope();
            cb.RegisterType<OdinHttpClientFactory>().As<IOdinHttpClientFactory>().SingleInstance();

            cb.RegisterType<HomeCachingService>()
                .AsSelf()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<DriveDefinitionAddedNotification>>()
                .SingleInstance();

            cb.RegisterType<HomeAuthenticatorService>()
                .AsSelf()
                .As<INotificationHandler<IdentityConnectionRegistrationChangedNotification>>()
                .SingleInstance();
            cb.RegisterType<HomeRegistrationStorage>().AsSelf().SingleInstance();

            cb.RegisterType<YouAuthUnifiedService>().As<IYouAuthUnifiedService>().SingleInstance();
            cb.RegisterType<YouAuthDomainRegistrationService>().AsSelf().SingleInstance();

            cb.RegisterType<RecoveryService>().AsSelf().SingleInstance();
            cb.RegisterType<OwnerSecretService>().AsSelf().SingleInstance();
            cb.RegisterType<OwnerAuthenticationService>()
                .AsSelf()
                .As<INotificationHandler<DriveDefinitionAddedNotification>>()
                .SingleInstance();

            cb.RegisterType<DriveManager>().AsSelf().SingleInstance();
            cb.RegisterType<DriveAclAuthorizationService>().As<IDriveAclAuthorizationService>().SingleInstance();

            cb.RegisterType<FileSystemResolver>().AsSelf().InstancePerDependency();
            cb.RegisterType<FileSystemHttpRequestResolver>().AsSelf().InstancePerDependency();

            cb.RegisterType<StandardFileStreamWriter>().AsSelf().InstancePerDependency();
            cb.RegisterType<StandardFilePayloadStreamWriter>().AsSelf().InstancePerDependency();
            cb.RegisterType<StandardFileDriveStorageService>().AsSelf().InstancePerDependency();
            cb.RegisterType<StandardFileDriveQueryService>().AsSelf().InstancePerDependency();

            cb.RegisterType<StandardFileSystem>().AsSelf().InstancePerDependency();

            cb.RegisterType<CommentStreamWriter>().AsSelf().InstancePerDependency();
            cb.RegisterType<CommentPayloadStreamWriter>().AsSelf().InstancePerDependency();
            cb.RegisterType<CommentFileStorageService>().AsSelf().InstancePerDependency();
            cb.RegisterType<CommentFileQueryService>().AsSelf().InstancePerDependency();
            cb.RegisterType<CommentFileSystem>().AsSelf().InstancePerDependency();

            cb.RegisterType<DriveDatabaseHost>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<ReactionPreviewUpdatedNotification>>()
                .AsSelf()
                .SingleInstance();

            cb.RegisterType<ReactionContentService>().AsSelf().SingleInstance();
            cb.RegisterType<GroupReactionService>().AsSelf().SingleInstance();
            
            cb.RegisterType<ReactionPreviewCalculator>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<ReactionContentAddedNotification>>()
                .As<INotificationHandler<ReactionContentDeletedNotification>>()
                .As<INotificationHandler<AllReactionsByFileDeleted>>();

            cb.RegisterType<AppRegistrationService>().As<IAppRegistrationService>().SingleInstance();

            cb.RegisterType<CircleMembershipService>().AsSelf().SingleInstance();
            cb.RegisterType<IcrKeyService>().AsSelf().SingleInstance();
            cb.RegisterType<CircleDefinitionService>().SingleInstance();
            cb.RegisterType<CircleNetworkService>()
                .AsSelf()
                .As<INotificationHandler<DriveDefinitionAddedNotification>>()
                .As<INotificationHandler<AppRegistrationChangedNotification>>()
                .SingleInstance();

            cb.RegisterType<CircleNetworkRequestService>().AsSelf().SingleInstance();

            cb.RegisterType<FollowerService>().SingleInstance();
            cb.RegisterType<FollowerPerimeterService>().SingleInstance();

            cb.RegisterType<PeerOutbox>().AsSelf().SingleInstance();

            cb.RegisterType<PeerInboxProcessor>().AsSelf()
                .As<INotificationHandler<RsaKeyRotatedNotification>>()
                .SingleInstance();

            cb.RegisterType<TransitAuthenticationService>()
                .As<INotificationHandler<IdentityConnectionRegistrationChangedNotification>>()
                .AsSelf()
                .SingleInstance();

            cb.RegisterType<IdentitiesIFollowAuthenticationService>().AsSelf().SingleInstance();
            cb.RegisterType<FollowerAuthenticationService>().AsSelf().SingleInstance();
            cb.RegisterType<FeedDriveDistributionRouter>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<ReactionPreviewUpdatedNotification>>()
                .AsSelf()
                .SingleInstance();

            cb.RegisterType<TransitInboxBoxStorage>().SingleInstance();
            cb.RegisterType<PeerOutgoingTransferService>().As<IPeerOutgoingTransferService>().SingleInstance();

            cb.RegisterType<ExchangeGrantService>().AsSelf().SingleInstance();

            cb.RegisterType<PeerDriveQueryService>().AsSelf().SingleInstance();

            cb.RegisterType<PeerReactionSenderService>().AsSelf().SingleInstance();

            cb.RegisterType<PeerIncomingReactionService>().AsSelf().SingleInstance();
            cb.RegisterType<PeerIncomingGroupReactionInboxRouterService>().AsSelf().SingleInstance();
            
            cb.RegisterType<PublicPrivateKeyService>()
                .As<INotificationHandler<OwnerIsOnlineNotification>>()
                .AsSelf()
                .SingleInstance();

            cb.RegisterType<StaticFileContentService>().AsSelf().SingleInstance();

            cb.RegisterType<ConnectionAutoFixService>().AsSelf().SingleInstance();

            // Background services
            cb.AddTenantBackgroundServices(tenant);
        }

        internal static void InitializeTenant(ILifetimeScope scope, Tenant tenant)
        {
            // DEPRECATED - don't do stuff in here.
        }
    }
}