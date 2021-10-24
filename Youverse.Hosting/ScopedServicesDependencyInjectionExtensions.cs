using System;
using System.Security.Claims;
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
using Youverse.Core.Services.Transit;
using Youverse.Services.Messaging;
using Youverse.Services.Messaging.Chat;
using Youverse.Services.Messaging.Demo;
using Youverse.Services.Messaging.Email;

namespace Youverse.Hosting
{
    /// <summary>
    /// Extension methods for setting up SignalR services in an <see cref="IServiceCollection" />.
    /// </summary>
    public static class ScopedServicesDependencyInjectionExtensions
    {
        /// <summary>
        /// Configures scoped services
        /// </summary>
        /// <param name="services"></param>
        public static void AddYouVerseScopedServices(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }


            services.AddScoped<DotYouHttpClientFactory>(svc =>
            {
                var context = ResolveContext(svc);
                return new DotYouHttpClientFactory(context);
            });

            //TODO: Need to move the resolveContext to it's own holder that is Scoped to a request

            services.AddScoped<IOwnerSecretService, OwnerSecretService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<OwnerSecretService>(svc);
                return new OwnerSecretService(context, logger);
            });

            services.AddScoped<IOwnerAuthenticationService, OwnerAuthenticationService>(
                svc =>
                {
                    var context = ResolveContext(svc);
                    var logger = ResolveLogger<OwnerAuthenticationService>(svc);
                    return new OwnerAuthenticationService(context, logger, ResolveOwnerSecretService(svc));
                });

            services.AddScoped<IAppRegistrationService, AppRegistrationService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<AppRegistrationService>(svc);
                var fac = ResolveDotYouHttpClientFactory(svc);
                var hub = ResolveNotificationHub(svc);
                return new AppRegistrationService(context, logger, hub, fac);
            });

            services.AddScoped<IProfileService, ProfileService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<ProfileService>(svc);
                var fac = ResolveDotYouHttpClientFactory(svc);
                return new ProfileService(context, logger, fac);
            });

            services.AddScoped<ICircleNetworkService, CircleNetworkService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<CircleNetworkService>(svc);
                var profileSvc = ResolveProfileService(svc);
                var fac = ResolveDotYouHttpClientFactory(svc);
                var hub = ResolveNotificationHub(svc);
                return new CircleNetworkService(context, profileSvc, logger, hub, fac);
            });

            services.AddScoped<ICircleNetworkRequestService, CircleNetworkRequestService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<CircleNetworkService>>();
                var cns = ResolveCircleNetworkService(svc);
                var fac = ResolveDotYouHttpClientFactory(svc);
                var hub = ResolveNotificationHub(svc);
                var mgt = ResolveOwnerDataAttributeManagementService(svc);
                return new CircleNetworkRequestService(context, cns, logger, hub, fac, mgt);
            });

            services.AddScoped<IOwnerDataAttributeManagementService, OwnerDataAttributeManagementService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<OwnerAuthenticationService>>();

                return new OwnerDataAttributeManagementService(context, logger);
            });

            services.AddScoped<IOwnerDataAttributeReaderService, OwnerDataAttributeReaderService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<OwnerDataAttributeReaderService>(svc);
                var cn = ResolveCircleNetworkService(svc);
                return new OwnerDataAttributeReaderService(context, logger, cn);
            });

            services.AddScoped<IMessagingService, MessagingService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<MessagingService>(svc);
                var fac = ResolveDotYouHttpClientFactory(svc);

                var msgHub = svc.GetRequiredService<IHubContext<MessagingHub, IMessagingHub>>();
                return new MessagingService(context, logger, msgHub, fac);
            });

            services.AddScoped<IStorageService, FileBasedStorageService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<FileBasedStorageService>(svc);
                return new FileBasedStorageService(context, logger);
            });

            services.AddScoped<IChatService, ChatService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<ChatService>(svc);
                var fac = ResolveDotYouHttpClientFactory(svc);
                var p = ResolveProfileService(svc);
                var cns = ResolveCircleNetworkService(svc);
                var ms = ResolveStorageService(svc);

                var msgHub = svc.GetRequiredService<IHubContext<MessagingHub, IMessagingHub>>();
                return new ChatService(context, logger, fac, p, cns, msgHub, ms);
            });

            services.AddScoped<IOutboxQueueService, OutboxQueueService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<OutboxQueueService>(svc);
                var fac = ResolveDotYouHttpClientFactory(svc);
                var hub = ResolveNotificationHub(svc);
                return new OutboxQueueService(context, logger, hub, fac);
            });

            services.AddScoped<IMultipartParcelBuilder, MultipartParcelBuilder>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<MultipartParcelBuilder>(svc);
                var storage = ResolveStorageService(svc);
                return new MultipartParcelBuilder(context, logger, storage);
            });

            services.AddScoped<ITransitService, TransitService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = ResolveLogger<TransitService>(svc);
                var fac = ResolveDotYouHttpClientFactory(svc);
                var ss = ResolveStorageService(svc);
                var box = svc.GetRequiredService<IOutboxQueueService>();
                var hub = ResolveNotificationHub(svc);
                return new TransitService(context, logger, box, ss, hub, fac);
            });

            services.AddScoped<IPrototrialDemoDataService, PrototrialDemoDataService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<ChatService>>();
                var cs = svc.GetRequiredService<IProfileService>();
                var admin = svc.GetRequiredService<IOwnerDataAttributeManagementService>();
                var cnrs = svc.GetRequiredService<ICircleNetworkRequestService>();

                return new PrototrialDemoDataService(context, logger, cs, admin, cnrs);
            });
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
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower()),
                loginDek: chk
            );

            var context = new DotYouContext((DotYouIdentity) hostname, cert, storage, caller);
            return context;
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