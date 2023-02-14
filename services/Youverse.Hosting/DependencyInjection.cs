using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using Autofac;
using MediatR;
using MediatR.Pipeline;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
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
using Youverse.Core.Services.Contacts.Circle.Notification;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.DataSubscription;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Comment;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Tenant;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Quarantine;
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

            cb.RegisterType<TenantSystemStorage>().As<ITenantSystemStorage>().SingleInstance();

            cb.RegisterType<AppNotificationHandler>()
                .As<INotificationHandler<FileAddedNotification>>()
                .As<INotificationHandler<ConnectionRequestReceived>>()
                .As<INotificationHandler<ConnectionRequestAccepted>>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .As<INotificationHandler<DriveFileChangedNotification>>()
                .As<INotificationHandler<DriveFileDeletedNotification>>()
                .As<INotificationHandler<TransitFileReceivedNotification>>()
                .As<INotificationHandler<NewFollowerNotification>>()
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
            
            // cb.RegisterType<StandardFileDriveUploadService>().AsSelf().InstancePerDependency();
            cb.RegisterType<StandardFileStreamWriter>().AsSelf().InstancePerDependency();
            cb.RegisterType<StandardFileDriveStorageService>().AsSelf().As<IDriveStorageService>().InstancePerDependency();
            cb.RegisterType<StandardFileDriveQueryService>().AsSelf().As<IDriveQueryService>().InstancePerDependency();
            cb.RegisterType<StandardDriveCommandService>().AsSelf().InstancePerDependency();
            cb.RegisterType<StandardFileSystem>().AsSelf().InstancePerDependency();
            
            // cb.RegisterType<CommentFileUploadService>().AsSelf().InstancePerDependency();
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

            cb.RegisterType<OutboxService>().As<IOutboxService>().SingleInstance();

            cb.RegisterType<TransitAppService>().As<ITransitAppService>().SingleInstance();
            cb.RegisterType<TransitRegistrationService>()
                .As<INotificationHandler<IdentityConnectionRegistrationChangedNotification>>()
                .AsSelf()
                .SingleInstance();

            cb.RegisterType<DataProviderAuthenticationService>().AsSelf().SingleInstance();
            cb.RegisterType<DataSubscriptionDistributionService>()
                .As<INotificationHandler<DriveFileAddedNotification>>()
                .AsSelf()
                .SingleInstance();
            
            cb.RegisterType<TransferKeyEncryptionQueueService>().As<ITransferKeyEncryptionQueueService>().SingleInstance();
            cb.RegisterType<TransitBoxService>().As<ITransitBoxService>().SingleInstance();
            cb.RegisterType<TransitService>().As<ITransitService>().SingleInstance();
            cb.RegisterType<TransitPerimeterService>().As<ITransitPerimeterService>().SingleInstance();
            cb.RegisterType<TransitPerimeterTransferStateService>().As<ITransitPerimeterTransferStateService>().SingleInstance();

            cb.RegisterType<CommandMessagingService>().AsSelf().SingleInstance();

            cb.RegisterType<AppService>().As<IAppService>().SingleInstance();

            cb.RegisterType<ExchangeGrantService>().AsSelf().SingleInstance();

            cb.RegisterType<TransitQueryService>().AsSelf().SingleInstance();

            cb.RegisterType<RsaKeyService>().As<IPublicKeyService>().SingleInstance();

            cb.RegisterType<CircleNetworkNotificationService>();

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