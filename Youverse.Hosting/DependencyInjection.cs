using System.Runtime.InteropServices;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
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
using Youverse.Hosting.Notifications;
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
            
            cb.RegisterType<SocketConnectionManager>().InstancePerDependency();
            cb.RegisterType<NotificationHandler>().AsSelf().SingleInstance();

            cb.RegisterType<DotYouContext>().AsSelf().SingleInstance();
            cb.RegisterType<DotYouHttpClientFactory>().AsSelf().SingleInstance();

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

            cb.RegisterType<ProfileService>().As<IProfileService>();
            cb.RegisterType<AppRegistrationService>().As<IAppRegistrationService>();
            cb.RegisterType<CircleNetworkService>().As<ICircleNetworkService>();
            cb.RegisterType<OwnerDataAttributeManagementService>().As<IOwnerDataAttributeManagementService>();
            cb.RegisterType<CircleNetworkRequestService>().As<ICircleNetworkRequestService>();
            cb.RegisterType<OwnerDataAttributeReaderService>().As<IOwnerDataAttributeReaderService>();
            cb.RegisterType<MessagingService>().As<IMessagingService>();
            cb.RegisterType<FileBasedStorageService>().As<IStorageService>();
            cb.RegisterType<ChatService>().As<IChatService>();
            cb.RegisterType<EncryptionService>().As<IEncryptionService>();
            cb.RegisterType<OutboxService>().As<IOutboxService>();
            cb.RegisterType<InboxService>().As<IInboxService>();
            cb.RegisterType<MultipartPackageStorageWriter>().As<IMultipartPackageStorageWriter>();
            cb.RegisterType<LiteDbTransitAuditReaderService>().As<ITransitAuditReaderService>();
            cb.RegisterType<LiteDbTransitAuditWriterService>().As<ITransitAuditWriterService>();
            cb.RegisterType<TransferKeyEncryptionQueueService>().As<ITransferKeyEncryptionQueueService>();
            cb.RegisterType<TransitService>().As<ITransitService>();
            cb.RegisterType<TransitQuarantineService>().As<ITransitQuarantineService>();
            cb.RegisterType<TransitPerimeterService>().As<ITransitPerimeterService>();
            cb.RegisterType<PrototrialDemoDataService>().As<IPrototrialDemoDataService>();
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