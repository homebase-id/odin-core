using System;
using System.Runtime.InteropServices;
using System.Security.Claims;
using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization;
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
using Youverse.Services.Messaging;
using Youverse.Services.Messaging.Chat;
using Youverse.Services.Messaging.Demo;
using Youverse.Services.Messaging.Email;
using AppContext = Youverse.Core.Services.Base.AppContext;

namespace Youverse.Hosting
{
    /// <summary>
    /// Extension methods for setting up SignalR services in an <see cref="IServiceCollection" />.
    /// </summary>
    public static class DependencyInjection
    {

        internal static void ConfigureMultiTenantServices(ContainerBuilder cb, Tenant tenant)
        {
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

            ctx.HostDotYouId = (DotYouIdentity)tenant.Name;
            ctx.StorageConfig = registry.ResolveStorageConfig(tenant.Name);
            ctx.TenantCertificate = registry.ResolveCertificate(tenant.Name);
                
            // var caller = new CallerContext(
            //     dotYouId: (DotYouIdentity)user.Identity.Name,
            //     isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
            //     loginDek: chk
            // );
            
            // var appId = user.GetYouverseClaimValue(DotYouClaimTypes.AppId);
            // var deviceUid = user.GetYouverseClaimValue(DotYouClaimTypes.DeviceUid);
            // var app = new AppContext(appId, deviceUid, null, null, false);
        }

        /// <summary>
        /// Gets the DotYouContext for the given Service Scope.
        /// </summary>
        internal static DotYouContext ResolveContext(IServiceProvider svc)
        {
            var accessor = svc.GetRequiredService<IHttpContextAccessor>();
            var reg = svc.GetRequiredService<IIdentityContextRegistry>();

            var httpContext = accessor.HttpContext;
            string hostname = httpContext.Request.Host.Host;
            var cert = reg.ResolveCertificate(hostname);
            var storage = reg.ResolveStorageConfig(hostname);
            var user = httpContext.User;

            //TODO: is there a way to delete the claim's reference to they kek?
            var kek = user.FindFirstValue(DotYouClaimTypes.LoginDek);
            SecureKey chk = kek == null ? null : new SecureKey(Convert.FromBase64String(kek));
            var caller = new CallerContext(
                dotYouId: (DotYouIdentity)user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                loginDek: chk
            );

            //TODO: load with correct app shared key 
            //HACK: !!!
            var appEncryptionKey = new SecureKey(Guid.Empty.ToByteArray());
            var sharedSecretKey = new SecureKey(Guid.Parse("4fc5b0fd-e21e-427d-961b-a2c7a18f18c5").ToByteArray());
            var appId = user.GetYouverseClaimValue(DotYouClaimTypes.AppId);
            var deviceUid = user.GetYouverseClaimValue(DotYouClaimTypes.DeviceUid);
            bool isAdminApp = bool.Parse(user.GetYouverseClaimValue(DotYouClaimTypes.IsAdminApp) ?? bool.FalseString);

            var app = new AppContext(appId, deviceUid, appEncryptionKey, sharedSecretKey, isAdminApp);
            var context = new DotYouContext((DotYouIdentity)hostname, cert, storage, caller, app);

            return context;
        }

        private static string GetYouverseClaimValue(this ClaimsPrincipal user, string name)
        {
            var claim = user.FindFirst(name);
            if (claim?.Issuer != DotYouClaimTypes.YouFoundationIssuer)
            {
                return null;
            }

            return claim?.Value;
        }
    }
}