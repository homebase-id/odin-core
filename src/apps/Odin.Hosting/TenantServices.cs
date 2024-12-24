using System;
using System.IO;
using Autofac;
using MediatR;
using Odin.Core.Cache;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
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
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.Comment.Attachments;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.FileSystem.Standard.Attachments;
using Odin.Services.Drives.Management;
using Odin.Services.Drives.Reactions;
using Odin.Services.Drives.Statistics;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Mediator;
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
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Services.Background;
using Odin.Services.Drives.FileSystem.Comment.Update;
using Odin.Services.Drives.FileSystem.Standard.Update;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Configuration.VersionUpgrade.Version0tov1;
using Odin.Services.Configuration.VersionUpgrade.Version1tov2;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.Fingering;
using Odin.Services.LinkMetaExtractor;
using Odin.Services.Peer.AppNotification;
using Odin.Services.Membership.Connections.Verification;
using Odin.Services.Peer.Incoming.Drive.Reactions.Group;
using Odin.Services.Registry;

namespace Odin.Hosting;

/// <summary>
/// Set up per-tenant services
/// </summary>
public static class TenantServices
{
    internal static void ConfigureTenantServices(
        ContainerBuilder cb,
        IdentityRegistration registration,
        TenantStorageConfig storageConfig,
        OdinConfiguration odinConfig)
    {
        //
        // DO NOT CREATE ANY SINGLETONS HERE!
        //
        // Unless you are sure that ALL their dependencies are, and always will be, either:
        // - Singletons
        // - Thread-safe scoped or transient
        //
        // Absolutely do not create singletons that depend on OdinContext or any database related classes.
        //

        cb.RegisterGeneric(typeof(GenericMemoryCache<>)).As(typeof(IGenericMemoryCache<>)).SingleInstance();
        cb.RegisterGeneric(typeof(SharedOdinContextCache<>)).SingleInstance();
        cb.RegisterGeneric(typeof(SharedConcurrentDictionary<,,>)).SingleInstance();
        cb.RegisterGeneric(typeof(SharedAsyncLock<>)).SingleInstance();
        cb.RegisterGeneric(typeof(SharedKeyedAsyncLock<>)).SingleInstance();
        cb.RegisterGeneric(typeof(SharedDeviceSocketCollection<>)).SingleInstance();

        cb.RegisterType<DriveQuery>().InstancePerLifetimeScope();

        cb.RegisterType<NotificationListService>().AsSelf().InstancePerLifetimeScope();

        cb.RegisterType<PushNotificationService>()
            .As<INotificationHandler<ConnectionRequestReceivedNotification>>()
            .As<INotificationHandler<ConnectionRequestAcceptedNotification>>()
            .AsSelf()
            .InstancePerLifetimeScope();

        cb.RegisterType<LinkMetaExtractor>().As<ILinkMetaExtractor>();

        cb.RegisterType<PushNotificationOutboxAdapter>()
            .As<INotificationHandler<PushNotificationEnqueuedNotification>>()
            .AsSelf()
            .InstancePerLifetimeScope();

        cb.RegisterType<FeedNotificationMapper>()
            .As<INotificationHandler<ReactionContentAddedNotification>>()
            .As<INotificationHandler<NewFeedItemReceived>>()
            .As<INotificationHandler<NewFollowerNotification>>()
            .As<INotificationHandler<DriveFileAddedNotification>>()
            .AsSelf()
            .InstancePerLifetimeScope();

        cb.RegisterType<AppNotificationHandler>()
            .As<INotificationHandler<FileAddedNotification>>()
            .As<INotificationHandler<ConnectionRequestReceivedNotification>>()
            .As<INotificationHandler<ConnectionRequestAcceptedNotification>>()
            .As<INotificationHandler<DriveFileAddedNotification>>()
            .As<INotificationHandler<DriveFileChangedNotification>>()
            .As<INotificationHandler<DriveFileDeletedNotification>>()
            .As<INotificationHandler<InboxItemReceivedNotification>>()
            .As<INotificationHandler<NewFollowerNotification>>()
            .As<INotificationHandler<ReactionContentAddedNotification>>()
            .As<INotificationHandler<ReactionContentDeletedNotification>>()
            .As<INotificationHandler<ReactionPreviewUpdatedNotification>>()
            .As<INotificationHandler<AppNotificationAddedNotification>>()
            .As<INotificationHandler<ConnectionFinalizedNotification>>()
            .AsSelf()
            .InstancePerLifetimeScope();

        cb.RegisterType<PeerAppNotificationHandler>()
            // .As<INotificationHandler<FileAddedNotification>>()
            .As<INotificationHandler<DriveFileAddedNotification>>()
            .As<INotificationHandler<DriveFileChangedNotification>>()
            .As<INotificationHandler<DriveFileDeletedNotification>>()
            .As<INotificationHandler<ReactionContentAddedNotification>>()
            .As<INotificationHandler<ReactionContentDeletedNotification>>()
            .As<INotificationHandler<ReactionPreviewUpdatedNotification>>()
            // .As<INotificationHandler<AppNotificationAddedNotification>>()
            .AsSelf()
            .InstancePerLifetimeScope();


        cb.RegisterType<TenantConfigService>().AsSelf().InstancePerLifetimeScope();
        cb.RegisterType<TenantContext>().AsSelf().SingleInstance();

        cb.RegisterType<OdinContext>().As<IOdinContext>().AsSelf().InstancePerLifetimeScope();
        cb.RegisterType<OdinHttpClientFactory>().As<IOdinHttpClientFactory>().SingleInstance();

        cb.RegisterType<HomeCachingService>()
            .AsSelf()
            .As<INotificationHandler<DriveFileAddedNotification>>()
            .As<INotificationHandler<DriveFileChangedNotification>>()
            .As<INotificationHandler<DriveFileDeletedNotification>>()
            .As<INotificationHandler<DriveDefinitionAddedNotification>>()
            .InstancePerLifetimeScope();

        cb.RegisterType<HomeAuthenticatorService>()
            .AsSelf()
            .As<INotificationHandler<ConnectionBlockedNotification>>()
            .As<INotificationHandler<ConnectionFinalizedNotification>>()
            .As<INotificationHandler<ConnectionDeletedNotification>>()
            .InstancePerLifetimeScope();

        cb.RegisterType<HomeRegistrationStorage>().InstancePerLifetimeScope();

        cb.RegisterType<YouAuthUnifiedService>().As<IYouAuthUnifiedService>().InstancePerLifetimeScope();

        cb.RegisterType<YouAuthDomainRegistrationService>().InstancePerLifetimeScope();

        cb.RegisterType<RecoveryService>().InstancePerLifetimeScope();
        cb.RegisterType<OwnerSecretService>().InstancePerLifetimeScope();

        cb.RegisterType<OwnerAuthenticationService>()
            .AsSelf()
            .As<INotificationHandler<DriveDefinitionAddedNotification>>()
            .InstancePerLifetimeScope();

        cb.RegisterType<DriveManager>().InstancePerLifetimeScope();

        cb.RegisterType<LongTermStorageManager>().InstancePerLifetimeScope();
        cb.RegisterType<TempStorageManager>().InstancePerLifetimeScope();

        cb.RegisterType<DriveAclAuthorizationService>().As<IDriveAclAuthorizationService>().InstancePerLifetimeScope();

        cb.RegisterType<FileSystemResolver>().InstancePerDependency();
        cb.RegisterType<FileSystemHttpRequestResolver>().InstancePerDependency();

        cb.RegisterType<StandardFileStreamWriter>().InstancePerDependency();
        cb.RegisterType<StandardFilePayloadStreamWriter>().InstancePerDependency();
        cb.RegisterType<StandardFileDriveStorageService>().InstancePerDependency();
        cb.RegisterType<StandardFileDriveQueryService>().InstancePerDependency();
        cb.RegisterType<StandardFileUpdateWriter>().InstancePerDependency();

        cb.RegisterType<StandardFileSystem>().InstancePerDependency();

        cb.RegisterType<CommentStreamWriter>().InstancePerDependency();
        cb.RegisterType<CommentPayloadStreamWriter>().InstancePerDependency();
        cb.RegisterType<CommentFileStorageService>().InstancePerDependency();
        cb.RegisterType<CommentFileQueryService>().InstancePerDependency();
        cb.RegisterType<CommentFileSystem>().InstancePerDependency();
        cb.RegisterType<CommentFileUpdateWriter>().InstancePerDependency();

        cb.RegisterType<ReactionContentService>().InstancePerLifetimeScope();
        cb.RegisterType<GroupReactionService>().InstancePerLifetimeScope();

        cb.RegisterType<ReactionPreviewCalculator>()
            .As<INotificationHandler<DriveFileAddedNotification>>()
            .As<INotificationHandler<DriveFileChangedNotification>>()
            .As<INotificationHandler<DriveFileDeletedNotification>>()
            .As<INotificationHandler<ReactionContentAddedNotification>>()
            .As<INotificationHandler<ReactionContentDeletedNotification>>()
            .As<INotificationHandler<AllReactionsByFileDeleted>>();

        cb.RegisterType<AppRegistrationService>()
            .As<IAppRegistrationService>()
            .InstancePerLifetimeScope();

        cb.RegisterType<CircleMembershipService>().InstancePerLifetimeScope();
        cb.RegisterType<IcrKeyService>().InstancePerLifetimeScope();
        cb.RegisterType<CircleDefinitionService>().InstancePerLifetimeScope();

        cb.RegisterType<CircleNetworkService>()
            .AsSelf()
            .As<INotificationHandler<DriveDefinitionAddedNotification>>()
            .As<INotificationHandler<AppRegistrationChangedNotification>>()
            .InstancePerLifetimeScope();

        cb.RegisterType<CircleNetworkRequestService>().InstancePerLifetimeScope();
        cb.RegisterType<CircleNetworkIntroductionService>().AsSelf()
            .As<INotificationHandler<ConnectionFinalizedNotification>>()
            .As<INotificationHandler<ConnectionBlockedNotification>>()
            .As<INotificationHandler<ConnectionDeletedNotification>>()
            .As<INotificationHandler<ConnectionRequestReceivedNotification>>()
            .InstancePerLifetimeScope();

        cb.RegisterType<CircleNetworkVerificationService>().InstancePerLifetimeScope();

        cb.RegisterType<FollowerService>().InstancePerLifetimeScope();
        cb.RegisterType<FollowerPerimeterService>().InstancePerLifetimeScope();

        cb.RegisterType<PeerOutbox>().InstancePerLifetimeScope();

        cb.RegisterType<PeerInboxProcessor>().InstancePerLifetimeScope();

        cb.RegisterType<TransitAuthenticationService>()
            .As<INotificationHandler<ConnectionFinalizedNotification>>()
            .As<INotificationHandler<ConnectionBlockedNotification>>()
            .As<INotificationHandler<ConnectionDeletedNotification>>()
            .AsSelf()
            .InstancePerLifetimeScope();
        cb.RegisterInstance(
            new SharedOdinContextCache<TransitAuthenticationService>(odinConfig.Host.CacheSlidingExpirationSeconds));

        cb.RegisterType<IdentitiesIFollowAuthenticationService>().InstancePerLifetimeScope();
        cb.RegisterInstance(
            new SharedOdinContextCache<IdentitiesIFollowAuthenticationService>(odinConfig.Host.CacheSlidingExpirationSeconds));

        cb.RegisterType<FollowerAuthenticationService>().InstancePerLifetimeScope();
        cb.RegisterInstance(
            new SharedOdinContextCache<FollowerAuthenticationService>(odinConfig.Host.CacheSlidingExpirationSeconds));

        cb.RegisterType<FeedDriveDistributionRouter>()
            .As<INotificationHandler<DriveFileAddedNotification>>()
            .As<INotificationHandler<DriveFileChangedNotification>>()
            .As<INotificationHandler<DriveFileDeletedNotification>>()
            .As<INotificationHandler<ReactionPreviewUpdatedNotification>>()
            .InstancePerLifetimeScope();

        cb.RegisterType<TransitInboxBoxStorage>().InstancePerLifetimeScope();
        cb.RegisterType<PeerOutgoingTransferService>().InstancePerLifetimeScope();

        cb.RegisterType<PeerOutboxProcessorMediatorAdapter>()
            .As<INotificationHandler<OutboxItemAddedNotification>>()
            .AsSelf();

        cb.RegisterType<ExchangeGrantService>().InstancePerLifetimeScope();

        cb.RegisterType<PeerDriveQueryService>().InstancePerLifetimeScope();

        cb.RegisterType<PeerReactionSenderService>().AsSelf().InstancePerLifetimeScope();

        cb.RegisterType<PeerIncomingReactionService>().AsSelf().InstancePerLifetimeScope();
        cb.RegisterType<PeerIncomingGroupReactionInboxRouterService>().AsSelf().InstancePerLifetimeScope();

        cb.RegisterType<PublicPrivateKeyService>()
            .AsSelf()
            .InstancePerLifetimeScope();

        cb.RegisterType<StaticFileContentService>().AsSelf().InstancePerLifetimeScope();

        cb.RegisterType<V0ToV1VersionMigrationService>().InstancePerLifetimeScope();
        cb.RegisterType<V1ToV2VersionMigrationService>().InstancePerLifetimeScope();
        cb.RegisterType<VersionUpgradeService>().InstancePerLifetimeScope();
        cb.RegisterType<VersionUpgradeScheduler>().InstancePerLifetimeScope();

        cb.RegisterType<PeerAppNotificationService>().AsSelf().InstancePerLifetimeScope();
        cb.RegisterType<CircleNetworkStorage>().InstancePerDependency();

        cb.RegisterType<WebfingerService>().As<IWebfingerService>().InstancePerLifetimeScope();
        cb.RegisterType<DidService>().As<IDidService>().InstancePerLifetimeScope();

        // Tenant background services
        cb.AddTenantBackgroundServices(registration);

        // Tenant database services
        cb.ConfigureDatabaseServices(registration, storageConfig, odinConfig);
    }

    //

    private static void ConfigureDatabaseServices(
        this ContainerBuilder cb,
        IdentityRegistration registration,
        TenantStorageConfig storageConfig,
        OdinConfiguration config)
    {
        cb.AddDatabaseCacheServices();
        switch (config.Database.Type)
        {
            case DatabaseType.Sqlite:
            {
                cb.AddSqliteIdentityDatabaseServices(
                    registration.Id,
                    Path.Combine(storageConfig.HeaderDataStoragePath, "identity.db"));
                break;
            }
            case DatabaseType.Postgres:
                cb.AddPgsqlIdentityDatabaseServices(
                    registration.Id,
                    config.Database.ConnectionString);
                break;
            default:
                throw new InvalidOperationException($"Unsupported database type: {config.Database.Type}");
        }
    }
}