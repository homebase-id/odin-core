using System;
using Autofac;
using MediatR;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
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
using Odin.Services.Configuration.VersionUpgrade.Version2tov3;
using Odin.Services.Configuration.VersionUpgrade.Version3tov4;
using Odin.Services.Configuration.VersionUpgrade.Version4tov5;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.Fingering;
using Odin.Services.LinkMetaExtractor;
using Odin.Services.LinkPreview;
using Odin.Services.Peer.AppNotification;
using Odin.Services.Membership.Connections.Verification;
using Odin.Services.Peer.Incoming.Drive.Reactions.Group;
using Odin.Services.Registry;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.LinkPreview.Posts;
using Odin.Services.LinkPreview.Profile;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Configuration.VersionUpgrade.Version5tov6;
using Odin.Services.Security;
using Odin.Services.Security.Email;
using Odin.Services.Security.Health;
using Odin.Services.Security.Health.RiskAnalyzer;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting;

/// <summary>
/// Set up per-tenant services
/// </summary>
public static class TenantServices
{
    internal static ContainerBuilder ConfigureTenantServices(
        ContainerBuilder cb,
        IdentityRegistration registration,
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

        cb.RegisterInstance(new OdinIdentity(registration.Id, registration.PrimaryDomainName)).SingleInstance();

        cb.RegisterGeneric(typeof(SharedDeviceSocketCollection<>)).SingleInstance(); // SEB:TODO does not scale

        cb.RegisterType<DriveQuery>().InstancePerLifetimeScope();

        cb.RegisterType<NotificationListService>().AsSelf().InstancePerLifetimeScope();

        cb.RegisterType<PushNotificationService>()
            .As<INotificationHandler<ConnectionRequestReceivedNotification>>()
            .As<INotificationHandler<ConnectionRequestAcceptedNotification>>()
            .As<INotificationHandler<ShamirPasswordRecoverySufficientShardsCollectedNotification>>()
            .As<INotificationHandler<ShamirPasswordRecoveryShardCollectedNotification>>()
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
        cb.RegisterType<OdinContextCache>().SingleInstance();
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

        cb.RegisterType<RecoveryNotifier>().InstancePerLifetimeScope();
        cb.RegisterType<ShamirConfigurationService>().InstancePerLifetimeScope();
        
        cb.RegisterType<ShamirRecoveryService>().InstancePerLifetimeScope();
        cb.RegisterType<PasswordKeyRecoveryService>().InstancePerLifetimeScope();
        cb.RegisterType<OwnerSecretService>().InstancePerLifetimeScope();

        cb.RegisterType<OwnerAuthenticationService>()
            .AsSelf()
            .As<INotificationHandler<DriveDefinitionAddedNotification>>()
            .InstancePerLifetimeScope();

        cb.RegisterType<OwnerSecurityHealthService>().AsSelf().InstancePerLifetimeScope();

        cb.RegisterType<DriveManager>().AsSelf().As<IDriveManager>().InstancePerLifetimeScope();

        cb.RegisterType<LongTermStorageManager>().InstancePerLifetimeScope();
        cb.RegisterType<UploadStorageManager>().InstancePerLifetimeScope();
        // cb.RegisterType<OrphanTestUtil>().InstancePerLifetimeScope();

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

        cb.RegisterType<FeedWriter>().AsSelf().InstancePerLifetimeScope();
        cb.RegisterType<AppRegistrationService>()
            .AsSelf()
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

        cb.RegisterType<IdentitiesIFollowAuthenticationService>().InstancePerLifetimeScope();

        cb.RegisterType<FollowerAuthenticationService>().InstancePerLifetimeScope();

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
        cb.RegisterType<V2ToV3VersionMigrationService>().InstancePerLifetimeScope();
        cb.RegisterType<V3ToV4VersionMigrationService>().InstancePerLifetimeScope();
        cb.RegisterType<V4ToV5VersionMigrationService>().InstancePerLifetimeScope();
        cb.RegisterType<V5ToV6VersionMigrationService>().InstancePerLifetimeScope();

        cb.RegisterType<VersionUpgradeService>().InstancePerLifetimeScope();
        cb.RegisterType<VersionUpgradeScheduler>().InstancePerLifetimeScope();

        cb.RegisterType<PeerAppNotificationService>().AsSelf().InstancePerLifetimeScope();
        cb.RegisterType<CircleNetworkStorage>().InstancePerDependency();

        cb.RegisterType<WebfingerService>().As<IWebfingerService>().InstancePerLifetimeScope();
        cb.RegisterType<DidService>().As<IDidService>().InstancePerLifetimeScope();
        cb.RegisterType<LinkPreviewService>().As<LinkPreviewService>().InstancePerLifetimeScope();
        cb.RegisterType<LinkPreviewAuthenticationService>().As<LinkPreviewAuthenticationService>().InstancePerLifetimeScope();


        cb.RegisterType<HomebaseProfileContentService>().AsSelf().InstancePerLifetimeScope();
        cb.RegisterType<HomebaseChannelContentService>().AsSelf().InstancePerLifetimeScope();
        cb.RegisterType<HomebaseSsrService>().AsSelf().InstancePerLifetimeScope();

        cb.RegisterType<Defragmenter>().AsSelf().InstancePerDependency();

        // Tenant background services
        cb.AddTenantBackgroundServices(registration);

        // Tenant database services
        cb.ConfigureDatabaseServices(registration, odinConfig);

        // Tenant cache services
        cb.AddTenantCaches(registration.Id.ToString());

        // Payload storage
        if (odinConfig.S3PayloadStorage.Enabled)
        {
            cb.RegisterType<PayloadS3ReaderWriter>().As<IPayloadReaderWriter>().SingleInstance();
        }
        else
        {
            cb.RegisterType<PayloadFileReaderWriter>().As<IPayloadReaderWriter>().SingleInstance();
        }

        return cb;
    }

    //

    private static void ConfigureDatabaseServices(
        this ContainerBuilder cb,
        IdentityRegistration registration,
        OdinConfiguration config)
    {
        switch (config.Database.Type)
        {
            case DatabaseType.Sqlite:
            {
                var tenantPathManager = new TenantPathManager(config, registration.Id);
                cb.AddSqliteIdentityDatabaseServices(registration.Id, tenantPathManager.GetIdentityDatabasePath());
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

    //
}