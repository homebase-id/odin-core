using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
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
    IMultiTenantContainer multiTenantContainer)
    : ITenantAdmin
{
    private readonly ILogger<TenantAdmin> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

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

    private async Task<TenantModel> Map(IdentityRegistration identityRegistration, bool includePayload)
    {
        var result = new TenantModel
        {
            Domain = identityRegistration.PrimaryDomainName,
            Id = identityRegistration.Id.ToString(),
            Enabled = !identityRegistration.Disabled
        };

        if (identityRegistry is FileSystemIdentityRegistry fsir)
        {
            result.RegistrationPath = Path.Combine(fsir.RegistrationRoot, result.Id);
            result.RegistrationSize = await GetDirectoryByteSizeAsync(result.RegistrationPath);

            if (includePayload)
            {
                var tenantScope = multiTenantContainer.GetTenantScope(identityRegistration.PrimaryDomainName);
                var driveMainIndex = tenantScope.Resolve<TableDriveMainIndexCached>();
                var sizeAllDrives = await driveMainIndex.GetTotalSizeAllDrivesAsync(TimeSpan.FromMinutes(10));

                if (config.S3PayloadStorage.Enabled)
                {
                    result.PayloadPath = Path.Combine(
                        config.S3PayloadStorage.ServiceUrl, config.S3PayloadStorage.BucketName, result.Id);
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