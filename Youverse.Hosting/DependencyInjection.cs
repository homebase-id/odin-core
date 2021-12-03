using System.Runtime.InteropServices;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
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
            cb.RegisterType<DotYouHttpClientFactory>().As<IDotYouHttpClientFactory>().SingleInstance();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // logger.LogWarning("Running Mac-workaround services");
                cb.RegisterType<MacHackOwnerSecretService>().As<IOwnerSecretService>().SingleInstance();
                cb.RegisterType<MacHackAuthenticationService>().As<IOwnerAuthenticationService>().SingleInstance();
            }
            else
            {
                cb.RegisterType<OwnerSecretService>().As<IOwnerSecretService>().SingleInstance();
                cb.RegisterType<OwnerAuthenticationService>().As<IOwnerAuthenticationService>().SingleInstance();
            }

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
            var registry = scope.Resolve<IIdentityContextRegistry>();

            var ctx = scope.Resolve<DotYouContext>();

            //Note: the rest of DotYouContext will be initialized with DotYouContextMiddleware
            ctx.HostDotYouId = (DotYouIdentity)tenant.Name;
            ctx.StorageConfig = registry.ResolveStorageConfig(tenant.Name);
            ctx.TenantCertificate = registry.ResolveCertificate(tenant.Name);
        }
        
    }
}