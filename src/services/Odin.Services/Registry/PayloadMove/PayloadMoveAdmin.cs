using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;
using Odin.Services.JobManagement;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

/// <summary>What an operator sees of an identity's payload move, from either side.</summary>
public class PayloadMoveReport
{
    public string Domain { get; set; } = "";

    /// <summary>Set on the host the identity was exported from.</summary>
    public PayloadMoveSourceView? Source { get; set; }

    /// <summary>Set on the host the identity was imported into.</summary>
    public PayloadMoveTargetView? Target { get; set; }
}

public class PayloadMoveSourceView
{
    public UnixTimeUtc HandoffExpiresAt { get; set; }
    public UnixTimeUtc? RedeemedAt { get; set; }
    public UnixTimeUtc? CompletedAt { get; set; }

    /// <summary>A target may still need the payloads from here, so the identity cannot be deleted.</summary>
    public bool Pending { get; set; }
}

public class PayloadMoveTargetView
{
    public JobState JobState { get; set; }
    public UnixTimeUtc NextRun { get; set; }
    public string? LastError { get; set; }

    /// <summary>The transfer's checkpoint, without its secrets.</summary>
    public PayloadMoveState Progress { get; set; } = new();
}

public enum PayloadMoveRetryResult { Rearmed, NoMove, Running }

public enum PayloadMoveAcceptResult { Requested, NoMove, Running, Refused }

/// <summary>What an accept-missing request did; <see cref="Objects"/> are the ones it gives up.</summary>
public record PayloadMoveAcceptOutcome(PayloadMoveAcceptResult Result, string? Reason = null, List<PayloadObject>? Objects = null);

/// <summary>The operator's view of payload moves: progress on either side, and re-arming the target's transfer.</summary>
public class PayloadMoveAdmin(IIdentityRegistry registry, IJobManager jobManager, PayloadMoveSource source)
{
    public async Task<PayloadMoveReport?> GetAsync(string domain)
    {
        var registration = await registry.GetAsync(domain);
        if (registration == null)
        {
            return null;
        }

        var report = new PayloadMoveReport { Domain = registration.PrimaryDomainName };

        if (await source.LoadAsync(registration.Id) is { } state)
        {
            report.Source = new PayloadMoveSourceView
            {
                HandoffExpiresAt = state.HandoffExpiresAt,
                RedeemedAt = state.RedeemedAt,
                CompletedAt = state.CompletedAt,
                Pending = PayloadMoveSource.IsPending(state)
            };
        }

        if (await LoadJobAsync(registration.Id) is { } job)
        {
            using (job)
            {
                report.Target = new PayloadMoveTargetView
                {
                    JobState = job.State,
                    NextRun = job.Record!.nextRun,
                    LastError = job.LastError,
                    Progress = job.Data.Redacted()
                };
            }
        }

        return report;
    }

    /// <summary>
    /// Runs the target's transfer again from the newest file. Cheap: whatever already arrived is skipped, and a
    /// redeemed credential is kept. For a move that finished with failures, or that stalled.
    /// </summary>
    public async Task<PayloadMoveRetryResult> RetryAsync(string domain)
    {
        var registration = await registry.GetAsync(domain);
        if (registration == null || await LoadJobAsync(registration.Id) is not { } job)
        {
            return PayloadMoveRetryResult.NoMove;
        }

        using (job)
        {
            if (job.State is JobState.Running or JobState.Preflight)
            {
                // Its slice would save its own checkpoint over ours; ask again once it is between slices
                return PayloadMoveRetryResult.Running;
            }

            job.Data.StartFrom(job.Data.StartRowId);

            await jobManager.RescheduleJobAsync(job.Id!.Value, registration.Id, job.SerializeJobData()!, DateTimeOffset.Now);
            return PayloadMoveRetryResult.Rearmed;
        }
    }

    /// <summary>
    /// Asks the target's transfer to give up the objects its source does not have, so the move can complete and
    /// the source copy can be deleted (#1868). Only for a move that ended with nothing but those; its next run asks
    /// the source for each once more and completes only if every one is still missing.
    /// </summary>
    public async Task<PayloadMoveAcceptOutcome> AcceptMissingAsync(string domain)
    {
        var registration = await registry.GetAsync(domain);
        if (registration == null || await LoadJobAsync(registration.Id) is not { } job)
        {
            return new PayloadMoveAcceptOutcome(PayloadMoveAcceptResult.NoMove);
        }

        using (job)
        {
            if (job.State is JobState.Running or JobState.Preflight)
            {
                return new PayloadMoveAcceptOutcome(PayloadMoveAcceptResult.Running);
            }

            if (job.Data.RequestAcceptMissing() is { } why)
            {
                return new PayloadMoveAcceptOutcome(PayloadMoveAcceptResult.Refused, why);
            }

            await jobManager.RescheduleJobAsync(job.Id!.Value, registration.Id, job.SerializeJobData()!, DateTimeOffset.Now);
            return new PayloadMoveAcceptOutcome(PayloadMoveAcceptResult.Requested, Objects: job.Data.Missing);
        }
    }

    /// <summary>
    /// Whether this identity, moved here, must stay down until its carried queue items' payloads arrive
    /// (<see cref="PayloadMoveState.HoldsResume"/>). Never for an identity without a transfer here.
    /// </summary>
    public async Task<bool> HoldsResumeAsync(string domain)
    {
        if (await registry.GetAsync(domain) is not { } registration || await LoadJobAsync(registration.Id) is not { } job)
        {
            return false;
        }

        using (job)
        {
            return job.Data.HoldsResume;
        }
    }

    private async Task<PayloadMoveJob?> LoadJobAsync(Guid identityId)
    {
        var record = await jobManager.GetJobByHashAsync(PayloadMoveJob.JobHashFor(identityId));
        return record == null ? null : await jobManager.GetJobAsync<PayloadMoveJob>(record.id);
    }
}
