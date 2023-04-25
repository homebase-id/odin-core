using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using Autofac;
using MediatR;
using MediatR.Pipeline;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Authentication.Apps;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authentication.Transit;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Certificate.Renewal;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.DataSubscription;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Drives.FileSystem.Comment;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Drives.Statistics;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Tenant;
using Youverse.Core.Services.Transit.ReceivingHost;
using Youverse.Core.Services.Transit.ReceivingHost.Incoming;
using Youverse.Core.Services.Transit.ReceivingHost.Reactions;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Services.Transit.SendingHost.Outbox;
using Youverse.Core.Storage;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting
{
    /// <summary>
    /// Set up per-tenant services
    /// </summary>
    public static class DependencyInjection
    {
        internal static void ConfigureMultiTenantServices(ContainerBuilder cb, Tenant tenant)
        {
            RegisterMediator(ref cb);

            cb.RegisterType<UploadLock>().AsSelf().SingleInstance();

            // cb.RegisterType<ServerSystemStorage>().AsSelf().SingleInstance();
            cb.RegisterType<TenantSystemStorage>().AsSelf().SingleInstance();

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

            cb.RegisterType<DotYouContextAccessor>().AsSelf().InstancePerLifetimeScope();
            cb.RegisterType<DotYouContext>().AsSelf().InstancePerLifetimeScope();

            cb.RegisterType<TenantCertificateService>().As<ITenantCertificateService>().SingleInstance();
            cb.RegisterType<DotYouHttpClientFactory>().As<IDotYouHttpClientFactory>().SingleInstance();

            cb.RegisterType<YouAuthService>().As<IYouAuthService>().SingleInstance();
            cb.RegisterType<YouAuthRegistrationService>()
                .As<IYouAuthRegistrationService>()
                .As<INotificationHandler<IdentityConnectionRegistrationChangedNotification>>()
                .SingleInstance();
            cb.RegisterType<YouAuthRegistrationStorage>().As<IYouAuthRegistrationStorage>().SingleInstance();
            cb.RegisterType<YouAuthAuthorizationCodeManager>().As<IYouAuthAuthorizationCodeManager>().SingleInstance();

            cb.RegisterType<OwnerSecretService>().As<IOwnerSecretService>().SingleInstance();
            cb.RegisterType<OwnerAuthenticationService>()
                .As<IOwnerAuthenticationService>()
                .As<INotificationHandler<DriveDefinitionAddedNotification>>()
                .SingleInstance();

            cb.RegisterType<AppAuthenticationService>().As<IAppAuthenticationService>().SingleInstance();

            cb.RegisterType<DriveManager>().AsSelf().SingleInstance();
            cb.RegisterType<DriveAclAuthorizationService>().As<IDriveAclAuthorizationService>().SingleInstance();
            
            cb.RegisterType<FileSystemResolver>().AsSelf().InstancePerDependency();
            cb.RegisterType<FileSystemHttpRequestResolver>().AsSelf().InstancePerDependency();
            
            cb.RegisterType<StandardFileStreamWriter>().AsSelf().InstancePerDependency();
            cb.RegisterType<StandardFileDriveStorageService>().AsSelf().InstancePerDependency();
            cb.RegisterType<StandardFileDriveQueryService>().AsSelf().InstancePerDependency();
            cb.RegisterType<StandardDriveCommandService>().AsSelf().InstancePerDependency();
            //Note As<IDriveFileSystem> means this will be the default in cases where we do not resolve the filesystem
            cb.RegisterType<StandardFileSystem>().AsSelf().InstancePerDependency();
            
            cb.RegisterType<CommentStreamWriter>().AsSelf().InstancePerDependency();
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
                .As<INotificationHandler<ReactionContentAddedNotification>>();
            
            cb.RegisterType<AppRegistrationService>().As<IAppRegistrationService>().SingleInstance();
            
            cb.RegisterType<CircleDefinitionService>().As<CircleDefinitionService>().SingleInstance();
            cb.RegisterType<CircleNetworkService>()
                .As<ICircleNetworkService>()
                .As<INotificationHandler<DriveDefinitionAddedNotification>>()
                .As<INotificationHandler<AppRegistrationChangedNotification>>()
                .SingleInstance();
            
            cb.RegisterType<CircleNetworkRequestService>().As<ICircleNetworkRequestService>().SingleInstance();

            cb.RegisterType<FollowerService>().SingleInstance();
            cb.RegisterType<FollowerPerimeterService>().SingleInstance();

            cb.RegisterType<TransitOutbox>().As<ITransitOutbox>().SingleInstance();

            cb.RegisterType<TransitInboxProcessor>().SingleInstance();
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
            cb.RegisterType<TransitService>().As<ITransitService>().SingleInstance();

            cb.RegisterType<CommandMessagingService>().AsSelf().SingleInstance();
            
            cb.RegisterType<ExchangeGrantService>().AsSelf().SingleInstance();

            cb.RegisterType<TransitQueryService>().AsSelf().SingleInstance();
            
            cb.RegisterType<TransitReactionContentSenderService>().AsSelf().SingleInstance();

            cb.RegisterType<TransitReactionPerimeterService>().AsSelf().SingleInstance();

            cb.RegisterType<RsaKeyService>().As<IPublicKeyService>().SingleInstance();

            cb.RegisterType<StaticFileContentService>().AsSelf().SingleInstance();

            cb.RegisterType<LetsEncryptTenantCertificateRenewalService>().As<ITenantCertificateRenewalService>();
        }

        private static void RegisterMediator(ref ContainerBuilder cb)
        {
            //TODO: following the docs here but should we pull in everything from this assembly?
            cb.RegisterAssemblyTypes(typeof(IMediator).GetTypeInfo().Assembly).AsImplementedInterfaces().SingleInstance();

            var mediatrOpenTypes = new[]
            {
                typeof(IRequestHandler<,>),
                typeof(IRequestExceptionHandler<,,>),
                typeof(IRequestExceptionAction<,>),
                typeof(INotificationHandler<>),
                typeof(IStreamRequestHandler<,>)
            };

            foreach (var mediatrOpenType in mediatrOpenTypes)
            {
                cb
                    .RegisterAssemblyTypes(typeof(Ping).GetTypeInfo().Assembly)
                    .AsClosedTypesOf(mediatrOpenType)
                    // when having a single class implementing several handler types
                    // this call will cause a handler to be called twice
                    // in general you should try to avoid having a class implementing for instance `IRequestHandler<,>` and `INotificationHandler<>`
                    // the other option would be to remove this call
                    // see also https://github.com/jbogard/MediatR/issues/462
                    .AsImplementedInterfaces();
            }

            cb.Register<ServiceFactory>(ctx =>
            {
                var c = ctx.Resolve<IComponentContext>();
                return t => c.Resolve(t);
            });
        }

        internal static void InitializeTenant(ILifetimeScope scope, Tenant tenant)
        {
            //TODO: add logging back in
            // var logger = scope.Resolve<ILogger<Startup>>();
            // logger.LogInformation("Initializing tenant {Tenant}", tenant.Name);

            var registry = scope.Resolve<IIdentityRegistry>();
            var config = scope.Resolve<YouverseConfiguration>();
            var tenantContext = scope.Resolve<TenantContext>();

            var isPreconfigured = config?.Development?.PreconfiguredDomains?.Any(d => d.Equals(tenant.Name, StringComparison.InvariantCultureIgnoreCase)) ?? false;

            var reg = registry.Get(tenant.Name).GetAwaiter().GetResult();
            tenantContext.Update(reg.Id, reg.PrimaryDomainName, config.Host.TenantDataRootPath, config.CertificateRenewal.ToCertificateRenewalConfig(), reg.FirstRunToken, isPreconfigured);
        }
    }
}