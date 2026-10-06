using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Http;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tenant.Container;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

/// <summary>
/// The target side of a payload move, as a job: scheduled by identity-import, it pulls the identity's
/// payloads from the source a slice at a time and saves its checkpoint between slices. A job because the
/// jobs table already gives one runner per job across a cluster, a persisted checkpoint and rescheduling
/// across restarts. It runs although the identity is paused (it lands paused), and a node that dies
/// mid-slice leaves it to the orphan rescue. See docs/superpowers/specs/2026-08-31-payload-migration-design.md.
/// </summary>
public class PayloadMoveJob(
    ILogger<PayloadMoveJob> logger,
    IIdentityRegistry registry,
    IMultiTenantContainer tenants,
    OdinConfiguration config,
    IDynamicHttpClientFactory httpClientFactory) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("8c4f5d2a-3e71-4b9c-a6f0-2d9e8b1c7a53");
    public override string JobType => JobTypeId.ToString();

    public static readonly TimeSpan SliceBudget = TimeSpan.FromMinutes(5);

    // A slice lasts at most SliceBudget plus one object's timeout; well below this
    public override TimeSpan? RescheduleIfOrphanedAfter => TimeSpan.FromMinutes(30);

    public override bool RunsWhileIdentityStopped => true;

    public PayloadMoveState Data { get; set; } = new();

    public static string JobHashFor(Guid identityId) => $"payload-move:{identityId}";

    public override string? CreateJobHash() => IdentityId is { } identityId ? JobHashFor(identityId) : null;

    /// <summary>
    /// Schedules the identity's payload move, replacing any earlier one (a finished move of the same identity
    /// would otherwise keep the hash and swallow the new job).
    /// </summary>
    public static async Task<Guid> ScheduleAsync(IJobManager jobManager, Guid identityId, string baseUrl, string handoffToken,
        long startRowId)
    {
        await jobManager.DeleteJobByHashAsync(JobHashFor(identityId));
        var job = jobManager.NewJob<PayloadMoveJob>(identityId);
        job.Data = new PayloadMoveState { BaseUrl = baseUrl, HandoffToken = handoffToken };
        job.Data.StartFrom(startRowId);

        return await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.Now,
            // A run that throws (the target store failing) is retried; waits on the source are deferrals
            // and spend no attempt
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromMinutes(1),
            // Kept so the operator can read how it went
            OnSuccessDeleteAfter = TimeSpan.FromDays(90),
            OnFailureDeleteAfter = TimeSpan.FromDays(90),
        });
    }

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (IdentityId is not { } identityId)
        {
            logger.LogError("Payload move job without an identity; dropping it");
            return JobExecutionResult.Abort();
        }

        if (Data.IsFinished)
        {
            return JobExecutionResult.Success();
        }

        // Right after an import this node may not have loaded the identity yet (it does within the catch-up interval)
        var registration = registry.Get(identityId);
        if (registration == null)
        {
            return JobExecutionResult.Defer(DateTimeOffset.Now.AddMinutes(1));
        }

        await using var scope = tenants.GetTenantScope(registration.PrimaryDomainName).BeginLifetimeScope("PayloadMove");
        var identityDatabase = scope.Resolve<IdentityDatabase>();
        var paths = new TenantPathManager(config, identityId);

        var transfer = new PayloadMoveTransfer(
            // A dev source runs on a self-signed certificate, as in IdentityRegistrationService
            new HttpPayloadMoveSourceClient(httpClientFactory, Data.BaseUrl, identityId,
                allowUntrustedServerCertificate: !config.CertificateRenewal.UseCertificateAuthorityProductionServers),
            scope.Resolve<LongTermPayloadStore>(),
            (belowRowId, count) => identityDatabase.DriveMainIndex.GetFilePayloadRowsBelowAsync(belowRowId, count),
            o => o.PathIn(paths),
            config.PayloadMove.Parallelism,
            logger);

        var result = await transfer.RunSliceAsync(Data, SliceBudget, cancellationToken);

        logger.LogInformation(
            "Payload move for {domain}: {status}, {files} file(s), {objects} object(s), {bytes} bytes, {skipped} already here, {failures} failure(s)",
            registration.PrimaryDomainName, Data.Status, Data.Files, Data.Objects, Data.Bytes, Data.Skipped, Data.FailureCount);

        return result.End switch
        {
            SliceEnd.Continue => JobExecutionResult.Repeat(DateTimeOffset.Now),
            SliceEnd.Wait => JobExecutionResult.Defer(DateTimeOffset.Now.Add(result.Wait)),
            _ => JobExecutionResult.Success()
        };
    }

    // The job API shows a job's data; never the credential
    public override JobApiResponse CreateApiResponseObject()
    {
        var response = base.CreateApiResponseObject();
        response.Data = OdinSystemSerializer.Serialize(Data.Redacted());
        return response;
    }

    public override string? SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<PayloadMoveState>(json);
    }
}
