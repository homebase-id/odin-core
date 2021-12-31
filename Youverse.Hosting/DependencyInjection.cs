using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Autofac;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.AppAuth;
using Youverse.Core.Services.Authentication.Owner;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Security;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Notifications;
using Youverse.Core.Services.Profile;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Registry.Provisioning;
using Youverse.Core.Services.Tenant;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Inbox;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.Owner.Demo;

namespace Youverse.Hosting
{
    /// <summary>
    /// Set up per-tenant services
    /// </summary>
    public static class DependencyInjection
    {
        internal static void ConfigureMultiTenantServices(ContainerBuilder cb, Tenant tenant)
        {
            cb.RegisterType<LiteDbSystemStorage>().As<ISystemStorage>();

            cb.RegisterType<SocketConnectionManager>().InstancePerDependency();
            cb.RegisterType<NotificationHandler>().AsSelf().SingleInstance();

            cb.RegisterType<DotYouContext>().AsSelf().SingleInstance();
            cb.RegisterType<CertificateResolver>().As<ICertificateResolver>().SingleInstance();
            cb.RegisterType<DotYouHttpClientFactory>().As<IDotYouHttpClientFactory>().SingleInstance();

            cb.RegisterType<YouAuthService>().As<IYouAuthService>().SingleInstance();
            cb.RegisterType<YouAuthSessionManager>().As<IYouAuthSessionManager>().SingleInstance();
            cb.RegisterType<YouAuthSessionStorage>().As<IYouAuthSessionStorage>().SingleInstance();
            cb.RegisterType<YouAuthAuthorizationCodeManager>().As<IYouAuthAuthorizationCodeManager>().SingleInstance();

            cb.RegisterType<AppAuthenticationService>().As<IAppAuthenticationService>().SingleInstance();
            
            cb.RegisterType<DotYouHttpClientFactory>().As<IDotYouHttpClientFactory>().SingleInstance();
            cb.RegisterType<OwnerSecretService>().As<IOwnerSecretService>().SingleInstance();
            cb.RegisterType<OwnerAuthenticationService>().As<IOwnerAuthenticationService>().SingleInstance();

            cb.RegisterType<GranteeResolver>().As<IGranteeResolver>().SingleInstance();
            cb.RegisterType<DriveService>().As<IDriveService>().SingleInstance();
            cb.RegisterType<DriveQueryService>().As<IDriveQueryService>().SingleInstance();
            cb.RegisterType<ProfileService>().As<IProfileService>().SingleInstance();
            cb.RegisterType<AppRegistrationService>().As<IAppRegistrationService>().SingleInstance();
            cb.RegisterType<CircleNetworkService>().As<ICircleNetworkService>().SingleInstance();
            cb.RegisterType<ProfileAttributeManagementService>().As<IProfileAttributeManagementService>().SingleInstance();
            cb.RegisterType<CircleNetworkRequestService>().As<ICircleNetworkRequestService>().SingleInstance();
            cb.RegisterType<OutboxService>().As<IOutboxService>().SingleInstance();
            cb.RegisterType<InboxService>().As<IInboxService>().SingleInstance();
            cb.RegisterType<MultipartPackageStorageWriter>().As<IMultipartPackageStorageWriter>().SingleInstance();
            cb.RegisterType<LiteDbTransitAuditReaderService>().As<ITransitAuditReaderService>().SingleInstance();
            cb.RegisterType<LiteDbTransitAuditWriterService>().As<ITransitAuditWriterService>().SingleInstance();
            cb.RegisterType<TransferKeyEncryptionQueueService>().As<ITransferKeyEncryptionQueueService>().SingleInstance();
            cb.RegisterType<TransitService>().As<ITransitService>().SingleInstance();
            cb.RegisterType<TransitQuarantineService>().As<ITransitQuarantineService>().SingleInstance();
            cb.RegisterType<TransitPerimeterService>().As<ITransitPerimeterService>().SingleInstance();
            cb.RegisterType<DemoDataGenerator>().SingleInstance();
            
            cb.RegisterType<IdentityProvisioner>().As<IIdentityProvisioner>().SingleInstance();

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
            ctx.DotYouRegistryId = id;
            ctx.HostDotYouId = (DotYouIdentity)tenant.Name;

            ctx.DataRoot = Path.Combine(config.Host.TenantDataRootPath, id.ToString());
            ctx.TempDataRoot = Path.Combine(config.Host.TempTenantDataRootPath, id.ToString());
            ctx.StorageConfig = new TenantStorageConfig(Path.Combine(ctx.DataRoot, "data"), Path.Combine(ctx.TempDataRoot, "temp"));
        }
    }
}