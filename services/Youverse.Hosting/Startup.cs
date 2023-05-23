using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Dawn;
using DnsClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Quartz;
using Microsoft.Extensions.Logging;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Dns;
using Youverse.Core.Services.Dns.PowerDns;
using Youverse.Core.Services.Logging;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Registry.Registration;
using Youverse.Core.Services.Transit.SendingHost.Outbox;
using Youverse.Core.Services.Workers.Certificate;
using Youverse.Core.Services.Workers.DefaultCron;
using Youverse.Core.Trie;
using Youverse.Hosting._dev;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.Perimeter;
using Youverse.Hosting.Authentication.System;
using Youverse.Hosting.Middleware;
using Youverse.Hosting.Middleware.Logging;
using Youverse.Hosting.Multitenant;

namespace Youverse.Hosting
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

            var config = new YouverseConfiguration(Configuration);
            services.AddSingleton(config);

            PrepareEnvironment(config);
            AssertValidRenewalConfiguration(config.CertificateRenewal);

            services.AddHttpClient();

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
                    foreach (var c in DotYouSystemSerializer.JsonSerializerOptions!.Converters)
                    {
                        options.JsonSerializerOptions.Converters.Add(c);
                    }

                    options.JsonSerializerOptions.IncludeFields =
                        DotYouSystemSerializer.JsonSerializerOptions.IncludeFields;
                    options.JsonSerializerOptions.Encoder = DotYouSystemSerializer.JsonSerializerOptions.Encoder;
                    options.JsonSerializerOptions.MaxDepth = DotYouSystemSerializer.JsonSerializerOptions.MaxDepth;
                    options.JsonSerializerOptions.NumberHandling =
                        DotYouSystemSerializer.JsonSerializerOptions.NumberHandling;
                    options.JsonSerializerOptions.ReferenceHandler =
                        DotYouSystemSerializer.JsonSerializerOptions.ReferenceHandler;
                    options.JsonSerializerOptions.WriteIndented =
                        DotYouSystemSerializer.JsonSerializerOptions.WriteIndented;
                    options.JsonSerializerOptions.AllowTrailingCommas =
                        DotYouSystemSerializer.JsonSerializerOptions.AllowTrailingCommas;
                    options.JsonSerializerOptions.DefaultBufferSize =
                        DotYouSystemSerializer.JsonSerializerOptions.DefaultBufferSize;
                    options.JsonSerializerOptions.DefaultIgnoreCondition =
                        DotYouSystemSerializer.JsonSerializerOptions.DefaultIgnoreCondition;
                    options.JsonSerializerOptions.DictionaryKeyPolicy =
                        DotYouSystemSerializer.JsonSerializerOptions.DictionaryKeyPolicy;
                    options.JsonSerializerOptions.PropertyNamingPolicy =
                        DotYouSystemSerializer.JsonSerializerOptions.PropertyNamingPolicy;
                    options.JsonSerializerOptions.ReadCommentHandling =
                        DotYouSystemSerializer.JsonSerializerOptions.ReadCommentHandling;
                    options.JsonSerializerOptions.UnknownTypeHandling =
                        DotYouSystemSerializer.JsonSerializerOptions.UnknownTypeHandling;
                    options.JsonSerializerOptions.IgnoreReadOnlyFields =
                        DotYouSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyFields;
                    options.JsonSerializerOptions.IgnoreReadOnlyProperties =
                        DotYouSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyProperties;
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive =
                        DotYouSystemSerializer.JsonSerializerOptions.PropertyNameCaseInsensitive;
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

            services.AddAuthentication(options => { })
                .AddOwnerAuthentication()
                .AddClientTokenAuthentication()
                .AddDiCertificateAuthentication(PerimeterAuthConstants.TransitCertificateAuthScheme)
                .AddDiCertificateAuthentication(PerimeterAuthConstants.PublicTransitAuthScheme)
                .AddDiCertificateAuthentication(PerimeterAuthConstants.FeedAuthScheme)
                .AddSystemAuthentication();

            services.AddAuthorization(policy =>
            {
                OwnerPolicies.AddPolicies(policy);
                SystemPolicies.AddPolicies(policy);
                ClientTokenPolicies.AddPolicies(policy);
                CertificatePerimeterPolicies.AddPolicies(policy, PerimeterAuthConstants.TransitCertificateAuthScheme);
                CertificatePerimeterPolicies.AddPolicies(policy, PerimeterAuthConstants.PublicTransitAuthScheme);
            });

            services.AddSingleton<YouverseConfiguration>(config);
            services.AddSingleton<ServerSystemStorage>();
            services.AddSingleton<IPendingTransfersService, PendingTransfersService>();

            // In production, the React files will be served from this directory
            services.AddSpaStaticFiles(configuration => { configuration.RootPath = "client/"; });

            services.AddSingleton<IIdentityRegistry>(sp => new FileSystemIdentityRegistry(
                sp.GetRequiredService<ICertificateServiceFactory>(),
                config.Host.TenantDataRootPath,
                config.CertificateRenewal.ToCertificateRenewalConfig(),
                config.Host.TenantPayloadRootPath));

            services.AddSingleton(new AcmeAccountConfig
            {
                AcmeContactEmail = config.CertificateRenewal.CertificateAuthorityAssociatedEmail,
                AcmeAccountFolder = config.Host.SystemSslRootPath
            });
            services.AddSingleton<IAcmeHttp01TokenCache, AcmeHttp01TokenCache>();
            services.AddSingleton<IIdentityRegistrationService, IdentityRegistrationService>();
            services.AddSingleton<ILookupClient>(new LookupClient());
            services.AddSingleton<IDnsRestClient, PowerDnsRestClient>();
            services.AddSingleton<ICertesAcme>(sp => new CertesAcme(
                sp.GetRequiredService<ILogger<CertesAcme>>(),
                sp.GetRequiredService<IAcmeHttp01TokenCache>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                config.CertificateRenewal.UseCertificateAuthorityProductionServers));
            services.AddSingleton<ICertificateServiceFactory, CertificateServiceFactory>();
            services.AddHttpClient<IDnsRestClient, PowerDnsRestClient>(client =>
            {
                client.BaseAddress = new Uri($"https://{config.Registry.PowerDnsHostAddress}/api/v1");
                client.DefaultRequestHeaders.Add("X-API-Key", config.Registry.PowerDnsApiKey);
            });
            services.AddHttpClient<IIdentityRegistrationService, IdentityRegistrationService>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(3);
                }).ConfigurePrimaryHttpMessageHandler(() =>
                {
                    var handler = new HttpClientHandler
                    {
                        AllowAutoRedirect = false
                    };
                    return handler;
                })
                // Shortlived to deal with DNS changes
                .SetHandlerLifetime(TimeSpan.FromSeconds(10));

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
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            var config = app.ApplicationServices.GetRequiredService<YouverseConfiguration>();
            var registry = app.ApplicationServices.GetRequiredService<IIdentityRegistry>();

            DevEnvironmentSetup.ConfigureIfPresent(config, registry);

            app.UseLoggingMiddleware();
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseMiddleware<CertesAcmeMiddleware>();

            bool IsProvisioningSite(HttpContext context)
            {
                var domain = context.RequestServices.GetService<YouverseConfiguration>()?.Registry.ProvisioningDomain;
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

            app.UseMiddleware<DotYouContextMiddleware>();
            app.UseResponseCompression();
            app.UseApiCors();
            app.UseAppCors();
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
                endpoints.MapGet("/", async context =>
                {
                    context.Response.Redirect("/home");
                    await Task.CompletedTask;
                });
                endpoints.MapControllers();
            });

            if (env.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DotYouCore v1"));

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/home"),
                    homeApp =>
                    {
                        homeApp.UseSpa(
                            spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3000/home/"); });
                    });

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner"),
                    homeApp =>
                    {
                        homeApp.UseSpa(spa =>
                        {
                            spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3001/owner/");
                        });
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

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/home"),
                    homeApp =>
                    {
                        var publicPath = Path.Combine(env.ContentRootPath, "client", "public-app");

                        homeApp.UseStaticFiles(new StaticFileOptions()
                        {
                            FileProvider = new PhysicalFileProvider(publicPath),
                            RequestPath = "/home"
                        });

                        homeApp.Run(async context =>
                        {
                            context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                            await context.Response.SendFileAsync(Path.Combine(publicPath, "index.html"));
                            return;
                        });
                    });
            }
        }

        private void PrepareEnvironment(YouverseConfiguration cfg)
        {
            Directory.CreateDirectory(cfg.Host.TenantDataRootPath);
            Directory.CreateDirectory(cfg.Host.SystemDataRootPath);
            Directory.CreateDirectory(cfg.Host.SystemSslRootPath);
        }

        private void AssertValidRenewalConfiguration(YouverseConfiguration.CertificateRenewalSection section)
        {
            Guard.Argument(section, nameof(section)).NotNull();
            Guard.Argument(section.CertificateAuthorityAssociatedEmail,
                nameof(section.CertificateAuthorityAssociatedEmail)).NotNull().NotEmpty();
        }
    }
}
