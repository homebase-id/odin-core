using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Core.Util;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Registry.Registration;

#nullable enable

namespace Odin.Services.Email.Relay;

public class MailRelayOnboardingJobData
{
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// How many times we have deferred waiting for DNS to propagate. Bounded, because a
    /// domain whose records will never resolve - a zone we do not control, a typo, a tenant
    /// deleted mid-flight - would otherwise poll someone else's API every ten minutes for
    /// ever. Giving up is visible in the status surface; an endless loop is not.
    /// </summary>
    public int VerifyAttempts { get; set; }
}

/// <summary>
/// Registers one tenant domain with the outbound relay and publishes the DNS it needs.
///
/// A job rather than an inline call because every step is a request to someone else's API:
/// a transient network failure must not leave a tenant with a mailbox that cannot send and
/// no record of why. JobSchedule's MaxAttempts/RetryDelay carry the retries.
///
/// Every step is safe to repeat, which is what makes retrying sound:
///   1. EnsureDomainAsync reads before it writes (their /domain/add is not idempotent)
///   2. DNS writes are REPLACE-semantics rrsets
///   3. Verification is a read the relay performs against public DNS
///
/// Deliberately NOT awaited by activation. DNS propagation takes minutes to hours, and a
/// tenant should get a working mailbox and published key immediately, with outbound relay
/// converging behind it. A tenant who cannot send yet is a worse outcome than one who cannot
/// send *for a while*, but a tenant who cannot finish activation is worse than both.
/// </summary>
public class MailRelayOnboardingJob(
    ILogger<MailRelayOnboardingJob> logger,
    IMailRelayProvider relayProvider,
    IIdentityRegistrationService identityRegistrationService) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("6f1b0c94-3d27-4a58-9b0e-7c2a5f8d41e3");
    public override string JobType => JobTypeId.ToString();

    public MailRelayOnboardingJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Data.Domain))
        {
            throw new InvalidOperationException("Domain is required");
        }

        if (!relayProvider.IsConfigured)
        {
            // The flag was turned off between scheduling and running. Not a failure: there is
            // simply nothing to onboard, and retrying would never change that.
            logger.LogInformation("Relay onboarding skipped for {domain}: no relay configured", Data.Domain);
            return JobExecutionResult.Success();
        }

        var domain = new AsciiDomainName(Data.Domain);

        var state = await relayProvider.EnsureDomainAsync(domain, cancellationToken);

        // Written wherever the tenant's records live - the shared apex zone for managed
        // domains, its own zone otherwise. False means the DNS is not ours (third-party DNS,
        // or no PowerDNS access), in which case the records are shown as instructions by the
        // status surface and verification below will keep failing until someone adds them.
        var written = await identityRegistrationService.WriteOnActivationRecords(domain, state.Records);
        if (!written)
        {
            logger.LogInformation(
                "Relay: {domain} DNS is not ours to write - {count} record(s) must be added by hand",
                domain, state.Records.Count);
            return JobExecutionResult.Success();
        }

        var verified = await relayProvider.VerifyDomainAsync(domain, cancellationToken);
        if (verified.Verified)
        {
            logger.LogInformation("Relay: {domain} verified, outbound relay is live", domain);
            return JobExecutionResult.Success();
        }

        // Not an error, so NOT Fail(): we published the records seconds ago and the relay
        // resolves them from public DNS, which has not caught up. Fail() would spend the
        // transient-flake budget on something that is not a flake. Defer keeps the job intact
        // and tries again later; the status surface reports the records as pending meanwhile.
        Data.VerifyAttempts++;
        foreach (var problem in verified.Problems)
        {
            logger.LogDebug("Relay: {domain} - {problem}", domain, problem);
        }

        if (Data.VerifyAttempts >= MaxVerifyAttempts)
        {
            logger.LogWarning(
                "Relay: {domain} still unverified after {attempts} attempts (~{hours}h); giving up. " +
                "The records are published; the status page reports what the relay cannot resolve.",
                domain, Data.VerifyAttempts, MaxVerifyAttempts * RetryMinutes / 60);
            return JobExecutionResult.Success();
        }

        logger.LogInformation("Relay: {domain} not verified yet ({problems} problem(s)); attempt {n}/{max}",
            domain, verified.Problems.Count, Data.VerifyAttempts, MaxVerifyAttempts);

        return JobExecutionResult.Defer(DateTimeOffset.Now.AddMinutes(RetryMinutes));
    }

    /// <summary>
    /// Long enough for DNS to propagate. Retrying every few seconds would only burn someone
    /// else's rate limit to learn the same thing.
    /// </summary>
    private const int RetryMinutes = 10;

    /// <summary>~24 hours of deferral. Past that it is not propagation, it is misconfiguration.</summary>
    private const int MaxVerifyAttempts = 144;

    public override string? SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<MailRelayOnboardingJobData>(json);
    }
}
