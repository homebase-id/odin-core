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
        // private static DotYouContext ResolveContextAutoFac(Tenant tenant)
        // {
        //     // var user = tenant.User;
        //     //
        //     // //TODO: is there a way to delete the claim's reference to they kek?
        //     // var kek = user.FindFirstValue(DotYouClaimTypes.LoginDek);
        //     // SecureKey chk = kek == null ? null : new SecureKey(Convert.FromBase64String(kek));
        //     // var caller = new CallerContext(
        //     //     dotYouId: (DotYouIdentity)user.Identity.Name,
        //     //     isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
        //     //     loginDek: chk
        //     // );
        //     //
        //     // //TODO: load with correct app shared key 
        //     // //HACK: !!!
        //     // var appEncryptionKey = new SecureKey(Guid.Empty.ToByteArray());
        //     // var sharedSecretKey = new SecureKey(Guid.Parse("4fc5b0fd-e21e-427d-961b-a2c7a18f18c5").ToByteArray());
        //     // var appId = user.GetYouverseClaimValue(DotYouClaimTypes.AppId);
        //     // var deviceUid = user.GetYouverseClaimValue(DotYouClaimTypes.DeviceUid);
        //     // bool isAdminApp = bool.Parse(user.GetYouverseClaimValue(DotYouClaimTypes.IsAdminApp) ?? bool.FalseString);
        //     //
        //     // var app = new AppContext(appId, deviceUid, appEncryptionKey, sharedSecretKey, isAdminApp);
        //     // var context = new DotYouContext((DotYouIdentity)tenant.Name, tenant.Certificate, tenant.Storage, caller, app);
        //
        //     return context;
        // }

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


        // public static void AddYouVerseScopedServices(this IServiceCollection services)
        // {
        //     if (services == null)
        //     {
        //         throw new ArgumentNullException(nameof(services));
        //     }
        //
        //     services.AddScoped<DotYouHttpClientFactory>(svc =>
        //     {
        //         var context = ResolveContext(svc);
        //         return new DotYouHttpClientFactory(context);
        //     });
        //
        //     //TODO: Need to move the resolveContext to it's own holder that is Scoped to a request
        //
        //     if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        //     {
        //         services.AddScoped<IOwnerSecretService, MacHackOwnerSecretService>(svc =>
        //         {
        //             return new MacHackOwnerSecretService(
        //                 ResolveContext(svc),
        //                 ResolveLogger<MacHackOwnerSecretService>(svc));
        //         });
        //
        //         services.AddScoped<IOwnerAuthenticationService, MacHackAuthenticationService>(
        //             svc =>
        //             {
        //                 return new MacHackAuthenticationService(
        //                     ResolveContext(svc),
        //                     ResolveLogger<MacHackAuthenticationService>(svc),
        //                     ResolveOwnerSecretService(svc));
        //             });
        //     }
        //     else
        //     {
        //         services.AddScoped<IOwnerSecretService, OwnerSecretService>(svc =>
        //         {
        //             return new OwnerSecretService(
        //                 ResolveContext(svc),
        //                 ResolveLogger<OwnerSecretService>(svc));
        //         });
        //
        //         services.AddScoped<IOwnerAuthenticationService, OwnerAuthenticationService>(
        //             svc =>
        //             {
        //                 return new OwnerAuthenticationService(
        //                     ResolveContext(svc),
        //                     ResolveLogger<OwnerAuthenticationService>(svc),
        //                     ResolveOwnerSecretService(svc));
        //             });
        //     }
        //
        //
        //     services.AddScoped<IProfileService, ProfileService>(svc =>
        //     {
        //         return new ProfileService(
        //             ResolveContext(svc),
        //             ResolveLogger<ProfileService>(svc),
        //             ResolveDotYouHttpClientFactory(svc));
        //     });
        //
        //
        //     services.AddScoped<IAppRegistrationService, AppRegistrationService>(svc =>
        //     {
        //         return new AppRegistrationService(
        //             ResolveContext(svc),
        //             ResolveLogger<AppRegistrationService>(svc),
        //             ResolveNotificationHub(svc),
        //             ResolveDotYouHttpClientFactory(svc));
        //     });
        //
        //
        //     services.AddScoped<ICircleNetworkService, CircleNetworkService>(svc =>
        //     {
        //         return new CircleNetworkService(
        //             ResolveContext(svc),
        //             ResolveProfileService(svc),
        //             ResolveLogger<CircleNetworkService>(svc),
        //             ResolveNotificationHub(svc),
        //             ResolveDotYouHttpClientFactory(svc));
        //     });
        //
        //     services.AddScoped<ICircleNetworkRequestService, CircleNetworkRequestService>(svc =>
        //     {
        //         return new CircleNetworkRequestService(
        //             ResolveContext(svc),
        //             ResolveCircleNetworkService(svc),
        //             ResolveLogger<CircleNetworkRequestService>(svc),
        //             ResolveNotificationHub(svc),
        //             ResolveDotYouHttpClientFactory(svc),
        //             ResolveOwnerDataAttributeManagementService(svc));
        //     });
        //
        //     services.AddScoped<IOwnerDataAttributeManagementService, OwnerDataAttributeManagementService>(svc =>
        //     {
        //         return new OwnerDataAttributeManagementService(
        //             ResolveContext(svc),
        //             ResolveLogger<OwnerDataAttributeManagementService>(svc));
        //     });
        //
        //     services.AddScoped<IOwnerDataAttributeReaderService, OwnerDataAttributeReaderService>(svc =>
        //     {
        //         var context = ResolveContext(svc);
        //         var logger = ResolveLogger<OwnerDataAttributeReaderService>(svc);
        //         var cn = ResolveCircleNetworkService(svc);
        //         return new OwnerDataAttributeReaderService(context, logger, cn);
        //     });
        //
        //     services.AddScoped<IMessagingService, MessagingService>(svc =>
        //     {
        //         var context = ResolveContext(svc);
        //         var logger = ResolveLogger<MessagingService>(svc);
        //         var fac = ResolveDotYouHttpClientFactory(svc);
        //
        //         var msgHub = svc.GetRequiredService<IHubContext<MessagingHub, IMessagingHub>>();
        //         return new MessagingService(context, logger, msgHub, fac);
        //     });
        //
        //     services.AddScoped<IStorageService, FileBasedStorageService>(svc =>
        //     {
        //         var context = ResolveContext(svc);
        //         var logger = ResolveLogger<FileBasedStorageService>(svc);
        //         return new FileBasedStorageService(context, logger);
        //     });
        //
        //
        //     services.AddScoped<IChatService, ChatService>(svc =>
        //     {
        //         var context = ResolveContext(svc);
        //         var logger = ResolveLogger<ChatService>(svc);
        //         var fac = ResolveDotYouHttpClientFactory(svc);
        //         var p = ResolveProfileService(svc);
        //         var cns = ResolveCircleNetworkService(svc);
        //         var ms = ResolveStorageService(svc);
        //
        //         var msgHub = svc.GetRequiredService<IHubContext<MessagingHub, IMessagingHub>>();
        //         return new ChatService(context, logger, fac, p, cns, msgHub, ms);
        //     });
        //
        //     services.AddScoped<IEncryptionService, EncryptionService>(svc => { return new EncryptionService(ResolveContext(svc), ResolveLogger<TransitService>(svc)); });
        //
        //     services.AddScoped<IOutboxService, OutboxService>(svc =>
        //     {
        //         return new OutboxService(
        //             ResolveContext(svc),
        //             ResolveLogger<OutboxService>(svc),
        //             svc.GetRequiredService<IPendingTransfersService>(),
        //             ResolveNotificationHub(svc),
        //             ResolveDotYouHttpClientFactory(svc));
        //     });
        //
        //     services.AddScoped<IInboxService, InboxService>(svc =>
        //     {
        //         return new InboxService(
        //             ResolveContext(svc),
        //             ResolveLogger<InboxService>(svc),
        //             ResolveNotificationHub(svc),
        //             ResolveDotYouHttpClientFactory(svc));
        //     });
        //
        //     services.AddScoped<IMultipartPackageStorageWriter, MultipartPackageStorageWriter>(svc =>
        //     {
        //         return new MultipartPackageStorageWriter(
        //             ResolveContext(svc),
        //             ResolveLogger<MultipartPackageStorageWriter>(svc),
        //             ResolveStorageService(svc),
        //             ResolveEncryptionService(svc));
        //     });
        //
        //     services.AddScoped<ITransitAuditReaderService, LiteDbTransitAuditReaderService>(svc =>
        //     {
        //         return new LiteDbTransitAuditReaderService(
        //             ResolveContext(svc),
        //             ResolveLogger<LiteDbTransitAuditReaderService>(svc));
        //     });
        //
        //     services.AddScoped<ITransitAuditWriterService, LiteDbTransitAuditWriterService>(svc =>
        //     {
        //         return new LiteDbTransitAuditWriterService(
        //             ResolveContext(svc),
        //             ResolveLogger<LiteDbTransitAuditWriterService>(svc));
        //     });
        //
        //     services.AddScoped<ITransferKeyEncryptionQueueService, TransferKeyEncryptionQueueService>(svc =>
        //     {
        //         return new TransferKeyEncryptionQueueService(
        //             ResolveContext(svc),
        //             ResolveLogger<ITransferKeyEncryptionQueueService>(svc));
        //     });
        //
        //     services.AddScoped<ITransitService, TransitService>(svc =>
        //     {
        //         return new TransitService(
        //             ResolveContext(svc),
        //             ResolveLogger<TransitService>(svc),
        //             svc.GetRequiredService<IOutboxService>(),
        //             ResolveStorageService(svc),
        //             ResolveEncryptionService(svc),
        //             svc.GetRequiredService<ITransferKeyEncryptionQueueService>(),
        //             ResolveTransitAuditService(svc),
        //             svc.GetRequiredService<IInboxService>(),
        //             ResolveNotificationHub(svc),
        //             ResolveDotYouHttpClientFactory(svc));
        //     });
        //
        //     services.AddScoped<ITransitQuarantineService, TransitQuarantineService>(svc =>
        //     {
        //         return new TransitQuarantineService(
        //             ResolveContext(svc),
        //             ResolveLogger<TransitQuarantineService>(svc),
        //             ResolveStorageService(svc),
        //             ResolveTransitAuditService(svc)
        //         );
        //     });
        //
        //     services.AddScoped<ITransitPerimeterService, TransitPerimeterService>(svc =>
        //     {
        //         return new TransitPerimeterService(
        //             ResolveContext(svc),
        //             ResolveLogger<TransitPerimeterService>(svc),
        //             ResolveTransitAuditService(svc),
        //             svc.GetRequiredService<ITransitService>(),
        //             svc.GetRequiredService<ITransitQuarantineService>(),
        //             ResolveStorageService(svc)
        //         );
        //     });
        //
        //     services.AddScoped<IPrototrialDemoDataService, PrototrialDemoDataService>(svc =>
        //     {
        //         var context = ResolveContext(svc);
        //         var logger = svc.GetRequiredService<ILogger<ChatService>>();
        //         var cs = svc.GetRequiredService<IProfileService>();
        //         var admin = svc.GetRequiredService<IOwnerDataAttributeManagementService>();
        //         var cnrs = svc.GetRequiredService<ICircleNetworkRequestService>();
        //
        //         return new PrototrialDemoDataService(context, logger, cs, admin, cnrs);
        //     });
        // }

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

        private static ILogger<T> ResolveLogger<T>(IServiceProvider svc)
        {
            return svc.GetRequiredService<ILogger<T>>();
        }

        private static DotYouHttpClientFactory ResolveDotYouHttpClientFactory(IServiceProvider svc)
        {
            return svc.GetRequiredService<DotYouHttpClientFactory>();
        }

        private static IHubContext<NotificationHub, INotificationHub> ResolveNotificationHub(IServiceProvider svc)
        {
            return svc.GetRequiredService<IHubContext<NotificationHub, INotificationHub>>();
        }

        private static IEncryptionService ResolveEncryptionService(IServiceProvider svc)
        {
            return svc.GetRequiredService<IEncryptionService>();
        }

        private static ICircleNetworkRequestService ResolveCircleNetworkRequestService(IServiceProvider svc)
        {
            return svc.GetRequiredService<ICircleNetworkRequestService>();
        }

        private static ICircleNetworkService ResolveCircleNetworkService(IServiceProvider svc)
        {
            return svc.GetRequiredService<ICircleNetworkService>();
        }

        private static IOwnerDataAttributeManagementService ResolveOwnerDataAttributeManagementService(IServiceProvider svc)
        {
            return svc.GetRequiredService<IOwnerDataAttributeManagementService>();
        }

        private static IProfileService ResolveProfileService(IServiceProvider svc)
        {
            return svc.GetRequiredService<IProfileService>();
        }

        private static ITransitAuditWriterService ResolveTransitAuditService(IServiceProvider svc)
        {
            return svc.GetRequiredService<ITransitAuditWriterService>();
        }

        private static IStorageService ResolveStorageService(IServiceProvider svc)
        {
            return svc.GetRequiredService<IStorageService>();
        }

        private static IOwnerSecretService ResolveOwnerSecretService(IServiceProvider svc)
        {
            return svc.GetRequiredService<IOwnerSecretService>();
        }
    }
}