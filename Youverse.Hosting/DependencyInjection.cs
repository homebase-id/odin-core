using System.IO;
using System.Runtime.InteropServices;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Notifications;
using Youverse.Core.Services.Profile;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Storage;
using Youverse.Core.Services.Tenant;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Inbox;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Services.Messaging.Chat;
using Youverse.Services.Messaging.Demo;
using Youverse.Services.Messaging.Email;

namespace Youverse.Hosting
{
    /// <summary>
    /// Extension methods for setting up SignalR services in an <see cref="IServiceCollection" />.
    /// </summary>
    public static class DependencyInjection
    {
        internal static void ConfigureMultiTenantServices(ContainerBuilder cb, Tenant tenant)
        {
            // cb.RegisterType<CorrelationUniqueIdGenerator>().As<ICorrelationIdGenerator>().SingleInstance();
            // cb.RegisterType<CorrelationContext>().As<ICorrelationContext>().SingleInstance();
            // cb.RegisterType<StickyHostnameGenerator>().As<IStickyHostnameGenerator>().SingleInstance();
            // cb.RegisterType<StickyHostname>().As<IStickyHostname>().SingleInstance();

            cb.RegisterType<LiteDbSystemStorage>().As<ISystemStorage>();

            cb.RegisterType<SocketConnectionManager>().InstancePerDependency();
            cb.RegisterType<NotificationHandler>().AsSelf().SingleInstance();

            cb.RegisterType<DotYouContext>().AsSelf().SingleInstance();
            cb.RegisterType<CertificateResolver>().As<ICertificateResolver>().SingleInstance();
            
            cb.RegisterType<DotYouHttpClientFactory>().As<IDotYouHttpClientFactory>().SingleInstance();
            cb.RegisterType<OwnerSecretService>().As<IOwnerSecretService>().SingleInstance();
            cb.RegisterType<OwnerAuthenticationService>().As<IOwnerAuthenticationService>().SingleInstance();

            cb.RegisterType<ProfileService>().As<IProfileService>().SingleInstance();
            cb.RegisterType<AppRegistrationService>().As<IAppRegistrationService>().SingleInstance();
            cb.RegisterType<CircleNetworkService>().As<ICircleNetworkService>().SingleInstance();
            cb.RegisterType<OwnerDataAttributeManagementService>().As<IOwnerDataAttributeManagementService>().SingleInstance();
            cb.RegisterType<CircleNetworkRequestService>().As<ICircleNetworkRequestService>().SingleInstance();
            cb.RegisterType<OwnerDataAttributeReaderService>().As<IOwnerDataAttributeReaderService>().SingleInstance();
            cb.RegisterType<MessagingService>().As<IMessagingService>().SingleInstance();
            cb.RegisterType<FileBasedStorageService>().As<IStorageService>().SingleInstance();
            cb.RegisterType<ChatService>().As<IChatService>().SingleInstance();
            cb.RegisterType<EncryptionService>().As<IEncryptionService>().SingleInstance();
            cb.RegisterType<OutboxService>().As<IOutboxService>().SingleInstance();
            cb.RegisterType<InboxService>().As<IInboxService>().SingleInstance();
            cb.RegisterType<MultipartPackageStorageWriter>().As<IMultipartPackageStorageWriter>().SingleInstance();
            cb.RegisterType<LiteDbTransitAuditReaderService>().As<ITransitAuditReaderService>().SingleInstance();
            cb.RegisterType<LiteDbTransitAuditWriterService>().As<ITransitAuditWriterService>().SingleInstance();
            cb.RegisterType<TransferKeyEncryptionQueueService>().As<ITransferKeyEncryptionQueueService>().SingleInstance();
            cb.RegisterType<TransitService>().As<ITransitService>().SingleInstance();
            cb.RegisterType<TransitQuarantineService>().As<ITransitQuarantineService>().SingleInstance();
            cb.RegisterType<TransitPerimeterService>().As<ITransitPerimeterService>().SingleInstance();
            cb.RegisterType<PrototrialDemoDataService>().As<IPrototrialDemoDataService>().SingleInstance();
        }

        internal static void InitializeTenant(ILifetimeScope scope, Tenant tenant)
        {
            var logger = scope.Resolve<ILogger<Startup>>();
            logger.LogInformation("Initializing tenant {Tenant}", tenant.Name);

            var registry = scope.Resolve<IIdentityContextRegistry>();
            var config = scope.Resolve<Configuration>();
            var ctx = scope.Resolve<DotYouContext>();
            
            //Note: the rest of DotYouContext will be initialized with DotYouContextMiddleware
            var id = registry.ResolveId(tenant.Name);
            ctx.DotYouReferenceId = id;
            ctx.HostDotYouId = (DotYouIdentity) tenant.Name;

            ctx.DataRoot = Path.Combine(config.Host.TenantDataRootPath, id.ToString());
            ctx.TempDataRoot = Path.Combine(config.Host.TempTenantDataRootPath, id.ToString());
            
            var path = Path.Combine(config.Host.TenantDataRootPath, domainName);
            var tempPath = Path.Combine(config.Host.TempTenantDataRootPath, domainName);
            ctx.StorageConfig = new TenantStorageConfig(Path.Combine(path, "data"), path.Combine(tempPath, "temp"));
        }
    }
}