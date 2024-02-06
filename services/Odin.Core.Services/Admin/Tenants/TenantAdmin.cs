using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Services.Admin.Tenants.Jobs;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Quartz;
using Odin.Core.Services.Registry;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants;
#nullable enable

public class TenantAdmin : ITenantAdmin
{
    private readonly ILogger<TenantAdmin> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly OdinConfiguration _config;
    private readonly IJobSchedulerFactory _jobSchedulerFactory;
    private readonly ISchedulerFactory _schedulerFactory; // TODO:SEB Remove
    private readonly IExclusiveJobManager _exclusiveJobManager; // TODO:SEB Remove
    private readonly IIdentityRegistry _identityRegistry;

    public TenantAdmin(
        ILogger<TenantAdmin> logger,
        ILoggerFactory loggerFactory,
        OdinConfiguration config,
        IJobSchedulerFactory jobSchedulerFactory,
        ISchedulerFactory schedulerFactory,
        IExclusiveJobManager exclusiveJobManager,
        IIdentityRegistry identityRegistry)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _config = config;
        _jobSchedulerFactory = jobSchedulerFactory;
        _schedulerFactory = schedulerFactory;
        _exclusiveJobManager = exclusiveJobManager;
        _identityRegistry = identityRegistry;
    }

    //

    public async Task<List<TenantModel>> GetTenants(bool includePayload)
    {
        var result = new List<TenantModel>();
        var identities = await _identityRegistry.GetList();
        foreach (var identityRegistration in identities.Results)
        {
            result.Add(await Map(identityRegistration, includePayload));
        }
        return result;
    }

    //

    public async Task<TenantModel?> GetTenant(string domain, bool includePayload)
    {
        var identity = await _identityRegistry.Get(domain);
        return identity == null ? null : await Map(identity, includePayload);
    }

    //

    public async Task<string> EnqueueDeleteTenant(string domain)
    {
        if (!await _identityRegistry.IsIdentityRegistered(domain))
        {
            throw new OdinClientException($"{domain} not found");
        }

        var jobSchedule = new DeleteTenantScheduler(_loggerFactory.CreateLogger<DeleteTenantScheduler>(), domain);
        var jobKey = await _jobSchedulerFactory.Schedule<DeleteTenantJob>(jobSchedule);

        return jobKey.ToString();
    }

    //

    public async Task<string> EnqueueExportTenant(string domain)
    {
        if (!await _identityRegistry.IsIdentityRegistered(domain))
        {
            throw new OdinClientException($"{domain} not found");
        }

        var jobKey = new JobKey(domain, ExportTenantJob.JobGroup);
        if (_exclusiveJobManager.Exists(jobKey))
        {
            return jobKey.ToString();
        }

        var scheduler = await _schedulerFactory.GetScheduler();
        var job = JobBuilder.Create<ExportTenantJob>()
            .WithIdentity(jobKey)
            .UsingJobData("domain", domain)
            .Build();
        var trigger = TriggerBuilder.Create()
            .StartNow()
            .Build();
        await scheduler.ScheduleJob(job, trigger);

        return jobKey.ToString();
    }

    //

    public async Task<bool> TenantExists(string domain)
    {
        return await _identityRegistry.IsIdentityRegistered(domain);
    }

    //

    public async Task EnableTenant(string domain)
    {
        await _identityRegistry.ToggleDisabled(domain, false);
    }

    //

    public async Task DisableTenant(string domain)
    {
        await _identityRegistry.ToggleDisabled(domain, true);
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

        if (_identityRegistry is FileSystemIdentityRegistry fsir)
        {
            result.RegistrationPath = Path.Combine(fsir.RegistrationRoot, result.Id);
            result.RegistrationSize = await GetDirectoryByteSizeAsync(result.RegistrationPath);

            if (includePayload)
            {
                var shards = await GetPayloadShards(fsir.ShardablePayloadRoot, result.Id);
                result.PayloadShards = shards;
                result.PayloadSize = shards.Sum(p => p.Size);
            }
        }

        return result;
    }

    //

    private static async Task<List<TenantModel.PayloadShard>> GetPayloadShards(string payloadRootPath, string tenantId)
    {
        var result = new List<TenantModel.PayloadShard>();

        var shards = Directory.GetDirectories(payloadRootPath);
        foreach (var shard in shards)
        {
            var tenantPayloadPath = Path.Combine(shard, tenantId);
            if (Directory.Exists(tenantPayloadPath))
            {
                result.Add(new TenantModel.PayloadShard()
                {
                    Name = Path.GetRelativePath(payloadRootPath, shard).Trim('/', '\\'),
                    Path = tenantPayloadPath,
                    Size = await GetDirectoryByteSizeAsync(tenantPayloadPath)
                });
            }
        }
        result.Sort((a, b) => string.Compare(
            a.Path,
            b.Path,
            StringComparison.CurrentCulture));

        return result;
    }

    //

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