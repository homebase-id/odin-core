using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autofac;
using DnsClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Login;
using Odin.Core.Dns;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Logging;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Tasks;
using Odin.Hosting.Authentication.Owner;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Admin;
using Odin.Hosting.Controllers.Registration;
using Odin.Hosting.Extensions;
using Odin.Hosting.Multitenant;
using Odin.Services.Admin.Tenants;
using Odin.Services.Background;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Dns;
using Odin.Services.Dns.PowerDns;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Email;
using Odin.Services.JobManagement;
using Odin.Services.LastSeen;
using Odin.Services.Registry;
using Odin.Services.Registry.Registration;
using Odin.Services.Tenant.Container;
using StackExchange.Redis;

namespace Odin.Hosting;

public static class SystemServices
{
    //

    internal static IServiceCollection ConfigureSystemServices(this IServiceCollection services, OdinConfiguration config)
    {
        services.AddSingleton(config);

        services.Configure<KestrelServerOptions>(options => { options.AllowSynchronousIO = true; });
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(config.Host.ShutdownTimeoutSeconds);
        });

        services.AddSingleton<IDynamicHttpClientFactory, DynamicHttpClientFactory>();
        services.AddSingleton<ISystemHttpClient, SystemHttpClient>();
        services.AddSingleton<FileReaderWriter>();
        services.AddSingleton<IForgottenTasks, ForgottenTasks>();
        services.AddSingleton<ISystemDomains, SystemDomains>();
        services.AddSingleton<ILastSeenService, LastSeenService>();

        services.AddSingleton<OwnerConsoleTokenManager>();
        services.AddSingleton<PasswordDataManager>();
        services.AddSingleton(new OdinCryptoConfig(
            config.Crypto.SaltSize, config.Crypto.SaltSize, config.Crypto.Iterations, config.Crypto.NonceSize));

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
            sp.GetRequiredService<ICertificateService>(),
            sp.GetRequiredService<IDynamicHttpClientFactory>(),
            sp.GetRequiredService<ISystemHttpClient>(),
            sp.GetRequiredService<IMultiTenantContainer>(),
            TenantServices.ConfigureTenantServices,
            config));

        services.AddSingleton(new CertificateStorageKey(config.CertificateRenewal.StorageKey));
        services.AddSingleton(new AcmeAccountConfig
        {
            AcmeContactEmail = config.CertificateRenewal.CertificateAuthorityAssociatedEmail,
        });
        services.AddSingleton<ILookupClient>(new LookupClient());
        services.AddSingleton<IAcmeHttp01TokenCache, AcmeHttp01TokenCache>();

        services.AddScoped<IIdentityRegistrationService, IdentityRegistrationService>();

        services.AddSingleton<IAuthoritativeDnsLookup, AuthoritativeDnsLookup>();
        services.AddSingleton<IDnsLookupService, DnsLookupService>();

        services.AddSingleton<IDnsRestClient>(sp => new PowerDnsRestClient(
            sp.GetRequiredService<ILogger<PowerDnsRestClient>>(),
            sp.GetRequiredService<IDynamicHttpClientFactory>(),
            new Uri($"https://{config.Registry.PowerDnsHostAddress}/api/v1"),
            config.Registry.PowerDnsApiKey));

        services.AddSingleton<ICertesAcme>(sp => new CertesAcme(
            sp.GetRequiredService<ILogger<CertesAcme>>(),
            sp.GetRequiredService<IAcmeHttp01TokenCache>(),
            sp.GetRequiredService<IDynamicHttpClientFactory>(),
            config.CertificateRenewal.UseCertificateAuthorityProductionServers));

        services.AddSingleton<ICertificateStore, CertificateStore>();
        services.AddSingleton<ICertificateService, CertificateService>();

        services.AddSingleton<IEmailSender>(sp => new MailgunSender(
            sp.GetRequiredService<ILogger<MailgunSender>>(),
            sp.GetRequiredService<IDynamicHttpClientFactory>(),
            config.Mailgun.ApiKey,
            config.Mailgun.EmailDomain,
            config.Mailgun.DefaultFrom));

        services.AddSingleton(sp => new AdminApiRestrictedAttribute(
            sp.GetRequiredService<ILogger<AdminApiRestrictedAttribute>>(),
            config.Admin.ApiEnabled,
            config.Admin.ApiKey,
            config.Admin.ApiKeyHttpHeaderName,
            config.Admin.ApiPort,
            config.Admin.Domain));

        services.AddSingleton(new RegistrationRestrictedAttribute(config.Registry.ProvisioningEnabled));

        services.AddTransient<ITenantAdmin, TenantAdmin>();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

        services.AddIpRateLimiter(config.Host.IpRateLimitRequestsPerSecond);

        if (config.Redis.Enabled)
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(config.Redis.Configuration));
        }

        if (config.Redis.Enabled)
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
            Level2CacheType = config.Cache.Level2CacheType,
        });

        // We currently don't use asp.net data protection, but we need to configure it to avoid warnings
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(config.Host.DataProtectionKeyPath));

        // Payload storage
        if (config.S3PayloadStorage.Enabled)
        {
            services.AddS3AwsPayloadStorage(
                config.S3PayloadStorage.AccessKey,
                config.S3PayloadStorage.SecretAccessKey,
                config.S3PayloadStorage.ServiceUrl,
                config.S3PayloadStorage.Region,
                config.S3PayloadStorage.ForcePathStyle,
                config.S3PayloadStorage.BucketName,
                config.S3PayloadStorage.RootPath);
        }

        return services;
    }

    //

    internal static ContainerBuilder ConfigureSystemServices(this ContainerBuilder builder, OdinConfiguration config)
    {
        builder.RegisterModule(new LoggingAutofacModule());
        builder.RegisterModule(new MultiTenantAutofacModule());

        builder.AddSystemBackgroundServices(config);
        builder.AddJobManagerServices();

        // Global database services
        builder.AddDatabaseServices();

        // System database services
        switch (config.Database.Type)
        {
            case DatabaseType.Sqlite:
                builder.AddSqliteSystemDatabaseServices(Path.Combine(config.Host.SystemDataRootPath, "sys.db"));
                break;
            case DatabaseType.Postgres:
                builder.AddPgsqlSystemDatabaseServices(config.Database.ConnectionString);
                break;
            default:
                throw new OdinSystemException("Unsupported database type");
        }

        // Global cache services
        builder.AddSystemCaches();

        return builder;
    }

    //

}