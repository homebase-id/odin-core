using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Odin.Services.Tests.Base;

public class OdinContextCacheTest
{
    private RedisContainer? _redisContainer;
    private ILifetimeScope? _services;

    [SetUp]
    public void Setup()
    {
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
            _redisContainer = null;
        }
        _services?.Dispose();
        _services = null;
    }

    //

    private async Task RegisterServicesAsync(Level2CacheType level2CacheType)
    {
        if (level2CacheType == Level2CacheType.Redis)
        {
            _redisContainer = new RedisBuilder()
                .WithImage("redis:latest")
                .Build();
            await _redisContainer.StartAsync();
        }

        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        if (_redisContainer != null)
        {
            var redisConfig = _redisContainer?.GetConnectionString() ?? throw new InvalidOperationException();
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));
        }

        services.AddCoreCacheServices(new CacheConfiguration
        {
            Level2CacheType = level2CacheType,
        });

        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.AddGlobalCaches();
        builder.AddTenantCaches("frodo.me");

        builder.RegisterType<OdinContextCache>().SingleInstance();

        _services = builder.Build();
    }

    //

    private OdinContext CreateOdinContext()
    {
        var odinContext = new OdinContext();
        odinContext.SetAuthContext("AuthContext");

        var odinId = new OdinId("frodo.me");
        odinContext.Tenant = odinId;
        odinContext.AuthTokenCreated = UnixTimeUtc.Now();

        //
        // CallerContext
        //

        var masterKey = new SensitiveByteArray("masterKey".ToUtf8ByteArray());
        var odinClientContext = new OdinClientContext
        {
            CorsHostName = "CorsHostName",
            AccessRegistrationId = new GuidId(Guid.NewGuid()),
            DevicePushNotificationKey = Guid.NewGuid(),
            ClientIdOrDomain = "frodo.me",
        };
        var circleIds = new List<GuidId> { new GuidId(Guid.NewGuid()) };
        odinContext.Caller = new CallerContext(
            odinId,
            masterKey,
            SecurityGroupType.Anonymous,
            odinClientContext,
            circleIds);

        //
        // PermissionContext
        //
        var permissionSet = new PermissionSet(1, 2, 3);
        var driveGrants = new List<DriveGrant>
        {
            new DriveGrant
            {
                DriveId = new GuidId(Guid.NewGuid()),
                PermissionedDrive = new PermissionedDrive
                {
                    Drive = new TargetDrive
                    {
                        Alias = new GuidId(Guid.NewGuid()),
                        Type = new GuidId(Guid.NewGuid()),
                    },
                    Permission = DrivePermission.Read
                }
            }
        };
        var keyStoreKey = new SensitiveByteArray("keyStoreKey".ToUtf8ByteArray());
        var encryptedIcrKey = new SymmetricKeyEncryptedAes(new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16)));
        var permissionGroup = new PermissionGroup(permissionSet, driveGrants, keyStoreKey, encryptedIcrKey);
        var permissionGroups = new Dictionary<string, PermissionGroup>
        {
            { "read_anonymous_drives", permissionGroup }
        };
        var sharedSecretKey = new SensitiveByteArray("sharedSecretKey".ToUtf8ByteArray());
        odinContext.SetPermissionContext(new PermissionContext(permissionGroups, sharedSecretKey));

        return odinContext;
    }

    //

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldCacheOdinContext(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<OdinContextCache>();

        var token = ClientAuthenticationToken.FromPortableBytes($"my-token-{Guid.NewGuid()}".ToUtf8ByteArray());

        var creator = new Func<Task<IOdinContext?>>(() =>
        {
            var odinContext = CreateOdinContext();
            return Task.FromResult<IOdinContext?>(odinContext);
        });

        var oc1 = await cache.GetOrAddContextAsync(token, creator);
        var oc2 = await cache.GetOrAddContextAsync(token, creator);

        Assert.That(oc1, Is.Not.Null);
        Assert.That(oc2, Is.Not.Null);

        Assert.That(oc2!.AuthContext, Is.EqualTo("AuthContext"));
        Assert.That(oc2.Tenant.DomainName, Is.EqualTo("frodo.me"));
        Assert.That(oc2.AuthTokenCreated, Is.Not.Null);

        Assert.That(oc2.AuthContext, Is.EqualTo(oc1!.AuthContext));
        Assert.That(oc2.Tenant.DomainName, Is.EqualTo(oc1.Tenant.DomainName));
        Assert.That(oc2.AuthTokenCreated, Is.EqualTo(oc1.AuthTokenCreated));
    }

}