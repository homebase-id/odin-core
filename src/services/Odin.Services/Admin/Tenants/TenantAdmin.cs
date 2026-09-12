using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Core.Storage.Database.System;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.LastSeen;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Admin.Tenants;
#nullable enable

public class TenantAdmin(
    ILogger<TenantAdmin> logger,
    ILoggerFactory loggerFactory,
    OdinConfiguration config,
    IJobManager jobManager,
    IIdentityRegistry identityRegistry,
    IMultiTenantContainer multiTenantContainer,
    ILastSeenService lastSeenService,
    IdentityStorageCensus identityStorageCensus)
    : ITenantAdmin
{
    private readonly ILogger<TenantAdmin> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    /// <summary>
    /// The table caches default to a 2 hour TTL, which is far too stale for a report whose whole
    /// job is to notice that data disappeared. The consumer reads this once a day, so a short TTL
    /// costs nothing and only coalesces bursts.
    /// </summary>
    private static readonly TimeSpan MetricsCacheTtl = TimeSpan.FromMinutes(1);

    //

    public async Task<List<TenantModel>> GetTenants(bool includePayload)
    {
        var result = new List<TenantModel>();
        var identities = await identityRegistry.GetTenants();
        foreach (var identity in identities)
        {
            result.Add(await Map(identity, includePayload));
        }
        return result;
    }

    //

    public async Task<TenantModel?> GetTenantAsync(string domain, bool includePayload)
    {
        var identity = await identityRegistry.GetAsync(domain);
        return identity == null ? null : await Map(identity, includePayload);
    }

    //

    public async Task<string> EnqueueDeleteTenant(string domain)
    {
        if (!await identityRegistry.IsIdentityRegistered(domain))
        {
            throw new OdinClientException($"{domain} not found");
        }

        var job = jobManager.NewJob<DeleteTenantJob>();
        job.Data.Domain = domain;

        var jobId = await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            MaxAttempts = 3,
            RetryDelay = TimeSpan.FromSeconds(5),
            OnFailureDeleteAfter = TimeSpan.FromDays(2),
            OnSuccessDeleteAfter = TimeSpan.FromDays(2),
            Priority = JobSchedule.LowPriority,
        });

        return jobId.ToString();
    }

    //

    public async Task<string> EnqueueExportTenant(string domain)
    {
        if (!await identityRegistry.IsIdentityRegistered(domain))
        {
            throw new OdinClientException($"{domain} not found");
        }

        var job = jobManager.NewJob<ExportTenantJob>();
        job.Data.Domain = domain;

        var jobId = await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            MaxAttempts = 3,
            RetryDelay = TimeSpan.FromSeconds(5),
            OnFailureDeleteAfter = TimeSpan.FromDays(2),
            OnSuccessDeleteAfter = TimeSpan.FromDays(2),
            Priority = JobSchedule.LowPriority,
        });

        return jobId.ToString();
    }

    //

    public async Task<TenantMetricsResponse> GetTenantMetricsAsync()
    {
        var registrations = await identityRegistry.GetTenants();
        var knownIds = new HashSet<Guid>(registrations.Select(r => r.Id));
        var fsir = identityRegistry as FileSystemIdentityRegistry;

        var response = new TenantMetricsResponse
        {
            GeneratedAt = UnixTimeUtc.Now(),
            DatabaseType = config.Database.Type.ToString().ToLowerInvariant(),
            IndexOrphanScanSupported = identityStorageCensus.IsSupported,
        };

        // One pass over the whole database where that is possible (Postgres), which both covers
        // every registered tenant without a query each AND surfaces identities the registry cannot
        // name. Empty on SQLite, where each tenant is a separate database file.
        var census = await identityStorageCensus.GetAllAsync();
        var censusById = census.ToDictionary(r => r.IdentityId);

        foreach (var registration in registrations)
        {
            // One unreadable tenant must not blank the whole fleet report, but it must not be
            // reported as zeros either - that is exactly the shape of the data loss we are looking
            // for. Record the failure on the row and leave its figures null.
            var row = new TenantMetricsModel
            {
                Id = registration.Id.ToString(),
                Domain = registration.PrimaryDomainName,
                Registered = true,
            };

            try
            {
                row = await MapMetricsAsync(registration, fsir, censusById);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not read metrics for tenant {domain}",
                    registration.PrimaryDomainName);
                row.MetricsError = e.Message;
            }

            response.Tenants.Add(row);
        }

        // Identities that still own rows but lost their registration.
        var censusOrphanIds = new HashSet<Guid>();
        foreach (var storage in census)
        {
            if (knownIds.Contains(storage.IdentityId))
            {
                continue;
            }

            censusOrphanIds.Add(storage.IdentityId);
            response.Tenants.Add(new TenantMetricsModel
            {
                Id = storage.IdentityId.ToString(),
                Domain = null,
                Registered = false,
                OrphanSource = OrphanSource.Index,
                Files = storage.Files,
                TotalBytes = storage.TotalBytes,
                ActiveBytes = storage.ActiveBytes,
                DriveCount = storage.DriveCount,
                RegistrationPath = fsir == null ? null : GetRegistrationPath(fsir, storage.IdentityId),
                RegistrationSize = fsir == null ? null : GetRegistrationSizeOrNull(fsir, storage.IdentityId),
                PayloadPath = fsir == null ? null : GetPayloadPath(fsir, storage.IdentityId),
            });
        }

        // Orphans found on disk: a registration directory whose id has no registration. Works on
        // both backends. We deliberately do not open any stray database file found here.
        if (fsir != null)
        {
            foreach (var identityId in EnumerateOrphanedRegistrationDirectories(fsir, knownIds))
            {
                if (censusOrphanIds.Contains(identityId))
                {
                    // Already reported, with real counts.
                    continue;
                }

                response.Tenants.Add(new TenantMetricsModel
                {
                    Id = identityId.ToString(),
                    Domain = null,
                    Registered = false,
                    OrphanSource = OrphanSource.Directory,
                    RegistrationPath = GetRegistrationPath(fsir, identityId),
                    RegistrationSize = GetRegistrationSizeOrNull(fsir, identityId),
                    PayloadPath = GetPayloadPath(fsir, identityId),
                });
            }
        }

        return response;
    }

    //

    private async Task<TenantMetricsModel> MapMetricsAsync(
        IdentityRegistration registration,
        FileSystemIdentityRegistry? fsir,
        IReadOnlyDictionary<Guid, IdentityStorageRow> censusById)
    {
        var result = new TenantMetricsModel
        {
            Id = registration.Id.ToString(),
            Domain = registration.PrimaryDomainName,
            Registered = true,
            Enabled = !registration.Disabled,
            EnablePublicWebPresence = registration.EnablePublicWebPresence,
            Email = registration.Email,
            PlanId = registration.PlanId,
            CreatedAt = registration.Created,
            MarkedForDeletionDate = registration.MarkedForDeletionDate,
            LastActivity = await lastSeenService.GetLastSeenAsync(registration.PrimaryDomainName),
        };

        if (fsir != null)
        {
            result.RegistrationPath = GetRegistrationPath(fsir, registration.Id);
            result.RegistrationSize = GetRegistrationSizeOrNull(fsir, registration.Id);
            result.PayloadPath = GetPayloadPath(fsir, registration.Id);
        }

        if (censusById.TryGetValue(registration.Id, out var storage))
        {
            result.Files = storage.Files;
            result.TotalBytes = storage.TotalBytes;
            result.ActiveBytes = storage.ActiveBytes;
            result.DriveCount = storage.DriveCount;
            return result;
        }

        if (identityStorageCensus.IsSupported)
        {
            // The census covered the whole database and this identity was not in it, so it owns
            // nothing. A real zero, not a missing measurement.
            result.Files = 0;
            result.TotalBytes = 0;
            result.ActiveBytes = 0;
            result.DriveCount = 0;
            return result;
        }

        // SQLite: each tenant is its own database file, so there is no census and the figures have
        // to be read one tenant at a time. A child scope per tenant, because ScopedConnectionFactory
        // is per-lifetime-scope and is not safe for concurrent use, and the tenant scope itself is
        // shared with that tenant's background services. Keep this sequential for the same reason.
        await using var scope = multiTenantContainer
            .GetTenantScope(registration.PrimaryDomainName)
            .BeginLifetimeScope($"TenantMetrics:{registration.Id}");

        var stats = await scope.Resolve<TableDriveMainIndexCached>()
            .GetIdentityStorageStatsAsync(MetricsCacheTtl);
        result.Files = stats.Files;
        result.TotalBytes = stats.TotalBytes;
        result.ActiveBytes = stats.ActiveBytes;
        result.DriveCount = await scope.Resolve<TableDrivesCached>().GetCountAsync(MetricsCacheTtl);

        return result;
    }

    //

    private static string GetRegistrationPath(FileSystemIdentityRegistry fsir, Guid identityId)
    {
        return Path.Combine(fsir.RegistrationRoot, identityId.ToString());
    }

    //

    private string GetPayloadPath(FileSystemIdentityRegistry fsir, Guid identityId)
    {
        return config.S3Payload.Enabled
            ? Path.Combine(config.S3Storage.ServiceUrl, config.S3Payload.BucketName, identityId.ToString())
            : Path.Combine(fsir.PayloadRoot, identityId.ToString());
    }

    //

    private static long? GetRegistrationSizeOrNull(FileSystemIdentityRegistry fsir, Guid identityId)
    {
        var path = Path.Combine(fsir.RegistrationRoot, identityId.ToString());
        if (!Directory.Exists(path))
        {
            return null;
        }

        try
        {
            return GetDirectoryByteSize(path);
        }
        catch (Exception)
        {
            // Unreadable is not the same as empty; say so by returning null.
            return null;
        }
    }

    //

    private static IEnumerable<Guid> EnumerateOrphanedRegistrationDirectories(
        FileSystemIdentityRegistry fsir,
        HashSet<Guid> knownIds)
    {
        if (!Directory.Exists(fsir.RegistrationRoot))
        {
            yield break;
        }

        foreach (var directory in Directory.GetDirectories(fsir.RegistrationRoot))
        {
            var name = Path.GetFileName(directory);
            if (Guid.TryParse(name, out var identityId) && !knownIds.Contains(identityId))
            {
                yield return identityId;
            }
        }
    }

    //

    public async Task<bool> TenantExists(string domain)
    {
        return await identityRegistry.IsIdentityRegistered(domain);
    }

    //

    public async Task EnableTenant(string domain)
    {
        await identityRegistry.ToggleDisabled(domain, false);
    }

    //

    public async Task DisableTenant(string domain)
    {
        await identityRegistry.ToggleDisabled(domain, true);
    }

    //

    public async Task EnablePublicWebPresence(string domain)
    {
        await identityRegistry.SetPublicWebPresenceAsync(domain, true);
    }

    //

    public async Task DisablePublicWebPresence(string domain)
    {
        await identityRegistry.SetPublicWebPresenceAsync(domain, false);
    }

    //

    private async Task<TenantModel> Map(IdentityRegistration identityRegistration, bool includePayload)
    {
        var result = new TenantModel
        {
            Domain = identityRegistration.PrimaryDomainName,
            Id = identityRegistration.Id.ToString(),
            Enabled = !identityRegistration.Disabled,
            EnablePublicWebPresence = identityRegistration.EnablePublicWebPresence
        };

        if (identityRegistry is FileSystemIdentityRegistry fsir)
        {
            result.RegistrationPath = Path.Combine(fsir.RegistrationRoot, result.Id);
            result.RegistrationSize = await GetDirectoryByteSizeAsync(result.RegistrationPath);

            if (includePayload)
            {
                var tenantScope = multiTenantContainer.GetTenantScope(identityRegistration.PrimaryDomainName);
                var driveMainIndex = tenantScope.Resolve<TableDriveMainIndexCached>();
                var sizeAllDrives = await driveMainIndex.GetTotalSizeAllDrivesAsync();

                if (config.S3Payload.Enabled)
                {
                    result.PayloadPath = Path.Combine(
                        config.S3Storage.ServiceUrl, config.S3Payload.BucketName, result.Id);
                }
                else
                {
                    result.PayloadPath = Path.Combine(fsir.PayloadRoot, result.Id);
                }

                result.PayloadSize = sizeAllDrives;
            }
        }

        return result;
    }

    //

    // NOTE: this is the equivalent of running bash command:
    // find . -type f -exec du -b {} + | awk '{total += $1} END {print total}'
    private static long GetDirectoryByteSize(string path)
    {
        var result = 0L;

        var files = Directory.GetFiles(path);
        foreach (var file in files)
        {
            try
            {
                var fileInfo = new FileInfo(file);
                result += fileInfo.Length;
            }
            catch
            {
                // Ignore
            }
        }

        var directories = Directory.GetDirectories(path);
        foreach (var directory in directories)
        {
            result += GetDirectoryByteSize(directory);
        }

        return result;
    }

    //

    private static async Task<long> GetDirectoryByteSizeAsync(string path)
    {
        return await Task.Run(() => GetDirectoryByteSize(path));
    }

    //

}