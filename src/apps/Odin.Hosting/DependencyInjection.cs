using Autofac;
using MediatR;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Services.AppNotifications.Push;
using Odin.Core.Services.AppNotifications.SystemNotifications;
using Odin.Core.Services.AppNotifications.WebSocket;
using Odin.Core.Services.Apps.CommandMessaging;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Authentication.Transit;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.DataSubscription;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Comment;
using Odin.Core.Services.Drives.FileSystem.Comment.Attachments;
using Odin.Core.Services.Drives.FileSystem.Standard;
using Odin.Core.Services.Drives.FileSystem.Standard.Attachments;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Drives.Reactions;
using Odin.Core.Services.Drives.Statistics;
using Odin.Core.Services.EncryptionKeyService;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Mediator.Owner;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Membership.Connections.Requests;
using Odin.Core.Services.Membership.YouAuth;
using Odin.Core.Services.Optimization.Cdn;
using Odin.Core.Services.Peer.Incoming.Drive.Reactions;
using Odin.Core.Services.Peer.Incoming.Drive.Transfer;
using Odin.Core.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Core.Services.Peer.Outgoing.Drive.Query;
using Odin.Core.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Core.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Core.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Core.Services.Registry;
using Odin.Core.Services.Tenant;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Home.Service;

namespace Odin.Hosting
{
    /// <summary>
    /// Set up per-tenant services
    /// </summary>
    public static class DependencyInjection
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
                .As<INotificationHandler<TransitFileReceivedNotification>>()
                .As<INotificationHandler<NewFollowerNotification>>()
                .As<INotificationHandler<ReactionContentAddedNotification>>()
                .As<INotificationHandler<ReactionPreviewUpdatedNotification>>()
                .AsSelf()
                .SingleInstance();

            cb.RegisterType<TenantConfigService>().AsSelf().SingleInstance();
            cb.RegisterType<TenantContext>().AsSelf().SingleInstance();

            cb.RegisterType<OdinContextAccessor>().AsSelf().InstancePerLifetimeScope();
            cb.RegisterType<OdinContext>().AsSelf().InstancePerLifetimeScope();
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
            cb.RegisterType<StandardDriveCommandService>().AsSelf().InstancePerDependency();

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
                .AsSelf()
                .SingleInstance();


            cb.RegisterType<ReactionContentService>().AsSelf().SingleInstance();

            cb.RegisterType<ReactionPreviewCalculator>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<ReactionContentAddedNotification>>()
                .As<INotificationHandler<ReactionDeletedNotification>>()
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

            cb.RegisterType<PeerOutbox>().As<IPeerOutbox>().SingleInstance();

            cb.RegisterType<TransitInboxProcessor>().AsSelf()
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
            cb.RegisterType<PeerOutgoingOutgoingTransferService>().As<IPeerOutgoingTransferService>().SingleInstance();

            cb.RegisterType<CommandMessagingService>().AsSelf().SingleInstance();

            cb.RegisterType<ExchangeGrantService>().AsSelf().SingleInstance();

            cb.RegisterType<PeerDriveQueryService>().AsSelf().SingleInstance();

            cb.RegisterType<PeerReactionSenderService>().AsSelf().SingleInstance();

            cb.RegisterType<PeerReactionService>().AsSelf().SingleInstance();

            cb.RegisterType<PublicPrivateKeyService>()
                .As<INotificationHandler<OwnerIsOnlineNotification>>()
                .AsSelf()
                .SingleInstance();

            cb.RegisterType<StaticFileContentService>().AsSelf().SingleInstance();
        }

        internal static void InitializeTenant(ILifetimeScope scope, Tenant tenant)
        {
            //TODO: add logging back in
            // var logger = scope.Resolve<ILogger<Startup>>();
            // logger.LogInformation("Initializing tenant {Tenant}", tenant.Name);

            var registry = scope.Resolve<IIdentityRegistry>();
            var tenantContext = scope.Resolve<TenantContext>();
            
            var tc = registry.CreateTenantContext(tenant.Name);
            tenantContext.Update(tc);
        }
    }
}
