using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Core.Trie;
using Odin.Core.Util;
using Odin.Services.Background;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Registry.Registration;
using Odin.Services.Tenant.Container;
using StackExchange.Redis;
using static Org.BouncyCastle.Math.EC.ECCurve;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Odin.Services.Registry;

/// <summary>
/// Reads identities from the file system using a convention
/// </summary>
public class FileSystemIdentityRegistry : IIdentityRegistry
{
    public string RegistrationRoot { get; private set; }
    public string ShardablePayloadRoot { get; private set; }

    private readonly ILogger<FileSystemIdentityRegistry> _logger;
    private readonly ConcurrentDictionary<Guid, IdentityRegistration> _cache;
    private readonly Trie<IdentityRegistration> _trie;
    private readonly ICertificateServiceFactory _certificateServiceFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISystemHttpClient _systemHttpClient;
    private readonly IMultiTenantContainerAccessor _tenantContainer;
    private readonly Action<ContainerBuilder, IdentityRegistration, OdinConfiguration> _tenantContainerBuilder;
    private readonly OdinConfiguration _config;
    private readonly bool _useCertificateAuthorityProductionServers;
    private readonly string _tempFolderRoot;

    public FileSystemIdentityRegistry(
        ILogger<FileSystemIdentityRegistry> logger,
        ICertificateServiceFactory certificateServiceFactory,
        IHttpClientFactory httpClientFactory,
        ISystemHttpClient systemHttpClient,
        IMultiTenantContainerAccessor tenantContainer,
        Action<ContainerBuilder, IdentityRegistration, OdinConfiguration> tenantContainerBuilder,
        OdinConfiguration config
    )
    {
        var tenantDataRootPath = config.Host.TenantDataRootPath;
        RegistrationRoot = Path.Combine(tenantDataRootPath, "registrations");
        ShardablePayloadRoot = Path.Combine(tenantDataRootPath, "payloads");
        _tempFolderRoot = tenantDataRootPath;

        _cache = new ConcurrentDictionary<Guid, IdentityRegistration>();
        _trie = new Trie<IdentityRegistration>();
        _logger = logger;
        _certificateServiceFactory = certificateServiceFactory;
        _httpClientFactory = httpClientFactory;
        _systemHttpClient = systemHttpClient;
        _tenantContainer = tenantContainer;
        _tenantContainerBuilder = tenantContainerBuilder;
        _config = config;

        _useCertificateAuthorityProductionServers = config.CertificateRenewal.UseCertificateAuthorityProductionServers;

        RegisterCertificateInitializerHttpClient();
    }

    public Guid? ResolveId(string domain)
    {
        var reg = _trie.LookupExactName(domain);
        return reg?.Id;
    }

    public IdentityRegistration ResolveIdentityRegistration(string domain, out string prefix)
    {
        if (string.IsNullOrEmpty(domain))
        {
            prefix = "";
            return null;
        }

        var (reg, pre) = _trie.LookupName(domain);

        prefix = pre;
        if (reg == null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(prefix) || DnsConfigurationSet.WellknownPrefixes.Contains(prefix))
        {
            return reg;
        }

        return null;
    }


    public TenantContext CreateTenantContext(string domain, bool updateFileSystem = false)
    {
        var idReg = this.ResolveIdentityRegistration(domain, out _);
        return this.CreateTenantContext(idReg, updateFileSystem);
    }

    public TenantContext CreateTenantContext(IdentityRegistration idReg, bool updateFileSystem = false)
    {
        var regIdFolder = idReg.Id.ToString();
        var rootPath = Path.Combine(RegistrationRoot, regIdFolder);

        var isPreconfigured = _config.Development?.PreconfiguredDomains.Any(d => d.Equals(idReg.PrimaryDomainName,
            StringComparison.InvariantCultureIgnoreCase)) ?? false;

        var tenantPathManager = new TenantPathManager(_config, idReg.PayloadShardKey, idReg.Id);

        if (updateFileSystem)
        {
            tenantPathManager.CreateDirectories();
            tenantPathManager.CreateSslRootDirectory();
        }

        var tc = new TenantContext(
            idReg.Id,
            (OdinId)idReg.PrimaryDomainName,
            tenantPathManager,
            idReg.FirstRunToken,
            isPreconfigured,
            idReg.MarkedForDeletionDate);

        return tc;
    }

    public Task<bool> IsIdentityRegistered(string domain)
    {
        return Task.FromResult(_trie.LookupExactName(domain) != null);
    }

    public async Task<bool> CanAddNewRegistration(string domain)
    {
        if (!_trie.IsDomainUniqueInHierarchy(domain))
        {
            return false;
        }
        var registration = await GetAsync(domain);
        return registration == null;
    }

    public async Task<Guid> AddRegistration(IdentityRegistrationRequest request)
    {
        string GetNextShard()
        {
            //TODO: read folders under this.ShardablePayloadRoot and choose a folder; wisely (maybe round robin?)
            const string shard1 = "shard1";
            return shard1;
        }

        var registration = new IdentityRegistration()
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PlanId = request.PlanId,
            PrimaryDomainName = request.OdinId,
            IsCertificateManaged = request.IsCertificateManaged,
            FirstRunToken = Guid.NewGuid(),
            PayloadShardKey = GetNextShard()
        };




        // Create directories
        var tenantPathManager = new TenantPathManager(_config, registration.PayloadShardKey, registration.Id);
        tenantPathManager.CreateDirectories();
        //var storageConfig = GetStorageConfig(registration);
        //storageConfig.CreateDirectories();

        // Create database on isolated scope
        await using var scope = GetOrCreateMultiTenantScope(registration)
            .BeginLifetimeScope($"AddRegistration:{registration.PrimaryDomainName}");

        var identityDatabase = scope.Resolve<IdentityDatabase>();
        await identityDatabase.CreateDatabaseAsync(false);

        await SaveRegistrationInternal(registration);

        if (request.OptionalCertificatePemContent == null)
        {
            await InitializeCertificate(request.OdinId);
        }
        else
        {
            //optionally, let an ssl certificate be provided 
            //TODO: is there a way to pull a specific tenant's service config from Autofac?
            var tenantContext = CreateTenantContext(request.OdinId, true);

            var tc = _certificateServiceFactory.Create(tenantContext.TenantPathManager.SslStoragePath);
            tc.SaveSslCertificate(
                request.OdinId.DomainName,
                new KeysAndCertificates
                {
                    CertificatesPem = request.OptionalCertificatePemContent.Certificate,
                    PrivateKeyPem = request.OptionalCertificatePemContent.PrivateKey,
                });
        }

        CacheCertificate(registration);
        await InitializeOdinContextCache(registration);
        if (_config.Job.TenantJobsEnabled)
        {
            await StartBackgroundServices(registration);
        }

        return registration.FirstRunToken.GetValueOrDefault();
    }

    public async Task DeleteRegistration(string domain)
    {
        var registration = await GetAsync(domain);

        if (null != registration)
        {
            _trie.RemoveDomain(domain);
            await UnloadRegistration(registration);
            var tenantRoot = Path.Combine(RegistrationRoot, registration.Id.ToString());
            Directory.Delete(tenantRoot, true);
            await DeletePayloads(registration);
        }
    }

    // Copy registration and payloads
    public async Task<string> CopyRegistration(string domain, string targetRootPath)
    {
        var registration = await GetAsync(domain);
        if (registration == null)
        {
            return "";
        }

        var disabled = registration.Disabled;
        await ToggleDisabled(domain, true);
        try
        {
            var targetPath = Path.Combine(targetRootPath, domain);
            if (Directory.Exists(targetPath))
            {
                throw new OdinClientException($"Path {targetPath} already exists");
            }

            var registrationId = registration.Id.ToString();
            var targetRegistrationsPath = Path.Combine(targetPath, "registrations", registrationId);
            Directory.CreateDirectory(targetRegistrationsPath);

            _logger.LogInformation("Copying {domain} registration to {targetRegistrationsPath}", domain, targetRegistrationsPath);
            var source = new DirectoryInfo(Path.Combine(RegistrationRoot, registrationId));
            await Task.Run(() => source.CopyTo(targetRegistrationsPath));

            var targetPayloadsPath = Path.Combine(targetPath, "payloads");
            Directory.CreateDirectory(targetPayloadsPath);

            var shards = Directory.GetDirectories(ShardablePayloadRoot);
            foreach (var shard in shards)
            {
                var payloadSourcePath = Path.Combine(shard, registrationId);
                var payloadTargetPath = Path.Combine(targetPayloadsPath, Path.GetFileName(shard), registrationId);
                if (Directory.Exists(payloadSourcePath))
                {
                    _logger.LogInformation("Copying {domain} shard to {payloadTargetPath}", domain, payloadTargetPath);
                    source = new DirectoryInfo(Path.Combine(payloadSourcePath));
                    await Task.Run(() => source.CopyTo(payloadTargetPath));
                }
            }

            return targetPath;
        }
        finally
        {
            await ToggleDisabled(domain, disabled);
        }
    }

    public async Task MarkRegistrationComplete(Guid firstRunToken)
    {
        var registration = GetByFirstRunToken(firstRunToken);
        registration.FirstRunToken = null;
        await this.SaveRegistrationInternal(registration);
    }

    public async Task<RegistrationStatus> GetRegistrationStatus(Guid firstRunToken)
    {
        var registration = GetByFirstRunToken(firstRunToken);

        if (null == registration)
        {
            return RegistrationStatus.Unknown;
        }

        //the other option here is to load the certs via the registry, which i dont like
        var svc = _systemHttpClient.CreateHttps<ICertificateStatusHttpClient>((OdinId)registration.PrimaryDomainName);
        try
        {
            var certsValidResponse = await svc.VerifyCertificatesValid();
            if (certsValidResponse.IsSuccessStatusCode)
            {
                if (certsValidResponse.Content)
                {
                    return RegistrationStatus.ReadyForPassword;
                }
                else
                {
                    return RegistrationStatus.AwaitingCertificate;
                }
            }
        }
        catch (System.Net.Http.HttpRequestException)
        {
            //hre.HResult == -2146232800
            return RegistrationStatus.AwaitingCertificate;
        }
        catch (Exception)
        {
            return RegistrationStatus.Unknown;
        }

        //TODO: Log system error here?

        return RegistrationStatus.Unknown;
    }

    private async Task SaveRegistrationInternal(IdentityRegistration registration)
    {
        var json = OdinSystemSerializer.Serialize(registration);
        var regFilePath = GetRegFilePath(registration.Id);
        await File.WriteAllTextAsync(regFilePath, json);

        _logger.LogInformation("Wrote registration file for [{registrationId}]", registration.Id);
        await CacheIdentity(registration);
    }

    public Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions = null)
    {
        var list = _cache.Values.ToList();
        return Task.FromResult(new PagedResult<IdentityRegistration>(PageOptions.All, 1, list));
    }
    
    public Task<List<IdentityRegistration>> GetTenants()
    {
        var list = _cache.Values.ToList();
        return Task.FromResult(list);
    }

    public Task<IdentityRegistration> GetAsync(string domain)
    {
        var reg = _trie.LookupExactName(domain);
        return Task.FromResult(reg);
    }

    public async Task<bool?> ToggleDisabled(string domain, bool disabled)
    {
        bool? result = null;
        var reg = _trie.LookupExactName(domain);
        if (reg != null)
        {
            result = reg.Disabled;
            if (reg.Disabled != disabled)
            {
                reg.Disabled = disabled;
                await SaveRegistrationInternal(reg);
            }
        }

        return result;
    }

    public async Task<UnixTimeUtc> MarkForDeletionAsync(string domain)
    {
        var reg = _trie.LookupExactName(domain);
        if (reg == null)
        {
            throw new OdinClientException("Invalid domain");
        }

        var markedDate = UnixTimeUtc.Now();
        reg.MarkedForDeletionDate = UnixTimeUtc.Now();
        await SaveRegistrationInternal(reg);

        return markedDate.AddDays(_config.Registry.DaysUntilAccountDeletion);
    }

    public async Task UnmarkForDeletionAsync(string domain)
    {
        var reg = _trie.LookupExactName(domain);
        if (reg == null)
        {
            throw new OdinClientException("Invalid domain");
        }

        reg.MarkedForDeletionDate = null;
        await SaveRegistrationInternal(reg);
    }

    private string GetRegFilePath(Guid registrationId)
    {
        return Path.Combine(RegistrationRoot, registrationId.ToString(), "reg.json");
    }

    public async Task LoadRegistrations()
    {
        Directory.CreateDirectory(RegistrationRoot);
        if (!Directory.Exists(RegistrationRoot))
        {
            throw new OdinSystemException($"Directory does not exist: [{RegistrationRoot}]");
        }

        Directory.CreateDirectory(ShardablePayloadRoot);
        if (!Directory.Exists(RegistrationRoot))
        {
            throw new OdinSystemException($"Directory does not exist: [{RegistrationRoot}]");
        }

        var directories = Directory.GetDirectories(RegistrationRoot);

        foreach (var dir in directories)
        {
            try
            {
                var path = Path.TrimEndingDirectorySeparator(dir.ToCharArray()).ToString();
                var potentialId = path.Split(Path.DirectorySeparatorChar).Last();
                if (!Guid.TryParse(potentialId, out var id))
                {
                    _logger.LogWarning(
                        "Identity Registry: Found invalid folder not in GUID format named [{potentialId}]; moving to next", potentialId);
                    continue;
                }
 
                var registration = LoadRegistration(id);
                if (registration == null)
                {
                    _logger.LogWarning("Identity Registry: could not find reg file for Id: [{id}]; moving to next", id.ToString());
                    continue;
                }

                var tenantPathManger = new TenantPathManager(_config, registration.PayloadShardKey, registration.Id);
                tenantPathManger.CreateDirectories();
                //var storageConfig = GetStorageConfig(registration);
                //storageConfig.CreateDirectories();

                // Sanity: create database if missing (can be necessary when switching dev from sqlite to postgres)
                await using var scope = GetOrCreateMultiTenantScope(registration)
                    .BeginLifetimeScope($"LoadRegistrations:{registration.PrimaryDomainName}");
                var identityDatabase = scope.Resolve<IdentityDatabase>();
                await identityDatabase.CreateDatabaseAsync(false);

                var (requiresUpgrade, tenantVersion, _) = await scope.Resolve<VersionUpgradeScheduler>().RequiresUpgradeAsync();
                if (requiresUpgrade)
                {
                    _logger.LogDebug("{tenant} is on data-release-version {currentVersion}; latest version is {latestVersion}",
                        registration.PrimaryDomainName,
                        tenantVersion,
                        Version.DataVersionNumber);
                }
                else
                {
                    _logger.LogDebug("{tenant} is on latest data version number v{latestVersion}",
                        registration.PrimaryDomainName,
                        Version.DataVersionNumber);
                }

                _logger.LogInformation("Loaded Identity {identity} ({id})", registration.PrimaryDomainName, registration.Id);
                await CacheIdentity(registration);

                CacheCertificate(registration);
                await InitializeOdinContextCache(registration);
                if (_config.Job.TenantJobsEnabled)
                {
                    await StartBackgroundServices(registration);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Identity Registry: Failed to load identity at path {dir}", dir);
            }
        }
    }

    private IdentityRegistration LoadRegistration(Guid id)
    {
        var regFile = GetRegFilePath(id);

        if (!File.Exists(regFile))
        {
            return null;
        }

        var json = File.ReadAllText(regFile);

        var registration = OdinSystemSerializer.Deserialize<IdentityRegistration>(json);
        return registration;
    }

    private async Task CacheIdentity(IdentityRegistration registration)
    {
        // BE VERY CAREFUL NOT TO START ANY DATABASE TRANSACTIONS HERE!!
        //
        // This method is called indirectly from other requests using their own scope, which
        // can conflict with the scope here, causing transaction deadlocks.

        RegisterDotYouHttpClient(registration);

        _trie.TryRemoveDomain(registration.PrimaryDomainName);
        _trie.AddDomain(registration.PrimaryDomainName, registration);
        _cache[registration.Id] = registration;

        await using var scope = GetOrCreateMultiTenantScope(registration)
            .BeginLifetimeScope($"CacheIdentity:{registration.PrimaryDomainName}:{Guid.NewGuid()}");

        var tenantContext = scope.Resolve<TenantContext>();
        var tc = CreateTenantContext(registration.PrimaryDomainName);
        tenantContext.Update(tc);

        var driveManager = scope.Resolve<DriveManager>();
        await driveManager.LoadCacheAsync();

        var tenantConfigService = scope.Resolve<TenantConfigService>();
        await tenantConfigService.InitializeAsync();
    }

    private async Task UnloadRegistration(IdentityRegistration registration)
    {
        _cache.TryRemove(registration.Id, out _);
        await StopBackgroundServices(registration);
        RemoveMultiTenantScope(registration.PrimaryDomainName);
    }

    private IdentityRegistration GetByFirstRunToken(Guid firstRunToken)
    {
        var registration = _cache.Values.SingleOrDefault(reg => reg.FirstRunToken == firstRunToken);
        if (null == registration)
        {
            throw new OdinClientException("Invalid first run token", OdinClientErrorCode.UnknownId);
        }

        return registration;
    }

    private async Task InitializeCertificate(string domain)
    {
        var httpClient = _httpClientFactory.CreateClient(nameof(RegisterCertificateInitializerHttpClient));
        var uri = $"https://{domain}:{_config.Host.DefaultHttpsPort}/.well-known/acme-challenge/ping";
        try
        {
            await httpClient.GetAsync(uri);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("InitializeCertificate took too long to complete and the http request was cancelled");
        }
        catch (HttpRequestException e)
        {
            // This can happen if a new identity gets created, but the DNS server the backed uses does not yet
            // know the domain
            _logger.LogWarning("InitializeCertificate: {error}. Will retry on next request to the domain.", e.Message);
        }
    }

    private void RegisterCertificateInitializerHttpClient()
    {
        _httpClientFactory.Register(nameof(RegisterCertificateInitializerHttpClient), builder => builder
            .ConfigureHttpClient(c =>
            {
                // this is called everytime you request a httpclient
                c.Timeout = TimeSpan.FromMinutes(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false, // DO NOT CHANGE!
                };

                // Make sure we accept certificates from letsencrypt staging servers if not in production
                if (!_useCertificateAuthorityProductionServers)
                {
                    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                }

                return handler;
            }));
    }

    private void RegisterDotYouHttpClient(IdentityRegistration idReg)
    {
        var tenantContext = this.CreateTenantContext(idReg);
        var domain = idReg.PrimaryDomainName;
        var httpClientKey = OdinHttpClientFactory.HttpFactoryKey(domain);

        // SEB:NOTE
        // Below is the reason that we have to use IHttpClientFactory from HttpClientFactoryLite instead of the
        // baked-in one. We have to be able to create HttpClientHandlers on the fly, this is not possible with
        // the original IHttpClientFactory.
        _httpClientFactory.Register(httpClientKey, builder => builder.ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false, // DO NOT CHANGE!
                AllowAutoRedirect = false,
                SslProtocols = SslProtocols.None, //allow OS to choose;
            };

            var tc = _certificateServiceFactory.Create(tenantContext.TenantPathManager.SslStoragePath);
            var x509 = tc.GetSslCertificate(domain);
            if (x509 != null)
            {
                handler.ClientCertificates.Add(x509);
            }
            else
            {
                _logger.LogError("RegisterHttpClient: could not find certificate for {domain}", domain);
            }
            
            // Make sure we accept certificates from letsencrypt staging servers if not in production
            if (!_useCertificateAuthorityProductionServers)
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }

            return handler;
        }).SetHandlerLifetime(TimeSpan.FromMinutes(2)));
    }

    //

    private Task DeletePayloads(IdentityRegistration identity)
    {
        return Task.Run(() =>
        {
            var shards = Directory.GetDirectories(ShardablePayloadRoot);
            foreach (var shard in shards)
            {
                var id = identity.Id.ToString();

                // Sanity
                if (string.IsNullOrEmpty(shard) || string.IsNullOrEmpty(id))
                {
                    throw new OdinSystemException("I just stopped you in wiping the wrong stuff!");
                }

                _logger.LogInformation("Deleting shard {shard} on {domain}", shard, identity.PrimaryDomainName);

                var payloadPath = Path.Combine(shard, id);
                if (Directory.Exists(payloadPath))
                {
                    try
                    {
                        Directory.Delete(payloadPath, true);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error deleting payload in '{path}': {error}", payloadPath, e.Message);
                    }
                }
            }
        });
    }

    //

    private void CacheCertificate(IdentityRegistration registration)
    {
        var scope = _tenantContainer.Container().GetTenantScope(registration.PrimaryDomainName);
        var tenantContext = scope.Resolve<TenantContext>();
        var certificateServiceFactory = scope.Resolve<ICertificateServiceFactory>();
        var certificateService = certificateServiceFactory.Create(tenantContext.TenantPathManager.SslStoragePath);
        var certificate = certificateService.ResolveCertificate(registration.PrimaryDomainName);
        if (certificate != null)
        {
            _logger.LogInformation("Certificate loaded for {domain}", registration.PrimaryDomainName);
        }
        else
        {
            _logger.LogWarning("No certificate loaded for {domain} (yet)", registration.PrimaryDomainName);
        }
    }

    //

    private async Task InitializeOdinContextCache(IdentityRegistration registration)
    {
        if (_config.Cache.Level2CacheType == Level2CacheType.Redis)
        {
            var scope = _tenantContainer.Container().GetTenantScope(registration.PrimaryDomainName);
            var multiplexer = scope.Resolve<IConnectionMultiplexer>();
            var odinContextCache = scope.Resolve<OdinContextCache>();
            await odinContextCache.InitializePubSub(multiplexer);
        }
    }

    //

    private async Task StartBackgroundServices(IdentityRegistration registration)
    {
        var scope = _tenantContainer.Container().GetTenantScope(registration.PrimaryDomainName);
        await scope.StartTenantBackgroundServices();
    }

    //

    private async Task StopBackgroundServices(IdentityRegistration registration)
    {
        var scope = _tenantContainer.Container().GetTenantScope(registration.PrimaryDomainName);
        var backgroundServiceManager = scope.Resolve<IBackgroundServiceManager>();
        await backgroundServiceManager.ShutdownAsync();
    }

    //

    private ILifetimeScope GetOrCreateMultiTenantScope(IdentityRegistration registration)
    {
        var scope = _tenantContainer.Container().GetOrAddTenantScope(
            registration.PrimaryDomainName,
            cb => _tenantContainerBuilder(cb, registration, _config));

        return scope;
    }

    //

    private void RemoveMultiTenantScope(string domain)
    {
        _tenantContainer.Container().RemoveTenantScope(domain);
    }


}