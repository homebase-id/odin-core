using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.ObjectStorage;
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

namespace Odin.Services.Registry;

/// <summary>
/// Reads identities from the file system using a convention
/// </summary>
public class FileSystemIdentityRegistry : IIdentityRegistry
{
    public string RegistrationRoot { get; private set; }
    public string PayloadRoot { get; private set; }

    private readonly ILogger<FileSystemIdentityRegistry> _logger;
    private readonly ConcurrentDictionary<Guid, IdentityRegistration> _cache;
    private readonly Trie<IdentityRegistration> _trie;
    private readonly ICertificateService _certificateService;
    private readonly IDynamicHttpClientFactory _httpClientFactory;
    private readonly ISystemHttpClient _systemHttpClient;
    private readonly IMultiTenantContainer _serviceProvider;
    private readonly Func<ContainerBuilder, IdentityRegistration, OdinConfiguration, ContainerBuilder> _tenantContainerBuilder;
    private readonly OdinConfiguration _config;

    public FileSystemIdentityRegistry(
        ILogger<FileSystemIdentityRegistry> logger,
        ICertificateService certificateService,
        IDynamicHttpClientFactory httpClientFactory,
        ISystemHttpClient systemHttpClient,
        IMultiTenantContainer serviceProvider,
        Func<ContainerBuilder, IdentityRegistration, OdinConfiguration, ContainerBuilder> tenantContainerBuilder,
        OdinConfiguration config
    )
    {
        var tenantDataRootPath = config.Host.TenantDataRootPath;
        RegistrationRoot = Path.Combine(tenantDataRootPath, TenantPathManager.RegistrationsFolder);
        PayloadRoot = Path.Combine(tenantDataRootPath, TenantPathManager.PayloadsFolder);

        _cache = new ConcurrentDictionary<Guid, IdentityRegistration>();
        _trie = new Trie<IdentityRegistration>();
        _logger = logger;
        _certificateService = certificateService;
        _httpClientFactory = httpClientFactory;
        _systemHttpClient = systemHttpClient;
        _serviceProvider = serviceProvider;
        _tenantContainerBuilder = tenantContainerBuilder;
        _config = config;
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
        var isPreconfigured = _config.Development?.PreconfiguredDomains.Any(d => d.Equals(idReg.PrimaryDomainName,
            StringComparison.InvariantCultureIgnoreCase)) ?? false;

        var tenantPathManager = new TenantPathManager(_config, idReg.Id);

        if (updateFileSystem)
        {
            tenantPathManager.CreateDirectories();
        }

        var tc = new TenantContext(
            idReg.Id,
            (OdinId)idReg.PrimaryDomainName,
            tenantPathManager,
            idReg.FirstRunToken,
            isPreconfigured,
            idReg.MarkedForDeletionDate,
            idReg.Email);

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
        var registration = new IdentityRegistration()
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PlanId = request.PlanId,
            PrimaryDomainName = request.OdinId,
            IsCertificateManaged = request.IsCertificateManaged,
            FirstRunToken = Guid.NewGuid()
        };

        // Create directories
        var tenantPathManager = new TenantPathManager(_config, registration.Id);
        tenantPathManager.CreateDirectories();

        // Create database on isolated scope
        _logger.LogInformation("Migrating database for {database}", registration.PrimaryDomainName);
        await using var scope = GetOrCreateMultiTenantScope(registration)
            .BeginLifetimeScope($"AddRegistration:{registration.PrimaryDomainName}");
        var identityDatabase = scope.Resolve<IdentityDatabase>();
        await identityDatabase.MigrateDatabaseAsync();

        await SaveRegistrationInternal(registration);

        if (request.OptionalCertificatePemContent == null)
        {
            await InitializeCertificate(request.OdinId);
        }
        else
        {
            //optionally, let an ssl certificate be provided 
            await _certificateService.PutCertificateAsync(
                request.OdinId.DomainName,
                new KeysAndCertificates
                {
                    CertificatesPem = request.OptionalCertificatePemContent.Certificate,
                    PrivateKeyPem = request.OptionalCertificatePemContent.PrivateKey,
                });
        }

        await CacheCertificateAsync(registration);
        await InitializeOdinContextCache(registration);
        if (_config.BackgroundServices.TenantBackgroundServicesEnabled)
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

        // SEB:TODO update for S3 payloads
        if (_config.S3PayloadStorage.Enabled)
        {
            throw new OdinSystemException("Copying registrations with S3 payloads is not supported yet.");
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
            var targetRegistrationsPath = Path.Combine(targetPath, TenantPathManager.RegistrationsFolder, registrationId);
            Directory.CreateDirectory(targetRegistrationsPath);

            _logger.LogInformation("Copying {domain} registration to {targetRegistrationsPath}", domain, targetRegistrationsPath);
            var source = new DirectoryInfo(Path.Combine(RegistrationRoot, registrationId));
            await Task.Run(() => source.CopyTo(targetRegistrationsPath));

            var targetPayloadsPath = Path.Combine(targetPath, TenantPathManager.PayloadsFolder);
            Directory.CreateDirectory(targetPayloadsPath);

            var shards = Directory.GetDirectories(PayloadRoot);
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
        await using var scope = GetOrCreateMultiTenantScope(registration)
            .BeginLifetimeScope($"SaveRegistration:{registration.PrimaryDomainName}");

        var systemDatabase = scope.Resolve<SystemDatabase>();
        await systemDatabase.Registrations.UpsertAsync(new RegistrationsRecord
        {
            identityId = registration.Id,
            primaryDomainName = registration.PrimaryDomainName.ToLower(),
            email = registration.Email?.ToLower(),
            firstRunToken = registration.FirstRunToken?.ToString(),
            disabled = false,
            markedForDeletionDate = null,
            planId = "free"
        });

        _logger.LogInformation("Wrote registration record for [{registrationId}]", registration.Id);
        await CacheIdentityAsync(registration);
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

    public async Task LoadRegistrations()
    {
        Directory.CreateDirectory(RegistrationRoot);
        if (!_config.S3PayloadStorage.Enabled)
        {
            Directory.CreateDirectory(PayloadRoot);
        }

        await using var systemScope = _serviceProvider.BeginLifetimeScope();
        var systemDatabase = systemScope.Resolve<SystemDatabase>();
        var registrations = await systemDatabase.Registrations.GetAllAsync();
        foreach (var registrationRecord in registrations)
        {
            try
            {
                var identityId = registrationRecord.identityId.ToString();
                var registrationPath = Path.Combine(RegistrationRoot, identityId);

                // Scalability: ensure the registration directory exists on all hosts
                Directory.CreateDirectory(registrationPath);

                var registration = new IdentityRegistration
                {
                    Id = registrationRecord.identityId,
                    PrimaryDomainName = registrationRecord.primaryDomainName,
                    Email = registrationRecord.email,
                    FirstRunToken = string.IsNullOrEmpty(registrationRecord.firstRunToken)
                        ? null
                        : Guid.Parse(registrationRecord.firstRunToken),
                    PlanId = registrationRecord.planId,
                    Disabled = registrationRecord.disabled,
                    MarkedForDeletionDate = registrationRecord.markedForDeletionDate,
                    // LastSeen = registrationRecord.lastSeen // SEB:TODO
                };

                var tenantPathManger = new TenantPathManager(_config, registration.Id);
                tenantPathManger.CreateDirectories();

                // Sanity: create database if missing (can be necessary when switching dev from sqlite to postgres)
                _logger.LogInformation("Migrating database for {database}", registration.PrimaryDomainName);
                await using var tenantScope = GetOrCreateMultiTenantScope(registration)
                    .BeginLifetimeScope($"LoadRegistrations:{registration.PrimaryDomainName}");
                var identityDatabase = tenantScope.Resolve<IdentityDatabase>();
                await identityDatabase.MigrateDatabaseAsync();

                var (requiresUpgrade, tenantVersion, _) = await tenantScope.Resolve<VersionUpgradeScheduler>().RequiresUpgradeAsync();
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
                await CacheIdentityAsync(registration);

                await CacheCertificateAsync(registration);
                await InitializeOdinContextCache(registration);

                if (_config.BackgroundServices.TenantBackgroundServicesEnabled)
                {
                    await StartBackgroundServices(registration);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error loading registration {id}: {error}", registrationRecord.identityId, e.Message);
            }
        }
    }

    private async Task CacheIdentityAsync(IdentityRegistration registration)
    {
        // BE VERY CAREFUL NOT TO START ANY DATABASE TRANSACTIONS HERE!!
        //
        // This method is called indirectly from other requests using their own scope, which
        // can conflict with the scope here, causing transaction deadlocks.

        _trie.TryRemoveDomain(registration.PrimaryDomainName);
        _trie.AddDomain(registration.PrimaryDomainName, registration);
        _cache[registration.Id] = registration;

        await using var scope = GetOrCreateMultiTenantScope(registration)
            .BeginLifetimeScope($"CacheIdentity:{registration.PrimaryDomainName}:{Guid.NewGuid()}");

        var tenantContext = scope.Resolve<TenantContext>();
        var tc = CreateTenantContext(registration.PrimaryDomainName);
        tenantContext.Update(tc);

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
        var httpClient = _httpClientFactory.CreateClient(domain);

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

    //

    private Task DeletePayloads(IdentityRegistration identity)
    {
        var id = identity.Id.ToString();

        // Sanity
        if (string.IsNullOrEmpty(id))
        {
            throw new OdinSystemException("I just stopped you in wiping the wrong stuff (missing id)");
        }

        return Task.Run(async () =>
        {
            if (_config.S3PayloadStorage.Enabled)
            {
                _logger.LogInformation("Deleting S3 payload data for {identity.PrimaryDomainName}",
                    identity.PrimaryDomainName);
                var s3 = _serviceProvider.Resolve<IS3PayloadStorage>();
                await s3.DeleteDirectoryAsync(id + "/");
            }
            else
            {
                // Sanity
                if (string.IsNullOrEmpty(PayloadRoot))
                {
                    throw new OdinSystemException("I just stopped you in wiping the wrong stuff (missing PayloadRoot)");
                }

                var identityPayloadDir = Path.Combine(PayloadRoot, id);
                if (Directory.Exists(identityPayloadDir))
                {
                    _logger.LogInformation("Deleting payload dir {dir} on {identity.PrimaryDomainName}",
                        identityPayloadDir, identity.PrimaryDomainName);

                    try
                    {
                        Directory.Delete(identityPayloadDir, true);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error deleting payload in '{path}': {error}", identityPayloadDir, e.Message);
                    }
                }
            }
        });
    }

    //

    private async Task CacheCertificateAsync(IdentityRegistration registration)
    {
        var certificate = await _certificateService.GetCertificateAsync(registration.PrimaryDomainName);
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
            var scope = _serviceProvider.GetTenantScope(registration.PrimaryDomainName);
            var multiplexer = scope.Resolve<IConnectionMultiplexer>();
            var odinContextCache = scope.Resolve<OdinContextCache>();
            await odinContextCache.InitializePubSub(multiplexer);
        }
    }

    //

    private async Task StartBackgroundServices(IdentityRegistration registration)
    {
        var scope = _serviceProvider.GetTenantScope(registration.PrimaryDomainName);
        await scope.StartTenantBackgroundServices();
    }

    //

    private async Task StopBackgroundServices(IdentityRegistration registration)
    {
        var scope = _serviceProvider.GetTenantScope(registration.PrimaryDomainName);
        var backgroundServiceManager = scope.Resolve<IBackgroundServiceManager>();
        await backgroundServiceManager.ShutdownAsync();
    }

    //

    private ILifetimeScope GetOrCreateMultiTenantScope(IdentityRegistration registration)
    {
        var scope = _serviceProvider.GetOrAddTenantScope(
            registration.PrimaryDomainName,
            cb => _tenantContainerBuilder(cb, registration, _config));

        return scope;
    }

    //

    private void RemoveMultiTenantScope(string domain)
    {
        _serviceProvider.RemoveTenantScope(domain);
    }

    //

}