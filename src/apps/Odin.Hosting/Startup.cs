using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
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
using Odin.Core.Dns;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Tasks;
using Odin.Services.Admin.Tenants;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Dns;
using Odin.Services.Dns.PowerDns;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Email;
using Odin.Services.Logging;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;
using Odin.Services.Tenant.Container;
using Odin.Core.Util;
using Odin.Hosting._dev;
using Odin.Hosting.Authentication.Owner;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Admin;
using Odin.Hosting.Controllers.Registration;
using Odin.Hosting.Extensions;
using Odin.Hosting.Middleware;
using Odin.Hosting.Middleware.Logging;
using Odin.Hosting.Multitenant;
using Odin.Services.Background;
using Odin.Services.JobManagement;
using Odin.Services.Util;

namespace Odin.Hosting
{
    public class Startup(IConfiguration configuration, IEnumerable<string> args)
    {
        private readonly IEnumerable<string> _args = args;
        private readonly OdinConfiguration _config = new(configuration);

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(_config);

            services.Configure<KestrelServerOptions>(options => { options.AllowSynchronousIO = true; });
            services.Configure<HostOptions>(options =>
            {
                options.ShutdownTimeout = TimeSpan.FromSeconds(_config.Host.ShutdownTimeoutSeconds);
            });

            PrepareEnvironment(_config);
            AssertValidRenewalConfiguration(_config.CertificateRenewal);

            //
            // We are using HttpClientFactoryLite because we have to be able to create HttpClientHandlers on the fly.
            //   (e.g.: FileSystemIdentityRegistry.RegisterDotYouHttpClient())
            // This is not possible with the baked in HttpClientFactory.
            //
            // IHttpClientFactory rules when creating a HttpClient:
            // - It is HttpClientHandler instance that is managed by HttpClientFactory, not the HttpClient instance.
            // - The HttpClientHandler instance, which is explictly or implicitly attached to a HttpClient instance,
            //   is shared by different HttpClient instances across all threads.
            // - It is OK to change properties on the HttpClient instance (e.g. AddDefaultHeaders)
            //   as long as you make sure that the instance is short-lived and not mutated on another thread.
            // - It is OK to create a HttpClientHandler, but it *MUST NOT* hold any instance data. This includes
            //   cookies in a CookieContainer. Therefore avoid using Cookies. If you need cookies, set the headers
            //   manually.
            // - Use SetHandlerLifetime to control how long connections are pooled (this also controls when existing
            //   HttpClientHandlers are called)
            //
            var httpClientFactory = new HttpClientFactory();
            services.AddSingleton<IHttpClientFactory>(httpClientFactory); // this is HttpClientFactoryLite
            services.AddSingleton<ISystemHttpClient, SystemHttpClient>();
            services.AddSingleton<ConcurrentFileManager>();
            services.AddSingleton<DriveFileReaderWriter>();

            //
            // Background and job stuff
            //
            services.AddJobManagerServices();

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

            //Note: this product is designed to avoid use of the HttpContextAccessor in the services
            //All params should be passed into to the services using DotYouContext
            services.AddHttpContextAccessor();
            services.AddResponseCompression(options => { options.EnableForHttps = true; });

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
                    Title = "Odin API",
                    Version = "v1"
                });
            });

            services.AddCorsPolicies();

            services.AddAuthentication(options => { })
                .AddOwnerAuthentication()
                .AddYouAuthAuthentication()
                .AddAppNotificationSubscriberAuthentication()
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

            // In production, the React files will be served from this directory
            services.AddSpaStaticFiles(configuration => { configuration.RootPath = "client/"; });

            services.AddSingleton<IIdentityRegistry>(sp => new FileSystemIdentityRegistry(
                sp.GetRequiredService<ILogger<FileSystemIdentityRegistry>>(),
                sp.GetRequiredService<ICertificateServiceFactory>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ISystemHttpClient>(),
                sp.GetRequiredService<IMultiTenantContainerAccessor>(),
                TenantServices.ConfigureTenantServices,
                _config));

            services.AddSingleton(new AcmeAccountConfig
            {
                AcmeContactEmail = _config.CertificateRenewal.CertificateAuthorityAssociatedEmail,
                AcmeAccountFolder = _config.Host.SystemSslRootPath
            });
            services.AddSingleton<ILookupClient>(new LookupClient());
            services.AddSingleton<IAcmeHttp01TokenCache, AcmeHttp01TokenCache>();

            services.AddIdentityRegistrationServices(httpClientFactory, _config);

            services.AddSingleton<IAuthoritativeDnsLookup, AuthoritativeDnsLookup>();
            services.AddSingleton<IDnsLookupService, DnsLookupService>();

            services.AddSingleton<IDnsRestClient>(sp => new PowerDnsRestClient(
                sp.GetRequiredService<ILogger<PowerDnsRestClient>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                new Uri($"https://{_config.Registry.PowerDnsHostAddress}/api/v1"),
                _config.Registry.PowerDnsApiKey));

            services.AddSingleton<ICertesAcme>(sp => new CertesAcme(
                sp.GetRequiredService<ILogger<CertesAcme>>(),
                sp.GetRequiredService<IAcmeHttp01TokenCache>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                _config.CertificateRenewal.UseCertificateAuthorityProductionServers));

            services.AddSingleton<ICertificateCache, CertificateCache>();
            services.AddSingleton<ICertificateServiceFactory, CertificateServiceFactory>();

            services.AddSingleton<IEmailSender>(sp => new MailgunSender(
                sp.GetRequiredService<ILogger<MailgunSender>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                _config.Mailgun.ApiKey,
                _config.Mailgun.EmailDomain,
                _config.Mailgun.DefaultFrom));

            services.AddSingleton(sp => new AdminApiRestrictedAttribute(
                sp.GetRequiredService<ILogger<AdminApiRestrictedAttribute>>(),
                _config.Admin.ApiEnabled,
                _config.Admin.ApiKey,
                _config.Admin.ApiKeyHttpHeaderName,
                _config.Admin.ApiPort,
                _config.Admin.Domain));

            services.AddSingleton(new RegistrationRestrictedAttribute(_config.Registry.ProvisioningEnabled));

            services.AddTransient<ITenantAdmin, TenantAdmin>();

            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

            services.AddIpRateLimiter(_config.Host.IpRateLimitRequestsPerSecond);
        }

        // ConfigureContainer is where you can register things directly
        // with Autofac. This runs after ConfigureServices so the things
        // here will override registrations made in ConfigureServices.
        // This will all go in the ROOT CONTAINER and is NOT TENANT SPECIFIC.
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.RegisterModule(new LoggingAutofacModule());
            builder.RegisterModule(new MultiTenantAutofacModule());
           
            builder.AddSystemBackgroundServices();

            // System database services
            builder.AddDatabaseCacheServices();
            switch (_config.Database.Type)
            {
                case DatabaseType.Sqlite:
                    builder.AddSqliteSystemDatabaseServices(Path.Combine(_config.Host.SystemDataRootPath, "sys.db"));
                    break;
                case DatabaseType.Postgres:
                    builder.AddPgsqlSystemDatabaseServices(_config.Database.ConnectionString);
                    break;
                default:
                    throw new OdinSystemException("Unsupported database type");
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger, IHostApplicationLifetime lifetime)
        {
            logger.LogInformation("Environment: {Environment}", env.EnvironmentName);

            var config = app.ApplicationServices.GetRequiredService<OdinConfiguration>();

            // Note 1: see NotificationSocketController
            // Note 2: UseWebSockets must be before UseLoggingMiddleware
            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromMinutes(2)
            });

            app.UseLoggingMiddleware();

            if (env.IsProduction())
            {
                app.UseRateLimiter();
            }

            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseMiddleware<RedirectIfNotApexMiddleware>();
            app.UseMiddleware<CertesAcmeMiddleware>();

            app.UseHttpsPortRedirection(config.Host.DefaultHttpsPort);
            app.UseResponseCompression();
            app.UseHsts();

            // Provisioning mapping
            if (config.Registry.ProvisioningEnabled)
            {
                app.MapWhen(
                    context => context.Request.Host.Host == config.Registry.ProvisioningDomain,
                    a => Provisioning.Map(a, env, logger));
            }

            // Admin mapping
            if (config.Admin.ApiEnabled)
            {
                app.MapWhen(
                    context => context.Request.Host.Host == config.Admin.Domain,
                    a => Admin.Map(a, env, logger));
            }

            app.UseMultiTenancy();

            app.UseDefaultFiles();
            app.UseCertificateForwarding();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseIdentityReadyState();
            app.UseVersionUpgrade();

            app.UseMiddleware<OdinContextMiddleware>();
            app.UseCors();
            app.UseApiCors();
            app.UseMiddleware<SharedSecretEncryptionMiddleware>();
            app.UseMiddleware<StaticFileCachingMiddleware>();

            app.UseEndpoints(endpoints =>
            {
                if (env.IsDevelopment())
                {
                    endpoints.MapGet("/test-shutdown", async context =>
                    {
                        var now = DateTime.UtcNow;
                        while (DateTime.UtcNow < now.AddSeconds(10))
                        {
                            logger.LogInformation("Waiting for shutdown");
                            await Task.Delay(1000);
                        }

                        await context.Response.WriteAsync("Done waiting for shutdown");
                    });
                }

                endpoints.MapControllers();
            });

            if (env.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OdinCore v1"));

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/owner"),
                    homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3001/"); }); });

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/feed"),
                    homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3002/"); }); });

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/chat"),
                    homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3003/"); }); });

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/mail"),
                    homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3004/"); }); });

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/community"),
                    homeApp => { homeApp.UseSpa(spa => { spa.UseProxyToSpaDevelopmentServer($"https://dev.dotyou.cloud:3006/"); }); });

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

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/feed"),
                    feedApp =>
                    {
                        var feedPath = Path.Combine(env.ContentRootPath, "client", "apps", "feed");
                        feedApp.UseStaticFiles(new StaticFileOptions()
                        {
                            FileProvider = new PhysicalFileProvider(feedPath),
                            RequestPath = "/apps/feed"
                        });
                        feedApp.Run(async context =>
                        {
                            context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                            await context.Response.SendFileAsync(Path.Combine(feedPath, "index.html"));
                            return;
                        });
                    });

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/chat"),
                    chatApp =>
                    {
                        var chatPath = Path.Combine(env.ContentRootPath, "client", "apps", "chat");
                        chatApp.UseStaticFiles(new StaticFileOptions()
                        {
                            FileProvider = new PhysicalFileProvider(chatPath),
                            RequestPath = "/apps/chat"
                        });

                        chatApp.Run(async context =>
                        {
                            context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                            await context.Response.SendFileAsync(Path.Combine(chatPath, "index.html"));
                            return;
                        });
                    });

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/mail"),
                    mailApp =>
                    {
                        var mailPath = Path.Combine(env.ContentRootPath, "client", "apps", "mail");
                        mailApp.UseStaticFiles(new StaticFileOptions()
                        {
                            FileProvider = new PhysicalFileProvider(mailPath),
                            RequestPath = "/apps/mail"
                        });

                        mailApp.Run(async context =>
                        {
                            context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                            await context.Response.SendFileAsync(Path.Combine(mailPath, "index.html"));
                            return;
                        });
                    });

                app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/apps/community"),
                    communityApp =>
                    {
                        var communityPath = Path.Combine(env.ContentRootPath, "client", "apps", "community");
                        communityApp.UseStaticFiles(new StaticFileOptions()
                        {
                            FileProvider = new PhysicalFileProvider(communityPath),
                            RequestPath = "/apps/community"
                        });

                        communityApp.Run(async context =>
                        {
                            context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
                            await context.Response.SendFileAsync(Path.Combine(communityPath, "index.html"));
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
                        });
                    });
            }

            lifetime.ApplicationStarted.Register(() =>
            {
                var services = app.ApplicationServices;

                // Create system database
                var systemDatabase = services.GetRequiredService<SystemDatabase>();
                systemDatabase.CreateDatabaseAsync().BlockingWait();

                // Load identity registry
                var registry = services.GetRequiredService<IIdentityRegistry>();
                registry.LoadRegistrations().BlockingWait();
                DevEnvironmentSetup.ConfigureIfPresent(logger, config, registry);

                // Check for singleton dependencies
                if (env.IsDevelopment())
                {
                    var root = services.GetRequiredService<IMultiTenantContainerAccessor>().Container();
                    new AutofacDiagnostics(root, logger).AssertSingletonDependencies();
                }

                // Start system background services
                if (config.Job.SystemJobsEnabled)
                {
                    services.StartSystemBackgroundServices().BlockingWait();
                }
            });

            lifetime.ApplicationStopping.Register(() =>
            {
                logger.LogDebug("Waiting max {ShutdownTimeoutSeconds}s for requests and jobs to complete",
                    config.Host.ShutdownTimeoutSeconds);

                var services = app.ApplicationServices;

                //
                // Shutdown all tenant background services
                //
                services.ShutdownTenantBackgroundServices().BlockingWait();

                //
                // Shutdown system background services
                //
                services.ShutdownSystemBackgroundServices().BlockingWait();
            });
        }

        private void PrepareEnvironment(OdinConfiguration cfg)
        {
            Directory.CreateDirectory(cfg.Host.TenantDataRootPath);
            Directory.CreateDirectory(cfg.Host.SystemDataRootPath);
            Directory.CreateDirectory(cfg.Host.SystemSslRootPath);
        }

        private void AssertValidRenewalConfiguration(OdinConfiguration.CertificateRenewalSection section)
        {
            var email = section?.CertificateAuthorityAssociatedEmail;
            if (string.IsNullOrEmpty(email) || string.IsNullOrWhiteSpace(email))
            {
                throw new OdinSystemException($"{nameof(section.CertificateAuthorityAssociatedEmail)} is not configured");
            }
        }
    }
}