using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Dawn;
using DnsClient;
using HttpClientFactoryLite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Background.Certificate;
using Odin.Core.Services.Background.DefaultCron;
using Odin.Core.Services.Base;
using Odin.Core.Services.Certificate;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Dns;
using Odin.Core.Services.Dns.PowerDns;
using Odin.Core.Services.Email;
using Odin.Core.Services.Logging;
using Odin.Core.Services.Peer.SendingHost.Outbox;
using Odin.Core.Services.Registry;
using Odin.Core.Services.Registry.Registration;
using Odin.Hosting._dev;
using Odin.Hosting.Authentication.Owner;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Admin;
using Odin.Hosting.Extensions;
using Odin.Hosting.Middleware;
using Odin.Hosting.Middleware.Logging;
using Odin.Hosting.Multitenant;
using Quartz;

namespace Odin.Hosting
{
    public class Startup
    {
        private IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<KestrelServerOptions>(options => { options.AllowSynchronousIO = true; });

            var config = new OdinConfiguration(Configuration);
            services.AddSingleton(config);

            PrepareEnvironment(config);
            AssertValidRenewalConfiguration(config.CertificateRenewal);

            //
            // IHttpClientFactory rules when creating a HttpClient:
            // - It is not the HttpClient that is managed by IHttpClientFactory, it is the HttpClientHandler
            //   that is explictly or implicitly attached to the HttpClient instance that is managed and shared by
            //   different HttpClients and on different threads.
            // - It is OK to change properties on the HttpClient instance (e.g. AddDefaultHeaders)
            //   as long as you make sure that the instance is short-lived and not mutated on another thread.
            // - It is OK to create a HttpClientHandler, but it *MUST NOT* hold any instance data. This includes
            //   cookies in a CookieContainer. Therefore avoid using Cookies. If you need cookies, set the headers
            //   manually.
            // - Use SetHandlerLifetime to control how long a connections are pooled (this also controls when existing
            //   HttpClientHandlers are called)
            //
            services.AddSingleton<IHttpClientFactory>(new HttpClientFactory());
            services.AddSingleton<ISystemHttpClient, SystemHttpClient>();

            if (config.Quartz.EnableQuartzBackgroundService)
            {
                services.AddQuartz(q =>
                {
                    //lets use use our normal DI setup
                    q.UseMicrosoftDependencyInjectionJobFactory();
                    q.UseDefaultCronSchedule(config);
                    q.UseDefaultCertificateRenewalSchedule(config);
                });

                services.AddQuartzServer(options => { options.WaitForJobsToComplete = true; });
            }

            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    foreach (var c in OdinSystemSerializer.JsonSerializerOptions!.Converters)
                    {
                        options.JsonSerializerOptions.Converters.Add(c);
                    }

                    options.JsonSerializerOptions.IncludeFields =
                        OdinSystemSerializer.JsonSerializerOptions.IncludeFields;
                    options.JsonSerializerOptions.Encoder = OdinSystemSerializer.JsonSerializerOptions.Encoder;
                    options.JsonSerializerOptions.MaxDepth = OdinSystemSerializer.JsonSerializerOptions.MaxDepth;
                    options.JsonSerializerOptions.NumberHandling =
                        OdinSystemSerializer.JsonSerializerOptions.NumberHandling;
                    options.JsonSerializerOptions.ReferenceHandler =
                        OdinSystemSerializer.JsonSerializerOptions.ReferenceHandler;
                    options.JsonSerializerOptions.WriteIndented =
                        OdinSystemSerializer.JsonSerializerOptions.WriteIndented;
                    options.JsonSerializerOptions.AllowTrailingCommas =
                        OdinSystemSerializer.JsonSerializerOptions.AllowTrailingCommas;
                    options.JsonSerializerOptions.DefaultBufferSize =
                        OdinSystemSerializer.JsonSerializerOptions.DefaultBufferSize;
                    options.JsonSerializerOptions.DefaultIgnoreCondition =
                        OdinSystemSerializer.JsonSerializerOptions.DefaultIgnoreCondition;
                    options.JsonSerializerOptions.DictionaryKeyPolicy =
                        OdinSystemSerializer.JsonSerializerOptions.DictionaryKeyPolicy;
                    options.JsonSerializerOptions.PropertyNamingPolicy =
                        OdinSystemSerializer.JsonSerializerOptions.PropertyNamingPolicy;
                    options.JsonSerializerOptions.ReadCommentHandling =
                        OdinSystemSerializer.JsonSerializerOptions.ReadCommentHandling;
                    options.JsonSerializerOptions.UnknownTypeHandling =
                        OdinSystemSerializer.JsonSerializerOptions.UnknownTypeHandling;
                    options.JsonSerializerOptions.IgnoreReadOnlyFields =
                        OdinSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyFields;
                    options.JsonSerializerOptions.IgnoreReadOnlyProperties =
                        OdinSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyProperties;
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive =
                        OdinSystemSerializer.JsonSerializerOptions.PropertyNameCaseInsensitive;
                });

            //services.AddRazorPages(options => { options.RootDirectory = "/Views"; });

            //Note: this product is designed to avoid use of the HttpContextAccessor in the services
            //All params should be passed into to the services using DotYouContext
            services.AddHttpContextAccessor();
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.MimeTypes = new[] { "application/json" };
            });

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.IgnoreObsoleteActions();
                c.IgnoreObsoleteProperties();
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
                c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory,
                    $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
                c.EnableAnnotations();
                c.SwaggerDoc("v1", new()
                {
                    Title = "DotYouCore API",
                    Version = "v1"
                });
            });

            services.AddCorsPolicies();

            services.AddAuthentication(options => { })
                .AddOwnerAuthentication()
                .AddYouAuthAuthentication()
                .AddPeerCertificateAuthentication(PeerAuthConstants.TransitCertificateAuthScheme)
                .AddPeerCertificateAuthentication(PeerAuthConstants.PublicTransitAuthScheme)
                .AddPeerCertificateAuthentication(PeerAuthConstants.FeedAuthScheme)
                .AddSystemAuthentication();

            services.AddAuthorization(policy =>
            {
                OwnerPolicies.AddPolicies(policy);
                SystemPolicies.AddPolicies(policy);
                YouAuthPolicies.AddPolicies(policy);
                PeerPerimeterPolicies.AddPolicies(policy, PeerAuthConstants.TransitCertificateAuthScheme);
                PeerPerimeterPolicies.AddPolicies(policy, PeerAuthConstants.PublicTransitAuthScheme);
            });

            services.AddSingleton<ServerSystemStorage>();
            services.AddSingleton<IPendingTransfersService, PendingTransfersService>();

            // In production, the React files will be served from this directory
            services.AddSpaStaticFiles(configuration => { configuration.RootPath = "client/"; });
            
            services.AddSingleton<IIdentityRegistry>(sp => new FileSystemIdentityRegistry(
                sp.GetRequiredService<ILogger<FileSystemIdentityRegistry>>(),
                sp.GetRequiredService<ICertificateServiceFactory>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ISystemHttpClient>(),
                config));

            services.AddSingleton(new AcmeAccountConfig
            {
                AcmeContactEmail = config.CertificateRenewal.CertificateAuthorityAssociatedEmail,
                AcmeAccountFolder = config.Host.SystemSslRootPath
            });
            services.AddSingleton<IAcmeHttp01TokenCache, AcmeHttp01TokenCache>();
            services.AddSingleton<IIdentityRegistrationService, IdentityRegistrationService>();
            services.AddSingleton<ILookupClient>(new LookupClient());

            services.AddSingleton<IDnsRestClient>(sp => new PowerDnsRestClient(
                sp.GetRequiredService<ILogger<PowerDnsRestClient>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                new Uri($"https://{config.Registry.PowerDnsHostAddress}/api/v1"),
                config.Registry.PowerDnsApiKey));

            services.AddSingleton<ICertesAcme>(sp => new CertesAcme(
                sp.GetRequiredService<ILogger<CertesAcme>>(),
                sp.GetRequiredService<IAcmeHttp01TokenCache>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                config.CertificateRenewal.UseCertificateAuthorityProductionServers));

            services.AddSingleton<ICertificateServiceFactory, CertificateServiceFactory>();

            services.AddSingleton<IEmailSender>(sp => new MailgunSender(
                sp.GetRequiredService<ILogger<MailgunSender>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                config.Mailgun.ApiKey,
                config.Mailgun.EmailDomain,
                config.Mailgun.DefaultFrom));

            services.AddSingleton(new AdminApiRestrictedAttribute(
                config.Admin.ApiEnabled,
                config.Admin.ApiKey,
                config.Admin.ApiKeyHttpHeaderName,
                config.Admin.ApiPort));
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
            //builder.RegisterType<Controllers.Test.TenantDependencyTest2>().As<Controllers.Test.ITenantDependencyTest2>().SingleInstance();
            builder.RegisterModule(new LoggingAutofacModule());
            builder.RegisterModule(new MultiTenantAutofacModule());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger, IHostApplicationLifetime lifetime)
        {
            var config = app.ApplicationServices.GetRequiredService<OdinConfiguration>();
            var registry = app.ApplicationServices.GetRequiredService<IIdentityRegistry>();

            app.UseLoggingMiddleware();
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseMiddleware<CertesAcmeMiddleware>();

            bool IsProvisioningSite(HttpContext context)
            {
                var domain = context.RequestServices.GetService<OdinConfiguration>()?.Registry.ProvisioningDomain;
                return context.Request.Host.Equals(new HostString(domain ?? ""));
            }

            app.MapWhen(IsProvisioningSite, app => Provisioning.Map(app, env, logger));

            app.UseMultiTenancy();

            app.UseDefaultFiles();
            app.UseCertificateForwarding();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseMiddleware<OdinContextMiddleware>();
            app.UseResponseCompression();
            app.UseCors();
            app.UseApiCors();
            app.UseMiddleware<SharedSecretEncryptionMiddleware>();
            app.UseMiddleware<StaticFileCachingMiddleware>();
            app.UseHttpsRedirection();

            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            };

            // webSocketOptions.AllowedOrigins.Add("https://...");
            app.UseWebSockets(webSocketOptions); //Note: see NotificationSocketController

            app.UseEndpoints(endpoints =>
            {
                // endpoints.MapGet("/", async context =>
                // {
                //     context.Response.Redirect("/home");
                //     await Task.CompletedTask;
                // });
                endpoints.MapControllers();
            });

            if (env.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OdinCore v1"));

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner"),
                    homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3001/owner/"); }); });
                
                // No idea why this should be true instead of `ctx.Request.Path.StartsWithSegments("/")`
                app.MapWhen(ctx => true,
                    homeApp =>
                    {
                        homeApp.UseSpa(
                            spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3000/"); });
                    });
            }
            else
            {
                logger.LogInformation("Mapping SPA paths on local disk");
                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner"),
                    ownerApp =>
                    {
                        var ownerPath = Path.Combine(env.ContentRootPath, "client", "owner-app");
                        ownerApp.UseStaticFiles(new StaticFileOptions()
                        {
                            FileProvider = new PhysicalFileProvider(ownerPath),
                            RequestPath = "/owner"
                        });

                        ownerApp.Run(async context =>
                        {
                            context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                            await context.Response.SendFileAsync(Path.Combine(ownerPath, "index.html"));
                            return;
                        });
                    });

                // app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/"),
                app.MapWhen(ctx => true,
                    homeApp =>
                    {
                        var publicPath = Path.Combine(env.ContentRootPath, "client", "public-app");

                        homeApp.UseStaticFiles(new StaticFileOptions()
                        {
                            FileProvider = new PhysicalFileProvider(publicPath),
                            // RequestPath = "/"
                        });

                        homeApp.Run(async context =>
                        {
                            context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                            await context.Response.SendFileAsync(Path.Combine(publicPath, "index.html"));
                            return;
                        });
                    });
            }

            lifetime.ApplicationStarted.Register(() => { DevEnvironmentSetup.ConfigureIfPresent(config, registry); });
        }

        private void PrepareEnvironment(OdinConfiguration cfg)
        {
            Directory.CreateDirectory(cfg.Host.TenantDataRootPath);
            Directory.CreateDirectory(cfg.Host.SystemDataRootPath);
            Directory.CreateDirectory(cfg.Host.SystemSslRootPath);
        }

        private void AssertValidRenewalConfiguration(OdinConfiguration.CertificateRenewalSection section)
        {
            Guard.Argument(section, nameof(section)).NotNull();
            Guard.Argument(section.CertificateAuthorityAssociatedEmail,
                nameof(section.CertificateAuthorityAssociatedEmail)).NotNull().NotEmpty();
        }
    }
}