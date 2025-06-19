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
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Tasks;
using Odin.Core.Util;
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
using Odin.Hosting.Multitenant;
using Odin.Services.Background;
using Odin.Services.Concurrency;
using Odin.Services.Drives;
using Odin.Services.JobManagement;
using Odin.Services.LinkPreview;
using Odin.Services.Membership.CircleMembership;
using StackExchange.Redis;

namespace Odin.Hosting;

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
        services.AddSingleton<FileReaderWriter>();
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
                options.JsonSerializerOptions.DefaultIgnoreCondition = OdinSystemSerializer.JsonSerializerOptions.DefaultIgnoreCondition;
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

        services.AddSingleton<ICertificateStore, CertificateStore>();
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

        // Payload storage
        if (_config.S3PayloadStorage.Enabled)
        {
            services.AddS3AwsPayloadStorage(
                _config.S3PayloadStorage.AccessKey,
                _config.S3PayloadStorage.SecretAccessKey,
                _config.S3PayloadStorage.ServiceUrl,
                _config.S3PayloadStorage.Region,
                _config.S3PayloadStorage.ForcePathStyle,
                _config.S3PayloadStorage.BucketName);
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
            // NOTE:
            // This is called AFTER the app has started and is accepting requests.
            // If you want stuff done BEFORE the app starts accepting requests,
            // put it in HostExtensions.BeforeApplicationStarting (below).
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            // NOTE:
            // This is called BEFORE the app has stopped accepting requests.
            logger.LogDebug("Waiting max {ShutdownTimeoutSeconds}s for requests and jobs to complete",
                config.Host.ShutdownTimeoutSeconds);

            var host = app.ApplicationServices.GetRequiredService<IHost>();
            host.OnApplicationStopping();
        });

        lifetime.ApplicationStopped.Register(() =>
        {
            // NOTE:
            // This is called AFTER the app has stopped accepting requests.
            // But it's not always being called. Or so it seems.
        });
    }

    //

    private void PrepareEnvironment(OdinConfiguration cfg)
    {
        Directory.CreateDirectory(cfg.Host.TenantDataRootPath);
        Directory.CreateDirectory(cfg.Host.SystemDataRootPath);
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

//

public static class HostExtensions
{
    private static bool _didCleanUp;

    public static IHost BeforeApplicationStarting(this IHost host, string[] args)
    {
        _didCleanUp = false;

        var services = host.Services;
        var logger = services.GetRequiredService<ILogger<Startup>>();
        var config = services.GetRequiredService<OdinConfiguration>();

        logger.LogDebug("Starting initialization in {method}", nameof(BeforeApplicationStarting));

        // Create system database
        var systemDatabase = services.GetRequiredService<SystemDatabase>();
        systemDatabase.CreateDatabaseAsync().BlockingWait();

        // Load identity registry
        var registry = services.GetRequiredService<IIdentityRegistry>();
        registry.LoadRegistrations().BlockingWait();
        var certificateStore = services.GetRequiredService<ICertificateStore>();
        DevEnvironmentSetup.ConfigureIfPresent(logger, config, registry, certificateStore);

        // Check for singleton dependencies
        if (Env.IsDevelopment())
        {
            var root = services.GetRequiredService<IMultiTenantContainerAccessor>().Container();
            new AutofacDiagnostics(root, logger).AssertSingletonDependencies();
        }

        // Sanity ping cache
        logger.LogInformation("Level2CacheType: {Level2CacheType}", config.Cache.Level2CacheType);
        var cache = services.GetRequiredService<IGlobalLevel2Cache>();
        cache.Set("ping", "pong", TimeSpan.FromSeconds(1));
        var pong = cache.TryGet<string>("ping");
        if (pong != "pong")
        {
            throw new OdinSystemException("Cache sanity check failed");
        }

        // Ensure S3 bucket exists
        logger.LogInformation("S3PayloadStorage enabled: {enabled}", config.S3PayloadStorage.Enabled);
        if (config.S3PayloadStorage.Enabled)
        {
            logger.LogInformation("Creating S3 bucket '{BucketName}' at {ServiceUrl}",
                config.S3PayloadStorage.BucketName, config.S3PayloadStorage.ServiceUrl);
            var payloadBucket = services.GetRequiredService<IS3PayloadStorage>();
            payloadBucket.CreateBucketAsync().BlockingWait();
        }

        // Start system background services
        if (config.BackgroundServices.SystemBackgroundServicesEnabled)
        {
            services.StartSystemBackgroundServices().BlockingWait();
        }

        //
        // DON'T PUT ANY INITIALIZATION CODE BELOW THIS LINE
        //
        logger.LogDebug("Finished initialization in {method}", nameof(BeforeApplicationStarting));

        return host;
    }

    //

    public static IHost OnApplicationStopping(this IHost host)
    {
        if (_didCleanUp)
        {
            return host;
        }

        _didCleanUp = true;

        var services = host.Services;
        var logger = services.GetRequiredService<ILogger<Startup>>();

        logger.LogDebug("Starting clean up in {method}", nameof(OnApplicationStopping));

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

        // DON'T PUT ANY CLEANUP BELOW THIS LINE
        logger.LogDebug("Finished clean up in {method}", nameof(OnApplicationStopping));

        return host;
    }

    //

    // Returns true if the web server should be started, false if it should not.
    public static bool ProcessCommandLineArgs(this IHost host, string[] args)
    {
        if (args.Contains("--dont-start-the-web-server"))
        {
            // This is a one-off command example, don't start the web server.
            return false;
        }

        if (args.Contains("--migrate-drive-grants", StringComparer.OrdinalIgnoreCase))
        {
            var services = host.Services;
            var logger = services.GetRequiredService<ILogger<Startup>>();

            logger.LogDebug("Starting drive-grant migration; stopping host");
            MigrateDriveGrants(services).GetAwaiter().GetResult();
            logger.LogDebug("Finished drive-grant migration; stopping host");
            return false;
        }

        if (args.Length == 2 && args[0] == "defragment")
        {
            DefragmentAsync(host.Services, args[1] == "cleanup").BlockingWait();
            return false;
        }

        return true;
    }

    //

    private static async Task MigrateDriveGrants(IServiceProvider services)
    {
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var migrationLogger = loggerFactory.CreateLogger("Migration");
        var tenantContainer = services.GetRequiredService<IMultiTenantContainerAccessor>().Container();

        var allTenants = await registry.GetTenants();

        foreach (var tenant in allTenants)
        {
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            migrationLogger.LogInformation("Starting migration for {tenant}; id: {id}", tenant.PrimaryDomainName, tenant.Id);
            var circleMembershipService = scope.Resolve<CircleMembershipService>();
            await circleMembershipService.Temp_ReconcileCircleAndAppGrants();
        }
    }

    //

    private static async Task DefragmentAsync(IServiceProvider services, bool cleanup)
    {
        var config = services.GetRequiredService<OdinConfiguration>();
        if (config.S3PayloadStorage.Enabled)
        {
            throw new OdinSystemException("S3 defragmentation is not supported");
        }

        var logger = services.GetRequiredService<ILogger<Startup>>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var tenantContainer = services.GetRequiredService<IMultiTenantContainerAccessor>().Container();

        logger.LogInformation("Starting defragmentation; cleanup mode: {cleanup}", cleanup);

        var allTenants = await registry.GetTenants();
        foreach (var tenant in allTenants)
        {
            var scope = tenantContainer.GetTenantScope(tenant.PrimaryDomainName);
            var defragmenter = scope.Resolve<Defragmenter>();

            logger.LogInformation("Defragmenting {tenant}", tenant.PrimaryDomainName);
            await defragmenter.Defragment(cleanup);
        }
    }
}