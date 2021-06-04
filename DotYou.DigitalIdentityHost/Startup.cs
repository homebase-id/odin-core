using System;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DotYou.DigitalIdentityHost.Controllers.Incoming;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Cryptography;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Kernel.Services.Admin.IdentityManagement;
using DotYou.Kernel.Services.Circle;
using DotYou.Kernel.Services.Contacts;
using DotYou.Kernel.Services.Identity;
using DotYou.Kernel.Services.Messaging.Chat;
using DotYou.Kernel.Services.Messaging.Email;
using DotYou.TenantHost;
using DotYou.TenantHost.Security;
using DotYou.TenantHost.Security.Authentication;
using DotYou.Types;
using DotYou.Types.Messaging;
using DotYou.Types.SignalR;
using LiteDB;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Converters;

namespace DotYou.DigitalIdentityHost
{
    public class Startup
    {
        const string YouFoundationIssuer = "YouFoundation";

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers(config =>
                {
                    config.Filters.Add(new ApplyIncomingMetaData());
                    config.OutputFormatters.RemoveType<HttpNoContentOutputFormatter>(); //removes content type when 204 is returned.
                }
            ).AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

            //services.AddRazorPages(options => { options.RootDirectory = "/Views"; });

            //Note: this product is designed to avoid use of the HttpContextAccessor in the services
            //All params should be passed into to the services using DotYouContext
            services.AddHttpContextAccessor();

            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = DotYouAuthSchemes.ExternalDigitalIdentityClientCertificate;
                    options.DefaultChallengeScheme = DotYouAuthSchemes.ExternalDigitalIdentityClientCertificate;
                    
                })
                .AddScheme<DotIdentityOwnerAuthenticationSchemeOptions, DotIdentityOwnerAuthenticationHandler>(DotYouAuthSchemes.DotIdentityOwner, op => { op.LoginUri = "/login"; })
                .AddCertificate(DotYouAuthSchemes.ExternalDigitalIdentityClientCertificate, options =>
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

            services.AddScoped<IAdminIdentityAttributeService, AdminAdminIdentityAttributeService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<OwnerAuthenticationService>>();

                return new AdminAdminIdentityAttributeService(context, logger);
            });

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

            services.AddScoped<IContactService, ContactService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<ContactService>>();
                return new ContactService(context, logger);
            });

            services.AddScoped<ICircleNetworkService, CircleNetworkService>(svc =>
            {
                var context = ResolveContext(svc);
                var logger = svc.GetRequiredService<ILogger<CircleNetworkService>>();
                var contactSvc = svc.GetRequiredService<IContactService>();
                var fac = svc.GetRequiredService<DotYouHttpClientFactory>();
                var hub = svc.GetRequiredService<IHubContext<NotificationHub, INotificationHub>>();
                return new CircleNetworkService(context, contactSvc, logger, hub, fac);
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
                var cs = svc.GetRequiredService<IContactService>();
                return new ChatService(context, logger, hub, fac, cs);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            this.ConfigureLiteDBSerialization();

            if (env.IsDevelopment())
            {
                app.UseWebAssemblyDebugging();
                app.UseDeveloperExceptionPage();
            }

            app.UseMiddleware<ExceptionMiddleware>();
            app.UseStaticFiles();
            app.UseBlazorFrameworkFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapFallbackToFile("index.html");
                endpoints.MapHub<NotificationHub>("/live/notifications", o =>
                {
                    //TODO: for #prototrial, i narrowed this to websockets
                    //only so i could disable negotiation from the client
                    //as it was causing issues with authentication.
                    o.Transports = HttpTransportType.WebSockets;
                });
            });
        }

        /// <summary>
        /// Gets the DotYouContext for the given Service Scope.
        /// </summary>
        private DotYouContext ResolveContext(IServiceProvider svc)
        {
            var accessor = svc.GetRequiredService<IHttpContextAccessor>();
            var reg = svc.GetRequiredService<IIdentityContextRegistry>();

            var context = reg.ResolveContext(accessor.HttpContext.Request.Host.Host);
            return context;
        }

        private Task CertificateValidated(CertificateValidatedContext context)
        {
            //todo: call out to additonal service to validate more rules
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
                    memberMapper.Serialize = (obj, mapper) => serialize((DotYouIdentity)obj);
                    memberMapper.Deserialize = (value, mapper) => deserialize(value);
                }
            };

            // BsonMapper.Global.Entity<NoncePackage>()
            //     .Id(x => new Guid(Convert.FromBase64String(x.Nonce64)));
        }
    }
}