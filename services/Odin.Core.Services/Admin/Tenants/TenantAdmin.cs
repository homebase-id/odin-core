using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Admin.Tenants.Jobs;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Registry;
using Quartz;
using Quartz.Impl.Matchers;

namespace Odin.Core.Services.Admin.Tenants;
#nullable enable

public class TenantAdmin : ITenantAdmin
{
    private readonly ILogger<TenantAdmin> _logger;
    private readonly OdinConfiguration _config;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IIdentityRegistry _identityRegistry;

    public TenantAdmin(
        ILogger<TenantAdmin> logger,
        OdinConfiguration config,
        ISchedulerFactory schedulerFactory,
        IIdentityRegistry identityRegistry)
    {
        _logger = logger;
        _config = config;
        _schedulerFactory = schedulerFactory;
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

    public async Task<AdminJobStatus> DeleteTenant(string domain)
    {
        SEB:HER!
        var jobGroup = $"delete-tenant-{domain}";
        var jobId = Guid.NewGuid().ToString();
        var jobKey = new JobKey(jobId, jobGroup);

        var scheduler = await _schedulerFactory.GetScheduler();

        var existingJobs = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(jobGroup));
        if (existingJobs.Count > 0)
        {
            var triggers = await scheduler.GetTriggersOfJob(existingJobs.First());
            if (triggers.Count == 0)
            {
                return AdminJobStatus.Unknown;
            }

            var triggerState = await scheduler.GetTriggerState(triggers.First().Key);
            switch (triggerState)
            {
                case TriggerState.Paused:
                    return AdminJobStatus.Paused;
                case TriggerState.Complete:
                    return AdminJobStatus.Completed;
                case TriggerState.Blocked:
                    return AdminJobStatus.Blocked;
                case TriggerState.Error:
                    return AdminJobStatus.Error;
                default:
                    return AdminJobStatus.Scheduled;
            }
        }

        var job = JobBuilder.Create<DeleteTenantJob>()
            .WithIdentity(jobKey)
            .UsingJobData("domain", domain)
            .Build();
        var trigger = TriggerBuilder.Create()
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        return AdminJobStatus.Scheduled;
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