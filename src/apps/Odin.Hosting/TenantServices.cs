using System;
using System.IO;
using Autofac;
using MediatR;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
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
using Odin.Services.Drives.FileSystem.Comment.Update;
using Odin.Services.Drives.FileSystem.Standard.Update;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Configuration.VersionUpgrade.Version0tov1;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.LinkMetaExtractor;
using Odin.Services.Peer.AppNotification;
using Odin.Services.Membership.Connections.IcrKeyAvailableWorker;
using Odin.Services.Membership.Connections.Verification;
using Odin.Services.Peer.Incoming.Drive.Reactions.Group;
using Odin.Services.Registry;

namespace Odin.Hosting
{
    /// <summary>
    /// Set up per-tenant services
    /// </summary>
    public static class TenantServices
    {
        internal static void ConfigureTenantServices(ContainerBuilder cb, IdentityRegistration registration, OdinConfiguration config)
        {
            cb.RegisterType<NotificationListService>().AsSelf().SingleInstance();

            cb.RegisterType<PushNotificationService>()
                .As<INotificationHandler<ConnectionRequestReceived>>()
                .As<INotificationHandler<ConnectionRequestAcceptedNotification>>()
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
                .SingleInstance();
            
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
                .As<INotificationHandler<ConnectionBlockedNotification>>()
                .As<INotificationHandler<ConnectionFinalizedNotification>>()
                .As<INotificationHandler<ConnectionDeletedNotification>>()
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
            cb.RegisterType<StandardFileUpdateWriter>().AsSelf().InstancePerDependency();

            cb.RegisterType<StandardFileSystem>().AsSelf().InstancePerDependency();

            cb.RegisterType<CommentStreamWriter>().AsSelf().InstancePerDependency();
            cb.RegisterType<CommentPayloadStreamWriter>().AsSelf().InstancePerDependency();
            cb.RegisterType<CommentFileStorageService>().AsSelf().InstancePerDependency();
            cb.RegisterType<CommentFileQueryService>().AsSelf().InstancePerDependency();
            cb.RegisterType<CommentFileSystem>().AsSelf().InstancePerDependency();
            cb.RegisterType<CommentFileUpdateWriter>().AsSelf().InstancePerDependency();

            cb.RegisterType<DriveDatabaseHost>()
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
            cb.RegisterType<CircleNetworkIntroductionService>().AsSelf()
                .As<INotificationHandler<ConnectionFinalizedNotification>>()
                .As<INotificationHandler<ConnectionBlockedNotification>>()
                .As<INotificationHandler<ConnectionDeletedNotification>>()
                .SingleInstance();
            cb.RegisterType<CircleNetworkVerificationService>().AsSelf().SingleInstance();

            cb.RegisterType<FollowerService>().SingleInstance();
            cb.RegisterType<FollowerPerimeterService>().SingleInstance();

            cb.RegisterType<PeerOutbox>().AsSelf().SingleInstance();

            cb.RegisterType<PeerInboxProcessor>().AsSelf()
                .SingleInstance();

            cb.RegisterType<TransitAuthenticationService>()
                .As<INotificationHandler<ConnectionFinalizedNotification>>()
                .As<INotificationHandler<ConnectionBlockedNotification>>()
                .As<INotificationHandler<ConnectionDeletedNotification>>()
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
            cb.RegisterType<PeerOutgoingTransferService>().SingleInstance();

            cb.RegisterType<PeerOutboxProcessorMediatorAdapter>()
                .As<INotificationHandler<OutboxItemAddedNotification>>()
                .AsSelf();

            cb.RegisterType<ExchangeGrantService>().AsSelf().SingleInstance();

            cb.RegisterType<PeerDriveQueryService>().AsSelf().SingleInstance();

            cb.RegisterType<PeerReactionSenderService>().AsSelf().SingleInstance();

            cb.RegisterType<PeerIncomingReactionService>().AsSelf().SingleInstance();
            cb.RegisterType<PeerIncomingGroupReactionInboxRouterService>().AsSelf().SingleInstance();

            cb.RegisterType<PublicPrivateKeyService>()
                .AsSelf()
                .InstancePerLifetimeScope();

            cb.RegisterType<StaticFileContentService>().AsSelf().InstancePerLifetimeScope();

            cb.RegisterType<V0ToV1VersionMigrationService>().AsSelf().SingleInstance();
            cb.RegisterType<VersionUpgradeService>().AsSelf().SingleInstance();
            cb.RegisterType<VersionUpgradeScheduler>().AsSelf().InstancePerLifetimeScope();

            cb.RegisterType<PeerAppNotificationService>().AsSelf().InstancePerLifetimeScope();
            cb.RegisterType<IcrKeyAvailableBackgroundService>().AsSelf().SingleInstance();
            cb.RegisterType<IcrKeyAvailableScheduler>().AsSelf().InstancePerLifetimeScope();

            cb.RegisterType<CircleNetworkStorage>().InstancePerDependency();
            // cb.RegisterType<PeerDriveIncomingTransferService>().InstancePerDependency();

            // Tenant background services
            cb.AddTenantBackgroundServices(registration);

            // Tenant database services
            cb.ConfigureDatabaseServices(registration, config);
        }

        //

        private static void ConfigureDatabaseServices(
            this ContainerBuilder cb,
            IdentityRegistration registration,
            OdinConfiguration config)
        {
            cb.AddDatabaseCacheServices();
            switch (config.Database.Type)
            {
                case DatabaseType.Sqlite:
                {
                    // SEB:TODO move this out of service registration
                    // SEB:TODO duplicated from registration code. Don't do that.
                    var headersPath = Path.Combine(
                        config.Host.TenantDataRootPath,
                        "registrations",
                        registration.Id.ToString(),
                        "headers");
                    Directory.CreateDirectory(headersPath);
                    cb.AddSqliteIdentityDatabaseServices(registration.Id, Path.Combine(headersPath, "identity.db"));
                    break;
                }
                case DatabaseType.Postgres:
                    cb.AddPgsqlIdentityDatabaseServices(registration.Id, config.Database.ConnectionString);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported database type: {config.Database.Type}");
            }
        }
    }
}