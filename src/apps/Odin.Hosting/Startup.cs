using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using DnsClient;
using HttpClientFactoryLite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
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
using Odin.Core.Logging;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Tasks;
using Odin.Services.Admin.Tenants;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Dns;
using Odin.Services.Dns.PowerDns;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Email;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;
using Odin.Services.Tenant.Container;
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
using Odin.Hosting.Migration.DriveAliasPhase1;
using Odin.Hosting.Multitenant;
using Odin.Services.Background;
using Odin.Services.Concurrency;
using Odin.Services.JobManagement;
using Odin.Services.LinkPreview;
using StackExchange.Redis;
using Odin.Services.Drives.FileSystem.Base;

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
            services.AddSingleton<DriveFileReaderWriter>();
            services.AddSingleton<IForgottenTasks, ForgottenTasks>();

            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    foreach (var c in OdinSystemSerializer.JsonSerializerOptions!.Converters)
                    {
                        options.JsonSerializerOptions.Converters.Add(c);
                    }

                    options.JsonSerializerOptions.IncludeFields = OdinSystemSerializer.JsonSerializerOptions.IncludeFields;
                    options.JsonSerializerOptions.Encoder = OdinSystemSerializer.JsonSerializerOptions.Encoder;
                    options.JsonSerializerOptions.MaxDepth = OdinSystemSerializer.JsonSerializerOptions.MaxDepth;
                    options.JsonSerializerOptions.NumberHandling = OdinSystemSerializer.JsonSerializerOptions.NumberHandling;
                    options.JsonSerializerOptions.ReferenceHandler = OdinSystemSerializer.JsonSerializerOptions.ReferenceHandler;
                    options.JsonSerializerOptions.WriteIndented = OdinSystemSerializer.JsonSerializerOptions.WriteIndented;
                    options.JsonSerializerOptions.AllowTrailingCommas = OdinSystemSerializer.JsonSerializerOptions.AllowTrailingCommas;
                    options.JsonSerializerOptions.DefaultBufferSize = OdinSystemSerializer.JsonSerializerOptions.DefaultBufferSize;
                    options.JsonSerializerOptions.DefaultIgnoreCondition =
                        OdinSystemSerializer.JsonSerializerOptions.DefaultIgnoreCondition;
                    options.JsonSerializerOptions.DictionaryKeyPolicy = OdinSystemSerializer.JsonSerializerOptions.DictionaryKeyPolicy;
                    options.JsonSerializerOptions.PropertyNamingPolicy = OdinSystemSerializer.JsonSerializerOptions.PropertyNamingPolicy;
                    options.JsonSerializerOptions.ReadCommentHandling = OdinSystemSerializer.JsonSerializerOptions.ReadCommentHandling;
                    options.JsonSerializerOptions.UnknownTypeHandling = OdinSystemSerializer.JsonSerializerOptions.UnknownTypeHandling;
                    options.JsonSerializerOptions.IgnoreReadOnlyFields = OdinSystemSerializer.JsonSerializerOptions.IgnoreReadOnlyFields;
                    options.JsonSerializerOptions.IgnoreReadOnlyProperties = OdinSystemSerializer.JsonSerializerOptions
                        .IgnoreReadOnlyProperties;
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = OdinSystemSerializer.JsonSerializerOptions
                        .PropertyNameCaseInsensitive;
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

            if (_config.Redis.Enabled)
            {
                services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(_config.Redis.Configuration));
            }

            if (_config.Redis.Enabled)
            {
                // Distributed lock:
                services.AddSingleton<INodeLock, RedisLock>();
            }
            else
            {
                // Node-wide lock:
                services.AddSingleton<INodeLock, NodeLock>();
            }

            services.AddCoreCacheServices(new CacheConfiguration
            {
                Level2CacheType = _config.Cache.Level2CacheType,
            });

            // We currently don't use asp.net data protection, but we need to configure it to avoid warnings
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_config.Host.DataProtectionKeyPath));

            if (_config.S3PayloadStorage.Enabled)
            {
                services.AddMinioClient(
                    _config.S3PayloadStorage.Endpoint,
                    _config.S3PayloadStorage.AccessKey,
                    _config.S3PayloadStorage.SecretAccessKey,
                    _config.S3PayloadStorage.Region);

                services.AddS3PayloadStorage(_config.S3PayloadStorage.BucketName);
            }
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
            builder.AddJobManagerServices();

            // Global database services
            builder.AddDatabaseCacheServices();
            builder.AddDatabaseCounterServices();

            // System database services
            switch (_config.Database.Type)
            {
                case DatabaseType.Sqlite:
                    // TenantPathManager.AssertEqualPaths(TenantPathManager.GetSysDatabasePath(), Path.Combine(_config.Host.SystemDataRootPath, "sys.db"));
                    builder.AddSqliteSystemDatabaseServices(Path.Combine(_config.Host.SystemDataRootPath, "sys.db"));
                    break;
                case DatabaseType.Postgres:
                    builder.AddPgsqlSystemDatabaseServices(_config.Database.ConnectionString);
                    break;
                default:
                    throw new OdinSystemException("Unsupported database type");
            }

            // Global cache services
            builder.AddGlobalCaches();
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
            app.UseMiddleware<OdinVersionNumberMiddleware>();

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

                            var svc = context.RequestServices.GetRequiredService<LinkPreviewService>();
                            var odinContext = context.RequestServices.GetRequiredService<IOdinContext>();

                            var indexFile = Path.Combine(publicPath, "index.html");

                            try
                            {
                                await svc.WriteIndexFileAsync(indexFile, odinContext);
                            }
                            catch (Exception)
                            {
                                // #super parnoid
                                await context.Response.SendFileAsync(indexFile);
                            }
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

                // Sanity ping cache
                logger.LogInformation("Level2CacheType: {Level2CacheType}", _config.Cache.Level2CacheType);
                var cache = services.GetRequiredService<IGlobalLevel2Cache>();
                cache.Set("ping", "pong", TimeSpan.FromSeconds(1));
                var pong = cache.TryGet<string>("ping");
                if (pong != "pong")
                {
                    throw new OdinSystemException("Cache sanity check failed");
                }

                // Sanity ping S3 bucket
                logger.LogInformation("S3PayloadStorage enabled: {enabled}", _config.S3PayloadStorage.Enabled);
                if (_config.S3PayloadStorage.Enabled)
                {
                    var payloadBucket = services.GetRequiredService<S3PayloadStorage>();
                    var bucketExists = payloadBucket.BucketExistsAsync().GetAwaiter().GetResult();
                    if (!bucketExists)
                    {
                        throw new OdinSystemException("S3 payload bucket sanity check failed");
                    }
                }

                // Start system background services
                if (config.Job.SystemJobsEnabled)
                {
                    services.StartSystemBackgroundServices().BlockingWait();
                }
             
                if (Environment.GetCommandLineArgs().Contains("--migration-drive-alias-export-map", StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Migrating drive alias phase deuce");
                    var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                    var migrationLogger = loggerFactory.CreateLogger("Migration");
                    var tenantContainer = services.GetRequiredService<IMultiTenantContainerAccessor>().Container();
                    
                    DriveAliasMigrationPhase2.ExportMap(registry, tenantContainer, migrationLogger, config.Host.SystemDataRootPath).BlockingWait();
                    logger.LogInformation($"Map export to complete (one per tenant)");
                    
                    lifetime.StopApplication();
                }
                
                if (Environment.GetCommandLineArgs().Contains("--migration-drive-alias-phase-one", StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Migrating drive alias phase deuce");
                    var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                    var migrationLogger = loggerFactory.CreateLogger("Migration");
                    var tenantContainer = services.GetRequiredService<IMultiTenantContainerAccessor>().Container();

                    DriveAliasPhase1Migrator.MigrateDrives(registry, tenantContainer, migrationLogger).BlockingWait();
                    
                    DriveAliasMigrationPhase2.MigrateData(registry, tenantContainer, migrationLogger).BlockingWait();

                    logger.LogInformation("Completed migrating drive alias phase one.  You should now remove " +
                                          "flag --migration-drive-alias-phase-one from docker-compose.yml " +
                                          "and restart.  Remember to update tenant services with right drive manager");
                    
                    lifetime.StopApplication();
                }
                
                if (Environment.GetCommandLineArgs().Contains("--migration-drive-alias-phase-two", StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Migrating drive alias phase deuce");
                    var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                    var migrationLogger = loggerFactory.CreateLogger("Migration");
                    var tenantContainer = services.GetRequiredService<IMultiTenantContainerAccessor>().Container();
                    
                    DriveAliasMigrationPhaseThree.UpdateFileSystem(registry, tenantContainer, migrationLogger).BlockingWait();

                    var msg = "Completed migrating drive alias phase two.  You should now remove " +
                              "flag --migration-drive-alias-phase-two from docker-compose.yml " +
                              "and restart.  Remember to update tenant services with right drive manager";
                    
                    logger.LogInformation(msg);
                    Console.WriteLine(msg);
                    
                    lifetime.StopApplication();
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

                //
                // Wait for any registered fire-and-forget tasks to complete
                //
                services.GetRequiredService<IForgottenTasks>().WhenAll().BlockingWait();

                // DON'T PUT ANYTHING BELOW THIS LINE
                logger.LogInformation("Background services stopped");
            });

            lifetime.ApplicationStopped.Register(() =>
            {
                // DON'T PUT ANYTHING BELOW THIS LINE
                logger.LogInformation("Application stopped");
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