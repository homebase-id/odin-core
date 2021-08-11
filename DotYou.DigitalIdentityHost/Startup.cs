using System;
using System.ComponentModel;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DotYou.DigitalIdentityHost.Controllers.Perimeter;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Kernel.Services.Circle;
using DotYou.Kernel.Services.Contacts;
using DotYou.Kernel.Services.DataAttribute;
using DotYou.Kernel.Services.Demo;
using DotYou.Kernel.Services.Identity;
using DotYou.Kernel.Services.Messaging.Chat;
using DotYou.Kernel.Services.Messaging.Email;
using DotYou.Kernel.Services.Owner.Authentication;
using DotYou.Kernel.Services.Owner.Data;
using DotYou.Kernel.Services.Profile;
using DotYou.TenantHost;
using DotYou.TenantHost.Security;
using DotYou.TenantHost.Security.Authentication;
using DotYou.Types;
using DotYou.Types.SignalR;
using LiteDB;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotYou.DigitalIdentityHost
{
    public class Startup
    {
        const string YouFoundationIssuer = "YouFoundation";

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers(config =>
                {
                    config.Filters.Add(new ApplyPerimeterMetaData());
                    //config.OutputFormatters.RemoveType<HttpNoContentOutputFormatter>(); //removes content type when 204 is returned.
                }
            ).AddJsonOptions(options => { options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

            //services.AddRazorPages(options => { options.RootDirectory = "/Views"; });

            //Note: this product is designed to avoid use of the HttpContextAccessor in the services
            //All params should be passed into to the services using DotYouContext
            services.AddHttpContextAccessor();

            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = DotYouAuthConstants.ExternalDigitalIdentityClientCertificateScheme;
                    options.DefaultChallengeScheme = DotYouAuthConstants.ExternalDigitalIdentityClientCertificateScheme;
                })
                .AddScheme<DotIdentityOwnerAuthenticationSchemeOptions, DotIdentityOwnerAuthenticationHandler>(DotYouAuthConstants.DotIdentityOwnerScheme, op => { op.LoginUri = "/login"; })
                .AddCertificate(DotYouAuthConstants.ExternalDigitalIdentityClientCertificateScheme, options =>
                {
                    options.AllowedCertificateTypes = CertificateTypes.Chained;
                    options.ValidateCertificateUse = true;

                    //options.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                    //options.RevocationMode = X509RevocationMode.NoCheck

                    options.Events = new CertificateAuthenticationEvents()
                    {
                        OnCertificateValidated = this.CertificateValidated,
                        OnAuthenticationFailed = this.HandleAuthenticationFailed,
                        OnChallenge = this.HandleCertificateChallenge
                    };
                })
                //TODO: this certificate cache is not multi-tenant
                .AddCertificateCache(options =>
                {
                    //TODO: revist this to see if it will serve as our re-validation method to ensure
                    // caller certs are still good 
                    options.CacheSize = 2048;
                    options.CacheEntryExpiration = TimeSpan.FromMinutes(10);
                });

            services.AddAuthorization(options => new PolicyConfig().AddPolicies(options));

            services.AddMemoryCache();
            services.AddSignalR(options => { options.EnableDetailedErrors = true; });

            services.AddScoped<DotYouHttpClientFactory>(svc =>
            {
                var context = ResolveContext(svc);
                return new DotYouHttpClientFactory(context);
            });

            //TODO: Need to move the resolveContext to it's own holder that is Scoped to a request

            services.AddScoped<IOwnerSecretService, OwnerSecretService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<OwnerSecretService>>();
                return new OwnerSecretService(context, logger);
            });

            services.AddScoped<IOwnerAuthenticationService, OwnerAuthenticationService>(
                svc =>
                {
                    var context = ResolveContext(svc);
                    var logger = svc.GetRequiredService<ILogger<OwnerAuthenticationService>>();
                    var ss = svc.GetRequiredService<IOwnerSecretService>();

                    return new OwnerAuthenticationService(context, logger, ss);
                });

            services.AddScoped<IProfileService, ProfileService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<ProfileService>>();
                return new ProfileService(context, logger);
            });

            
            services.AddScoped<ICircleNetworkRequestService, CircleNetworkRequestService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<CircleNetworkService>>();
                var cns = svc.GetRequiredService<ICircleNetworkService>();
                var fac = svc.GetRequiredService<DotYouHttpClientFactory>();
                var hub = svc.GetRequiredService<IHubContext<NotificationHub, INotificationHub>>();
                var mgt = svc.GetRequiredService<IOwnerDataAttributeManagementService>();
                return new CircleNetworkRequestService(context, cns, logger, hub, fac, mgt);
            });
            
            services.AddScoped<ICircleNetworkService, CircleNetworkService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<CircleNetworkService>>();
                var profileSvc = svc.GetRequiredService<IProfileService>();
                var fac = svc.GetRequiredService<DotYouHttpClientFactory>();
                var hub = svc.GetRequiredService<IHubContext<NotificationHub, INotificationHub>>();
                return new CircleNetworkService(context, profileSvc, logger, hub, fac);
            });

            services.AddScoped<IOwnerDataAttributeManagementService, OwnerDataAttributeManagementService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<OwnerAuthenticationService>>();

                return new OwnerDataAttributeManagementService(context, logger);
            });

            services.AddScoped<IMessagingService, MessagingService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<MessagingService>>();
                var fac = svc.GetRequiredService<DotYouHttpClientFactory>();
                var hub = svc.GetRequiredService<IHubContext<NotificationHub, INotificationHub>>();

                return new MessagingService(context, logger, hub, fac);
            });

            services.AddScoped<IChatService, ChatService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<ChatService>>();
                var fac = svc.GetRequiredService<DotYouHttpClientFactory>();
                var hub = svc.GetRequiredService<IHubContext<NotificationHub, INotificationHub>>();
                var p = svc.GetRequiredService<IProfileService>();
                var cns = svc.GetRequiredService<ICircleNetworkService>();
                return new ChatService(context, logger, hub, fac, p, cns);
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

            // In production, the React files will be served from this directory
            services.AddSpaStaticFiles(configuration => { configuration.RootPath = "ClientApp/build"; });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            this.ConfigureLiteDBSerialization();

            if (env.IsDevelopment())
            {
                //app.UseWebAssemblyDebugging();
                app.UseDeveloperExceptionPage();
            }

            app.UseMiddleware<ExceptionMiddleware>();
            app.UseStaticFiles();
            app.UseSpaStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                //endpoints.MapControllerRoute("api", "api/{controller}/{action=Index}/{id?}");
                //endpoints.MapFallbackToFile("index.html");
                endpoints.MapHub<NotificationHub>("/api/live/notifications", o =>
                {
                    //TODO: for #prototrial, i narrowed this to websockets
                    //only so i could disable negotiation from the client
                    //as it was causing issues with authentication.
                    o.Transports = HttpTransportType.WebSockets;
                });
            });

            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "ClientApp";
                if (env.IsDevelopment())
                {
                    spa.UseReactDevelopmentServer(npmScript: "start");
                }
            });
        }

        /// <summary>
        /// Gets the DotYouContext for the given Service Scope.
        /// </summary>
        private DotYouContext ResolveContext(IServiceProvider svc)
        {
            var accessor = svc.GetRequiredService<IHttpContextAccessor>();
            var reg = svc.GetRequiredService<IIdentityContextRegistry>();

            var httpContext = accessor.HttpContext;

            string hostname = httpContext.Request.Host.Host;
            var cert = reg.ResolveCertificate(hostname);
            var storage = reg.ResolveStorageConfig(hostname);

            var user = httpContext.User;
            var caller = new CallerContext(
                dotYouId: (DotYouIdentity) user.Identity.Name,
                isOwner: user.HasClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower())
            );

            var context = new DotYouContext((DotYouIdentity) hostname, cert, storage, caller);
            return context;
        }

        private Task CertificateValidated(CertificateValidatedContext context)
        {
            //todo: call out to additional service to validate more rules
            // i.e. blocked list
            // determine if this certificate is in my network an add extra claims
            //add system circle claim based on my relationship to this person
            //lookup name for the individual and add to the claims

            bool isTenantOwner = false;
            var dotYouContext = ResolveContext(context.HttpContext.RequestServices);
            using (var serverCertificate = dotYouContext.TenantCertificate.LoadCertificateWithPrivateKey())
            {
                //HACK: this is not sufficient for establishing the client and server certificates are the same.
                //https://eprint.iacr.org/2019/130.pdf - first few paragraphs
                isTenantOwner = serverCertificate.Thumbprint == context.ClientCertificate.Thumbprint;
            }

            //By logging in with a client certificate for this #prototrial, you are identified
            bool isIdentified = true;

            var bytes = context.ClientCertificate.Export(X509ContentType.Pkcs12);
            string clientCertificatePortable = Convert.ToBase64String(bytes);

            //TODO: PROTOTRIAL: assumes the certificate has a format where the domain is a common name. revisit
            string domain = CertificateUtils.GetDomainFromCommonName(context.ClientCertificate.Subject);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
                new Claim(DotYouClaimTypes.IsIdentityOwner, isTenantOwner.ToString().ToLower(), ClaimValueTypes.Boolean, YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsIdentified, isIdentified.ToString().ToLower(), ClaimValueTypes.Boolean, YouFoundationIssuer),

                //HACK: I don't know if this is a good idea to put this whole thing in the claims
                new Claim(DotYouClaimTypes.PublicKeyCertificate, clientCertificatePortable, ClaimValueTypes.String, YouFoundationIssuer)
            };

            context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
            context.Success();
            return Task.CompletedTask;
        }

        private Task HandleAuthenticationFailed(CertificateAuthenticationFailedContext context)
        {
            Console.WriteLine("Authentication Failed");
            return Task.CompletedTask;
        }

        private Task HandleCertificateChallenge(CertificateChallengeContext context)
        {
            Console.WriteLine("handle cert challenge");
            return Task.CompletedTask;
        }

        private void ConfigureLiteDBSerialization()
        {
            var serialize = new Func<DotYouIdentity, BsonValue>(identity => identity.ToString());
            var deserialize = new Func<BsonValue, DotYouIdentity>(bson => new DotYouIdentity(bson.AsString));

            //see: Register our custom type @ https://www.litedb.org/docs/object-mapping/   
            BsonMapper.Global.RegisterType<DotYouIdentity>(
                serialize: serialize,
                deserialize: deserialize
            );

            BsonMapper.Global.ResolveMember = (type, memberInfo, memberMapper) =>
            {
                if (memberMapper.DataType == typeof(DotYouIdentity))
                {
                    //memberMapper.Serialize = (obj, mapper) => new BsonValue(((DotYouIdentity) obj).ToString());
                    memberMapper.Serialize = (obj, mapper) => serialize((DotYouIdentity) obj);
                    memberMapper.Deserialize = (value, mapper) => deserialize(value);
                }
            };

            // BsonMapper.Global.Entity<NoncePackage>()
            //     .Id(x => new Guid(Convert.FromBase64String(x.Nonce64)));
        }
    }
}