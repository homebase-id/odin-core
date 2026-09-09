using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Json;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Storage.PubSub;
using Odin.Core.Time;
using Odin.Core.Trie;
using Odin.Core.Util;
using Odin.Services.Background;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Drives.FileSystem.Base;
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
    private const string RegistryVersionKey = "registry-version";
    private readonly Guid _nodeId = Guid.NewGuid();
    private long _localVersion;
    // Guards every mutation of _trie/_cache and the registration objects they hold: writers,
    // the initial load and reconcile. Without it a reconcile could copy a not-yet-committed row
    // back over a field a writer set in memory a moment earlier.
    private readonly SemaphoreSlim _registryLock = new(1, 1);
    private IPubSubSubscription _registryChangeSubscription;
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

        // Optional prefixes resolve only while their feature is on - also load-bearing for
        // TLS: SNI certificate selection goes through this lookup (Program.cs)
        if (_config.Email.TenantMail.Enabled && DnsConfigurationSet.OptionalPrefixes.Contains(prefix))
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
        var isPreconfigured = _config.Development.PreconfiguredDomains.Any(d => d.Equals(idReg.PrimaryDomainName,
            StringComparison.InvariantCultureIgnoreCase));

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
            idReg.Email,
            idReg.EnablePublicWebPresence);

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
            Id = request.Id ?? Guid.NewGuid(),
            Email = request.Email,
            PlanId = request.PlanId,
            PrimaryDomainName = request.OdinId,
            IsCertificateManaged = request.IsCertificateManaged,
            EnablePublicWebPresence = request.EnablePublicWebPresence,
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

        long version;
        await _registryLock.WaitAsync();
        try
        {
            version = await SaveRegistrationInternal(registration);
        }
        finally
        {
            _registryLock.Release();
        }

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

        // Announce last: a node that hears this re-reads the row and brings the tenant up itself,
        // so the tenant must be complete here first.
        await AnnounceAsync(version, registration.PrimaryDomainName);

        return registration.FirstRunToken.GetValueOrDefault();
    }

    public async Task DeleteRegistration(string domain)
    {
        var registration = await GetAsync(domain);

        if (null != registration)
        {
            long version;
            await _registryLock.WaitAsync();
            try
            {
                version = await DeleteRegistrationLocked(registration);
            }
            finally
            {
                _registryLock.Release();
            }

            await AnnounceAsync(version, registration.PrimaryDomainName);

            // Storage last, outside the lock: rows are gone and no node routes to the tenant, so
            // nothing can reach this, and a large S3 wipe must not stall every other registry write.
            var tenantRoot = Path.Combine(RegistrationRoot, registration.Id.ToString());
            if (Directory.Exists(tenantRoot))
            {
                Directory.Delete(tenantRoot, true);
            }

            await DeletePayloads(registration);
        }
    }

    // Rows first, then forget: the order every other node observes. Forgetting first would, if the
    // commit then failed, leave this node without a tenant that is still in the database, with no
    // announcement to repair it, and let a retried delete find nothing and report success.
    private async Task<long> DeleteRegistrationLocked(IdentityRegistration registration)
    {
        long version;
        await using (var scope = _serviceProvider.BeginLifetimeScope($"DeleteRegistration:{registration.PrimaryDomainName}"))
        {
            var systemDatabase = scope.Resolve<SystemDatabase>();
            version = await CommitRegistryChangeLockedAsync(systemDatabase, registration.Id, async () =>
            {
                await systemDatabase.Registrations.DeleteAsync(registration.Id);
                await systemDatabase.Certificates.DeleteAsync(new OdinId(registration.PrimaryDomainName));
            });
        }

        // ForgetLocallyAsync is the same step a remote node performs on this delete, which keeps
        // the local and remote paths from drifting apart.
        await ForgetLocallyAsync(registration);
        return version;
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
        if (_config.S3Payload.Enabled)
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
        long version;
        string domain;
        await _registryLock.WaitAsync();
        try
        {
            var registration = GetByFirstRunToken(firstRunToken);
            domain = registration.PrimaryDomainName;
            registration.FirstRunToken = null;
            version = await this.SaveRegistrationInternal(registration);
        }
        finally
        {
            _registryLock.Release();
        }

        await AnnounceAsync(version, domain);
    }

    public Task AssetValidFirstRunToken(Guid firstRunToken, IOdinContext odinContext)
    {
        var registration = GetByFirstRunToken(firstRunToken);
        if ((OdinId)registration.PrimaryDomainName != odinContext.Tenant)
        {
            throw new OdinSecurityException("Invalid first run token");
        }
        
        return Task.CompletedTask;
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

    private async Task<long> SaveRegistrationInternal(IdentityRegistration registration)
    {
        await using var scope = GetOrCreateMultiTenantScope(registration)
            .BeginLifetimeScope($"SaveRegistration:{registration.PrimaryDomainName}");

        var systemDatabase = scope.Resolve<SystemDatabase>();
        var version = await CommitRegistryChangeLockedAsync(systemDatabase, registration.Id, () =>
            systemDatabase.Registrations.UpsertAsync(new RegistrationsRecord
            {
                identityId = registration.Id,
                primaryDomainName = registration.PrimaryDomainName.ToLower(),
                email = registration.Email?.ToLower(),
                firstRunToken = registration.FirstRunToken?.ToString(),
                disabled = registration.Disabled,
                markedForDeletionDate = registration.MarkedForDeletionDate,
                planId = registration.PlanId ?? "free",
                enablePublicWebPresence = registration.EnablePublicWebPresence
            }));

        _logger.LogInformation("Wrote registration record for [{registrationId}] at registry version {version}",
            registration.Id, version);
        await CacheIdentityAsync(registration);
        return version;
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
        long? version = null;
        await _registryLock.WaitAsync();
        try
        {
            var reg = _trie.LookupExactName(domain);
            if (reg != null)
            {
                result = reg.Disabled;
                if (reg.Disabled != disabled)
                {
                    reg.Disabled = disabled;
                    version = await SaveRegistrationInternal(reg);
                }
            }
        }
        finally
        {
            _registryLock.Release();
        }

        await AnnounceAsync(version, domain);
        return result;
    }

    public async Task<bool?> SetPublicWebPresenceAsync(string domain, bool enabled)
    {
        bool? result = null;
        long? version = null;
        await _registryLock.WaitAsync();
        try
        {
            var reg = _trie.LookupExactName(domain);
            if (reg != null)
            {
                result = reg.EnablePublicWebPresence;
                if (reg.EnablePublicWebPresence != enabled)
                {
                    reg.EnablePublicWebPresence = enabled;
                    version = await SaveRegistrationInternal(reg);
                }
            }
        }
        finally
        {
            _registryLock.Release();
        }

        await AnnounceAsync(version, domain);
        return result;
    }

    public async Task<UnixTimeUtc> MarkForDeletionAsync(string domain)
    {
        UnixTimeUtc markedDate;
        long version;
        await _registryLock.WaitAsync();
        try
        {
            var reg = _trie.LookupExactName(domain);
            if (reg == null)
            {
                throw new OdinClientException("Invalid domain");
            }

            markedDate = UnixTimeUtc.Now();
            reg.MarkedForDeletionDate = markedDate;
            version = await SaveRegistrationInternal(reg);
        }
        finally
        {
            _registryLock.Release();
        }

        await AnnounceAsync(version, domain);
        return markedDate.AddDays(_config.Registry.DaysUntilAccountDeletion);
    }

    public async Task UnmarkForDeletionAsync(string domain)
    {
        long version;
        await _registryLock.WaitAsync();
        try
        {
            var reg = _trie.LookupExactName(domain);
            if (reg == null)
            {
                throw new OdinClientException("Invalid domain");
            }

            reg.MarkedForDeletionDate = null;
            version = await SaveRegistrationInternal(reg);
        }
        finally
        {
            _registryLock.Release();
        }

        await AnnounceAsync(version, domain);
    }

    public async Task LoadRegistrations()
    {
        Directory.CreateDirectory(RegistrationRoot);
        if (!_config.S3Payload.Enabled)
        {
            Directory.CreateDirectory(PayloadRoot);
        }

        // Under the registry lock so an announcement arriving mid-load queues behind it instead
        // of reconciling against a half-built registry; it then finds the version above ours and
        // reconciles after, which is the "dropped or reconciled after" guarantee.
        await _registryLock.WaitAsync();
        try
        {
            await using var systemScope = _serviceProvider.BeginLifetimeScope();
            var systemDatabase = systemScope.Resolve<SystemDatabase>();

            // Version before rows: a change landing between the two reads then has a version above
            // ours and its announcement triggers a reconcile, whereas rows-then-version could record a
            // version newer than the rows we hold and silently skip that change.
            var version = await ReadRegistryVersionAsync(systemDatabase);
            var registrations = await systemDatabase.Registrations.GetAllAsync();
            var allLoaded = true;
            foreach (var registrationRecord in registrations)
            {
                // A reconcile that queued behind an early announcement may already have loaded it.
                if (_cache.ContainsKey(registrationRecord.identityId))
                {
                    continue;
                }

                allLoaded &= await LoadRegistrationRecordAsync(registrationRecord);
            }

            // Only claim the version if every tenant actually came up; otherwise the next
            // announcement or reconnect re-check retries the ones that failed.
            if (allLoaded)
            {
                RaiseLocalVersion(version);
            }

            _logger.LogInformation("Registry loaded at version {version} (all loaded: {allLoaded})", version, allLoaded);
        }
        finally
        {
            _registryLock.Release();
        }
    }

    /// <summary>
    /// Brings one registration fully into this node: directories, database migration, tenant scope,
    /// caches and background services. Used both by the initial load and when another node tells us
    /// about a registration this node has never seen.
    /// </summary>
    private async Task<bool> LoadRegistrationRecordAsync(RegistrationsRecord registrationRecord)
    {
        try
        {
            var identityId = registrationRecord.identityId.ToString();
            var registrationPath = Path.Combine(RegistrationRoot, identityId);

            // Scalability: ensure the registration directory exists on all hosts
            Directory.CreateDirectory(registrationPath);

            var registration = new IdentityRegistration { Id = registrationRecord.identityId };
            CopyFields(registration, registrationRecord);

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
            return false;
        }

        return true;
    }

    #region cross-node registry coherence

    /// <summary>
    /// Every registry write goes through here so the ordering that cross-node coherence depends on
    /// cannot drift: the version is bumped inside the write's transaction, and announced only after
    /// it committed.
    /// </summary>
    /// <summary>
    /// Runs a registry write under the caller-held registry lock. The version bump is atomic with
    /// the write and reports the value it advanced from; if that is above this node's local
    /// version, another node's change landed here unapplied, so the rows are reconciled first,
    /// under the lock we already hold, before the new version is claimed and announced.
    /// </summary>
    private async Task<long> CommitRegistryChangeLockedAsync(SystemDatabase systemDatabase, Guid writingIdentityId, Func<Task> mutate)
    {
        long previous;
        long version;
        await using (var tx = await systemDatabase.BeginStackedTransactionAsync())
        {
            await mutate();
            (previous, version) = await systemDatabase.Settings.BumpMonotonicAsync(RegistryVersionKey);
            tx.Commit();
        }

        if (previous > Volatile.Read(ref _localVersion))
        {
            // The identity this commit writes is excluded: its caller is still bringing it up, and
            // loading it here would start its background services a second time.
            _logger.LogInformation("Registry was behind at write time (database {previous}, local {local}); reconciling before announcing",
                previous, Volatile.Read(ref _localVersion));
            await ReconcileLockedAsync(previous, excludeIdentityId: writingIdentityId);
        }

        RaiseLocalVersion(version);
        return version;
    }

    /// <summary>
    /// Starts listening for registry version announcements from other nodes, and re-checks the
    /// version whenever the Redis connection is restored. Call before <see cref="LoadRegistrations"/>:
    /// an announcement arriving mid-load is either at or below the version load reads, and dropped,
    /// or above it, and reconciled after, so there is no startup window to close with a timer.
    /// </summary>
    public async Task SubscribeToRegistryChangesAsync()
    {
        if (_registryChangeSubscription != null)
        {
            return;
        }

        var pubSub = _serviceProvider.Resolve<ISystemPubSub>();
        _registryChangeSubscription = await pubSub.SubscribeAsync(RegistryChangeMessage.Channel, OnRegistryVersionAnnouncedAsync);

        if (_config.Redis.Enabled)
        {
            // Pub/sub has no replay: anything announced while this connection was down is gone.
            // The version row says whether we missed something, and this is the only moment we
            // could have, so re-check it here rather than on a timer.
            _serviceProvider.Resolve<IConnectionMultiplexer>().ConnectionRestored += OnRedisConnectionRestored;
        }

        _logger.LogInformation("Registry subscribed to {channel}", RegistryChangeMessage.Channel);
    }

    private void OnRedisConnectionRestored(object sender, ConnectionFailedEventArgs e)
    {
        // Fires once per physical connection; only the subscription connection carries announcements.
        if (e.ConnectionType != ConnectionType.Subscription)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ReconcileWithDatabaseAsync(null, "redis connection restored");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registry re-check after redis reconnect failed: {error}", ex.Message);
            }
        });
    }

    private async Task OnRegistryVersionAnnouncedAsync(JsonEnvelope envelope)
    {
        try
        {
            if (envelope.DeserializeMessage() is not RegistryChangeMessage message || message.OriginNodeId == _nodeId)
            {
                return;
            }

            if (message.Version <= Volatile.Read(ref _localVersion))
            {
                return;
            }

            await ReconcileWithDatabaseAsync(message.Version, $"version {message.Version} announced");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error handling registry version announcement: {error}", e.Message);
        }
    }

    private async Task PublishRegistryVersionAsync(long version, string primaryDomain)
    {
        var pubSub = _serviceProvider.Resolve<ISystemPubSub>();
        var envelope = JsonEnvelope.Create(new RegistryChangeMessage { Version = version, OriginNodeId = _nodeId });

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await pubSub.PublishAsync(RegistryChangeMessage.Channel, envelope);
                return;
            }
            catch (Exception e) when (attempt < 3)
            {
                _logger.LogWarning(e, "Publishing registry version {version} failed (attempt {attempt}): {error}",
                    version, attempt, e.Message);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
            catch (Exception e)
            {
                // The change is committed and applied here, but no other node will hear about it
                // until the next registry change anywhere, a redis reconnect, or a restart. That is
                // the one gap this design accepts, and it has to be findable in the logs.
                _logger.LogError(e,
                    "Could not publish registry version {version} after {domain} changed; other nodes stay stale " +
                    "until the next registry change, a redis reconnect, or a restart: {error}",
                    version, primaryDomain, e.Message);
                return;
            }
        }
    }


    /// <summary>
    /// Brings the in-memory registry up to the database, which is the source of truth. Reads the
    /// version before the rows for the same reason as <see cref="LoadRegistrations"/>. Single-flight:
    /// concurrent announcements and reconnect events collapse into one pass.
    /// </summary>
    /// <summary>
    /// Brings the in-memory registry up to the database if the database is known (or suspected)
    /// to be past <paramref name="floor"/>. Announcements pass the version they carry; a redis
    /// reconnect passes nothing and lets the database say. The floor is checked again once the
    /// lock is held, before any scope or query, so a burst of announcements collapses into one
    /// pass and the rest return without touching the database.
    /// </summary>
    private async Task ReconcileWithDatabaseAsync(long? floor, string reason)
    {
        await _registryLock.WaitAsync();
        try
        {
            if (floor.HasValue && floor.Value <= Volatile.Read(ref _localVersion))
            {
                return;
            }

            long version;
            await using (var scope = _serviceProvider.BeginLifetimeScope("RegistryReconcile"))
            {
                version = await ReadRegistryVersionAsync(scope.Resolve<SystemDatabase>());
            }

            if (version <= Volatile.Read(ref _localVersion))
            {
                return;
            }

            _logger.LogInformation("Registry is behind after {reason}: database version {version}, local {local}; reconciling",
                reason, version, Volatile.Read(ref _localVersion));
            await ReconcileLockedAsync(version);
        }
        finally
        {
            _registryLock.Release();
        }
    }

    // Caller holds _registryLock. Reads rows and applies them; raises the local version only if
    // every tenant that needed loading came up, so a failed one is retried on the next trigger.
    private async Task ReconcileLockedAsync(long version, Guid? excludeIdentityId = null)
    {
        List<RegistrationsRecord> records;
        await using (var scope = _serviceProvider.BeginLifetimeScope("RegistryReconcile"))
        {
            records = await scope.Resolve<SystemDatabase>().Registrations.GetAllAsync();
        }

        var added = 0;
        var allLoaded = true;
        foreach (var record in records)
        {
            if (record.identityId == excludeIdentityId)
            {
                continue;
            }

            var known = _cache.GetValueOrDefault(record.identityId);
            if (known == null)
            {
                _logger.LogInformation("Reconciliation is adding {domain}", record.primaryDomainName);
                allLoaded &= await LoadRegistrationRecordAsync(record);
                added++;
            }
            else
            {
                // We only get here because the version moved, and the table is small: applying
                // every row costs less than deciding which one changed.
                await ApplyRecordAsync(known, record);
            }
        }

        var unloaded = 0;
        var liveIds = records.Select(r => r.identityId).ToHashSet();
        foreach (var orphan in _cache.Values.Where(r => !liveIds.Contains(r.Id) && r.Id != excludeIdentityId).ToList())
        {
            _logger.LogInformation("Reconciliation is unloading {domain}, which is no longer registered", orphan.PrimaryDomainName);
            try
            {
                await ForgetLocallyAsync(orphan);
                unloaded++;
            }
            catch (Exception e)
            {
                // Keep sweeping: one tenant's scope refusing to dispose must not leave the others routable.
                _logger.LogError(e, "Could not unload {domain}: {error}", orphan.PrimaryDomainName, e.Message);
                allLoaded = false;
            }
        }

        if (allLoaded)
        {
            RaiseLocalVersion(version);
        }

        _logger.LogInformation("Registry reconciled to version {version}: {count} registrations, {added} added, {unloaded} unloaded, complete: {complete}",
            version, records.Count, added, unloaded, allLoaded);
    }

    private async Task AnnounceAsync(long? version, string primaryDomain)
    {
        if (version.HasValue)
        {
            await PublishRegistryVersionAsync(version.Value, primaryDomain);
        }
    }

    /// <summary>
    /// Drops a registration from this node without touching any storage. This is what a node does
    /// when another node deleted the tenant: that node already removed the rows, the registration
    /// directory and the payloads, and repeating it here would delete a second time.
    /// </summary>
    private async Task ForgetLocallyAsync(IdentityRegistration registration)
    {
        _trie.TryRemoveDomain(registration.PrimaryDomainName);
        await UnloadRegistration(registration);
    }

    private static void CopyFields(IdentityRegistration target, RegistrationsRecord record)
    {
        target.PrimaryDomainName = record.primaryDomainName;
        target.Email = record.email;
        target.FirstRunToken = string.IsNullOrEmpty(record.firstRunToken) ? null : Guid.Parse(record.firstRunToken);
        target.PlanId = record.planId;
        target.Disabled = record.disabled;
        target.EnablePublicWebPresence = record.enablePublicWebPresence;
        target.MarkedForDeletionDate = record.markedForDeletionDate;
        // LastSeen = record.lastSeen // SEB:TODO
    }

    private async Task ApplyRecordAsync(IdentityRegistration known, RegistrationsRecord record)
    {
        if (!string.Equals(known.PrimaryDomainName, record.primaryDomainName, StringComparison.OrdinalIgnoreCase))
        {
            // The tenant scope, its background services and the certificate cache are all keyed by
            // domain, so a rename is a different tenant to everything but the trie: rebuild it.
            _logger.LogWarning("Registration {id} changed domain {old} -> {new}; reloading", known.Id,
                known.PrimaryDomainName, record.primaryDomainName);
            await ForgetLocallyAsync(known);
            await LoadRegistrationRecordAsync(record);
            return;
        }

        // The trie holds this same object, so the field copy is already visible to lookups.
        CopyFields(known, record);

        // TenantContext is a per-scope singleton with its own copy of FirstRunToken, Email and the
        // public-web-presence flag; the local write path refreshes it in CacheIdentityAsync, and a
        // remote change must too or those readers stay stale here until restart.
        var scope = _serviceProvider.LookupTenantScope(known.PrimaryDomainName);
        scope?.Resolve<TenantContext>().Update(CreateTenantContext(known.PrimaryDomainName));
    }

    private static async Task<long> ReadRegistryVersionAsync(SystemDatabase systemDatabase)
    {
        var record = await systemDatabase.Settings.GetAsync(RegistryVersionKey);
        return record?.modified.milliseconds ?? 0;
    }

    private void RaiseLocalVersion(long version)
    {
        long current;
        while ((current = Volatile.Read(ref _localVersion)) < version &&
               Interlocked.CompareExchange(ref _localVersion, version, current) != current)
        {
        }
    }

    #endregion

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

    // Long enough for an ACME order, which is the thing being waited on
    private static readonly TimeSpan InitializeCertificateTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan InitializeCertificateRetryDelay = TimeSpan.FromSeconds(3);

    private async Task InitializeCertificate(string domain)
    {
        var httpClient = _httpClientFactory.CreateClient(domain);
        var uri = $"https://{domain}:{_config.Host.DefaultHttpsPort}/.well-known/acme-challenge/ping";

        //
        // This request exists only to make the host obtain a certificate for the new identity.
        // It used to do so synchronously: the TLS handshake placed the ACME order inline, so a
        // single GET came back once the certificate existed. Issuance now happens out of band
        // (see docs/certificate-issuance-locking.md), so the first attempts are EXPECTED to fail
        // while the order runs - the handshake asks the background issuer and serves nothing
        // until it delivers. Retrying until the handshake succeeds keeps the old guarantee that
        // registration finishes with a usable identity, without putting the order back on the
        // handshake path.
        //
        var deadline = DateTimeOffset.UtcNow + InitializeCertificateTimeout;
        var attempts = 0;

        while (true)
        {
            attempts++;
            try
            {
                var response = await httpClient.GetAsync(uri);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "InitializeCertificate: {domain} is serving HTTPS after {attempts} attempt(s)",
                        domain, attempts);
                    return;
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogDebug("InitializeCertificate: request to {domain} timed out (attempt {attempts})",
                    domain, attempts);
            }
            catch (HttpRequestException e)
            {
                // Expected while the certificate is still being ordered. Also happens when the
                // DNS server this host uses does not yet know the domain.
                _logger.LogDebug("InitializeCertificate: {domain} not ready yet (attempt {attempts}): {error}",
                    domain, attempts, e.Message);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                _logger.LogWarning(
                    "InitializeCertificate: {domain} still has no certificate after {timeout}s and {attempts} " +
                    "attempt(s). The background issuer will keep trying; the identity is not reachable over " +
                    "HTTPS until it succeeds.",
                    domain, (int)InitializeCertificateTimeout.TotalSeconds, attempts);
                return;
            }

            await Task.Delay(InitializeCertificateRetryDelay);
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
            if (_config.S3Payload.Enabled)
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