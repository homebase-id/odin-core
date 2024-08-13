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
    public static class DependencyInjection
    {
        internal static void ConfigureMultiTenantServices(ContainerBuilder cb, Tenant tenant)
        {
            // SEB:TODO lifetime
            cb.RegisterType<TenantSystemStorage>()
                .SingleInstance();

            // SEB:IOC-OK
            cb.RegisterType<NotificationListService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<PushNotificationService>()
                .As<INotificationHandler<ConnectionRequestReceived>>()
                .As<INotificationHandler<ConnectionRequestAccepted>>()
                .AsSelf()
                .InstancePerDependency();
            
            // SEB:TODO why is this registered per-tenant?
            cb.RegisterType<LinkMetaExtractor>()
                .As<ILinkMetaExtractor>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<PushNotificationOutboxAdapter>()
                .As<INotificationHandler<PushNotificationEnqueuedNotification>>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<FeedNotificationMapper>()
                .As<INotificationHandler<ReactionContentAddedNotification>>()
                .As<INotificationHandler<NewFeedItemReceived>>()
                .As<INotificationHandler<NewFollowerNotification>>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .InstancePerDependency();

            // SEB:TODO must inject DeviceSocketCollection to become transient 
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

            // SEB:TODO this should be transient, but runs code in ctor that touches database
            cb.RegisterType<TenantConfigService>()
                .SingleInstance();
            
            // SEB:TODO needs more investigation
            // (was: this should possibly be per-scope, because it has properties that are updated in individual requests
            // most importantly during authentication. But it lives in a number of singleton services, so we need to
            // be careful here.)
            cb.RegisterType<TenantContext>()
                .SingleInstance();

            // SEB:IOC-OK
            cb.RegisterType<OdinContext>()
                .As<IOdinContext>()
                .InstancePerLifetimeScope();
            
            // SEB:IOC-OK
            cb.RegisterType<OdinHttpClientFactory>()
                .As<IOdinHttpClientFactory>()
                .InstancePerDependency();

            // SEB:TODO this should probably be transient and have the IAppCache injected into it
            cb.RegisterType<HomeCachingService>()
                .AsSelf()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<DriveDefinitionAddedNotification>>()
                .SingleInstance();

            // SEB:TODO needs more investigation because of OdinContextCache
            cb.RegisterType<HomeAuthenticatorService>()
                .AsSelf()
                .As<INotificationHandler<IdentityConnectionRegistrationChangedNotification>>()
                .SingleInstance();
            
            // SEB:IOC-OK
            cb.RegisterType<HomeRegistrationStorage>()
                .InstancePerDependency();

            // SEB:TODO needs more investigation
            cb.RegisterType<YouAuthUnifiedService>()
                .As<IYouAuthUnifiedService>()
                .SingleInstance();
           
            // SEB:TODO needs more investigation because of OdinContextCache 
            cb.RegisterType<YouAuthDomainRegistrationService>()
                .SingleInstance();

            // SEB:IOC-OK
            cb.RegisterType<RecoveryService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<OwnerSecretService>()
                .InstancePerDependency();
            
            // SEB:TODO needs more investigation because of OdinContextCache
            cb.RegisterType<OwnerAuthenticationService>()
                .AsSelf()
                .As<INotificationHandler<DriveDefinitionAddedNotification>>()
                .SingleInstance();

            // SEB:TODO this should be transient, but runs code in ctor that touches database
            cb.RegisterType<DriveManager>()
                .SingleInstance();
            
            // SEB:IOC-OK
            cb.RegisterType<DriveAclAuthorizationService>()
                .As<IDriveAclAuthorizationService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<FileSystemResolver>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<FileSystemHttpRequestResolver>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<StandardFileStreamWriter>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<StandardFilePayloadStreamWriter>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<StandardFileDriveStorageService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<StandardFileDriveQueryService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<StandardFileSystem>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<CommentStreamWriter>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<CommentPayloadStreamWriter>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<CommentFileStorageService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<CommentFileQueryService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<CommentFileSystem>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<DriveDatabaseHost>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .AsSelf()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<ReactionContentService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<GroupReactionService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<ReactionPreviewCalculator>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<ReactionContentAddedNotification>>()
                .As<INotificationHandler<ReactionContentDeletedNotification>>()
                .As<INotificationHandler<AllReactionsByFileDeleted>>()
                .InstancePerDependency();

            // SEB:TODO needs more investigation because of OdinContextCache
            cb.RegisterType<AppRegistrationService>()
                .As<IAppRegistrationService>()
                .SingleInstance();

            // SEB:IOC-OK
            cb.RegisterType<CircleMembershipService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<IcrKeyService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<CircleDefinitionService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<CircleNetworkService>()
                .AsSelf()
                .As<INotificationHandler<DriveDefinitionAddedNotification>>()
                .As<INotificationHandler<AppRegistrationChangedNotification>>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<CircleNetworkRequestService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<FollowerService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<FollowerPerimeterService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<PeerOutbox>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<PeerInboxProcessor>()
                .AsSelf()
                .As<INotificationHandler<RsaKeyRotatedNotification>>()
                .InstancePerDependency();

            // SEB:TODO needs more investigation because of OdinContextCache
            cb.RegisterType<TransitAuthenticationService>()
                .As<INotificationHandler<IdentityConnectionRegistrationChangedNotification>>()
                .AsSelf()
                .SingleInstance();

            // SEB:TODO needs more investigation because of OdinContextCache
            cb.RegisterType<IdentitiesIFollowAuthenticationService>()
                .AsSelf()
                .SingleInstance();
            
            // SEB:TODO needs more investigation because of OdinContextCache
            cb.RegisterType<FollowerAuthenticationService>()
                .AsSelf()
                .SingleInstance();

            // SEB:IOC-OK
            cb.RegisterType<FeedDriveDistributionRouter>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<ReactionPreviewUpdatedNotification>>()
                .AsSelf()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<TransitInboxBoxStorage>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<PeerOutgoingTransferService>()
                .As<IPeerOutgoingTransferService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<ExchangeGrantService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<PeerDriveQueryService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<PeerReactionSenderService>()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<PeerIncomingReactionService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<PeerIncomingGroupReactionInboxRouterService>()
                .InstancePerDependency();
            
            // SEB:IOC-OK
            cb.RegisterType<PublicPrivateKeyService>()
                .As<INotificationHandler<OwnerIsOnlineNotification>>()
                .AsSelf()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<StaticFileContentService>()
                .AsSelf()
                .InstancePerDependency();

            // SEB:IOC-OK
            cb.RegisterType<ConnectionAutoFixService>()
                .AsSelf()
                .InstancePerDependency();

            // Background services
            cb.AddTenantBackgroundServices(tenant);
        }

        internal static void InitializeTenant(ILifetimeScope scope, Tenant tenant)
        {
            // DEPRECATED - don't do stuff in here.
        }
    }
}