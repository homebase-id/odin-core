using System;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Autofac;
using Dawn;
using LiteDB;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Logging;
using Youverse.Core.Services.Tenant;
using Youverse.Core.Services.Transit.Background;
using Youverse.Core.Util;
using Youverse.Hosting.Controllers.Perimeter;
using Youverse.Hosting.Middleware.Logging;
using Youverse.Hosting.Multitenant;
using Youverse.Hosting.Security;
using Youverse.Hosting.Security.Authentication;
using Youverse.Services.Messaging.Chat;

namespace Youverse.Hosting
{
    public class Startup
    {
        const string YouFoundationIssuer = "YouFoundation";

        private IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMultiTenancy();
            services.AddLoggingServices();
            
            var config = this.Configuration.GetSection("Config").Get<Config>();
            AssertValidConfiguration(config);
            PrepareEnvironment(config);

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
                    options.ValidateCertificateUse = false; //HACK: to work around the fact that ISRG Root X1 is not set for Client Certificate authentication

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
                    //TODO: revisit this to see if it will serve as our re-validation method to ensure
                    // caller certs are still good 
                    options.CacheSize = 2048;
                    options.CacheEntryExpiration = TimeSpan.FromMinutes(10);
                });

            services.AddAuthorization(options => new PolicyConfig().AddPolicies(options));

            services.AddMemoryCache();
            services.AddSignalR(options => { options.EnableDetailedErrors = true; });

            services.AddYouVerseScopedServices();
            services.AddHostedService<BackgroundOutboxTransferService>();
                        
            // In production, the React files will be served from this directory
            services.AddSpaStaticFiles(configuration => { configuration.RootPath = "ClientApp/build"; });
        }
        
        // ConfigureContainer is where you can register things directly
        // with Autofac. This runs after ConfigureServices so the things
        // here will override registrations made in ConfigureServices.
        // Don't build the container; that gets done for you. If you
        // need a reference to the container, you need to use the
        // "Without ConfigureContainer" mechanism shown later.
         public void ConfigureContainer(ContainerBuilder builder)
         {
             /*

             AUTOFAC CHEAT SHEET (https://stackoverflow.com/questions/42809618/migration-from-asp-net-cores-container-to-autofac)

             ASP.NET Core container             -> Autofac
             ----------------------                -------

             // the 3 big ones
             services.AddSingleton<IFoo, Foo>() -> builder.RegisterType<Foo>().As<IFoo>().SingleInstance()
             services.AddScoped<IFoo, Foo>()    -> builder.RegisterType<Foo>().As<IFoo>().InstancePerLifetimeScope()
             services.AddTransient<IFoo, Foo>() -> builder.RegisterType<Foo>().As<IFoo>().InstancePerDependency()

             // default
             services.AddTransient<IFoo, Foo>() -> builder.RegisterType<Foo>().As<IFoo>()

             // multiple
             services.AddX<IFoo1, Foo>();
             services.AddX<IFoo2, Foo>();       -> builder.RegisterType<Foo>().As<IFoo1>().As<IFoo2>().X()

             // without interface
             services.AddX<Foo>()               -> builder.RegisterType<Foo>().AsSelf().X()

             */
             
             // This will all go in the ROOT CONTAINER and is NOT TENANT SPECIFIC.
             builder.RegisterType<Controllers.Test.TenantDependencyTest2>().As<Controllers.Test.ITenantDependencyTest2>().SingleInstance();
        }
        
        internal static void ConfigureMultiTenantServices(ContainerBuilder cb, Tenant tenant)
        {
            cb.RegisterType<Controllers.Test.TenantDependencyTest>().As<Controllers.Test.ITenantDependencyTest>().SingleInstance();
        }

        internal static void InitializeTenant(ILifetimeScope scope, Tenant tenant)
        {
            var logger = scope.Resolve<ILogger<Startup>>();
            var test = scope.Resolve<Controllers.Test.ITenantDependencyTest>();
            logger.LogInformation(test.Hello($"I am scoped to tenant {tenant} and I will be disposed!"));            
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            app.UseLoggingMiddleware();
            app.UseMultiTenancy();
            
            this.ConfigureLiteDBSerialization();

            if (env.IsDevelopment())
            {
                //app.UseWebAssemblyDebugging();
                app.UseDeveloperExceptionPage();
            }

            app.UseCertificateForwarding();
            app.UseMiddleware<ExceptionMiddleware>();
            app.UseStaticFiles();
            app.UseSpaStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                // endpoints.Map("/", async context =>
                // {
                //     await context.Response.WriteAsync("wrinkle");
                // });

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

                endpoints.MapHub<MessagingHub>("/api/live/chat", o =>
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

        private Task CertificateValidated(CertificateValidatedContext context)
        {
            //todo: call out to additional service to validate more rules
            // i.e. blocked list
            // determine if this certificate is in my network an add extra claims
            //add system circle claim based on my relationship to this person
            //lookup name for the individual and add to the claims

            bool isTenantOwner = false;
            var dotYouContext = ScopedServicesDependencyInjectionExtensions.ResolveContext(context.HttpContext.RequestServices);
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

            // BsonMapper.Global.Entity<DotYouProfile>()
            //     .Id(x => x.DotYouId);
            // BsonMapper.Global.Entity<NoncePackage>()
            //     .Id(x => new Guid(Convert.FromBase64String(x.Nonce64)));
        }

        private void AssertValidConfiguration(Config cfg)
        {
            Guard.Argument(cfg, nameof(cfg)).NotNull();
            Guard.Argument(cfg.RegistryServerUri, nameof(cfg.RegistryServerUri)).NotNull().NotEmpty();
            Guard.Argument(Uri.IsWellFormedUriString(cfg.RegistryServerUri, UriKind.Absolute), nameof(cfg.RegistryServerUri)).True();
            Guard.Argument(cfg.TenantDataRootPath, nameof(cfg.TenantDataRootPath)).NotNull().NotEmpty();
            Guard.Argument(cfg.TempTenantDataRootPath, nameof(cfg.TempTenantDataRootPath)).NotNull().NotEmpty();
        }

        private void PrepareEnvironment(Config cfg)
        {
            Directory.CreateDirectory(cfg.TenantDataRootPath);
            Directory.CreateDirectory(cfg.TempTenantDataRootPath);
        }
    }
}