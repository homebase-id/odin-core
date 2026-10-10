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

public enum PayloadMoveRearmResult { Rearmed, NoMove, Running, Refused }

/// <summary>What a retry or accept-missing request did; <see cref="Reason"/> says why it was refused.</summary>
public record PayloadMoveRearmOutcome(PayloadMoveRearmResult Result, string? Reason = null);

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
    public Task<PayloadMoveRearmOutcome> RetryAsync(string domain) => RearmAsync(domain, state =>
    {
        state.StartFrom(state.StartRowId);
        return null;
    });

    /// <summary>
    /// Asks the target's transfer to give up the objects its source does not have, so the move can complete and
    /// the source copy can be deleted (#1868). Only for a move that ended with nothing but those; its next run asks
    /// the source for each once more and completes only if every one is still missing.
    /// </summary>
    public Task<PayloadMoveRearmOutcome> AcceptMissingAsync(string domain) => RearmAsync(domain, state => state.RequestAcceptMissing());

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

    // Changes the target's transfer between its slices and runs it now; `change` returns why it cannot, or null
    private async Task<PayloadMoveRearmOutcome> RearmAsync(string domain, Func<PayloadMoveState, string?> change)
    {
        var registration = await registry.GetAsync(domain);
        if (registration == null || await LoadJobAsync(registration.Id) is not { } job)
        {
            return new PayloadMoveRearmOutcome(PayloadMoveRearmResult.NoMove);
        }

        using (job)
        {
            if (job.State is JobState.Running or JobState.Preflight)
            {
                // Its slice would save its own checkpoint over ours; ask again once it is between slices
                return new PayloadMoveRearmOutcome(PayloadMoveRearmResult.Running);
            }

            if (change(job.Data) is { } why)
            {
                return new PayloadMoveRearmOutcome(PayloadMoveRearmResult.Refused, why);
            }

            await jobManager.RescheduleJobAsync(job.Id!.Value, registration.Id, job.SerializeJobData()!, DateTimeOffset.Now);
            return new PayloadMoveRearmOutcome(PayloadMoveRearmResult.Rearmed);
        }
    }

    private async Task<PayloadMoveJob?> LoadJobAsync(Guid identityId)
    {
        var record = await jobManager.GetJobByHashAsync(PayloadMoveJob.JobHashFor(identityId));
        return record == null ? null : await jobManager.GetJobAsync<PayloadMoveJob>(record.id);
    }
}
