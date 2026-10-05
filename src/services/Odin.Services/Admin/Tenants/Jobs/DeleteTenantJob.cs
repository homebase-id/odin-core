using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Email.Dkim;
using Odin.Services.Email.Mailbox;
using Odin.Services.Registry;
using Odin.Services.Registry.PayloadMove;

namespace Odin.Services.Admin.Tenants.Jobs;
#nullable enable

public class DeleteTenantJobData
{
    public string Domain { get; set; } = string.Empty;
}

/// <summary>
/// Deletes what this host holds of a disabled tenant: its registration, certificate, data, payloads, mailbox and DKIM
/// keys. Never its DNS: PowerDNS is shared, and after a move the records are another host's. Removing DNS is a
/// separate, deliberate command (delete-identity-dns), run on the host the records point at.
/// </summary>
public class DeleteTenantJob(
    ILogger<DeleteTenantJob> logger,
    IIdentityRegistry identityRegistry,
    IMailboxProvider mailboxProvider,
    IDkimStore dkimStore,
    PayloadMoveSource payloadMoveSource) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("324fa88f-2ef6-404a-a511-9ef65ea841af");
    public override string JobType => JobTypeId.ToString();

    public DeleteTenantJobData Data { get; set; } = new ();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Data.Domain))
        {
            throw new InvalidOperationException("Domain is required");
        }

        logger.LogDebug("Starting delete tenant {domain}", Data.Domain);
        var sw = Stopwatch.StartNew();

        // Checked when it was queued; it may have been enabled since
        var registration = await identityRegistry.GetAsync(Data.Domain);
        if (registration != null && registration.Status != TenantStatus.Disabled)
        {
            throw new InvalidOperationException($"Not deleting {Data.Domain}: it is {registration.Status} now, and only a disabled tenant is deleted");
        }

        await identityRegistry.DeleteRegistration(Data.Domain);

        // Email ride-along (docs/email-keys-plan.md): mailbox + DKIM keys, best-effort - never blocks deletion
        try
        {
            await mailboxProvider.DeleteMailboxAsync(Data.Domain);
            await dkimStore.DeleteKeysAsync(Data.Domain);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Email cleanup failed for {domain}; clean up manually", Data.Domain);
        }

        if (registration != null)
        {
            await payloadMoveSource.ForgetAsync(registration.Id);
        }

        logger.LogDebug("Finished delete tenant {domain} in {elapsed}s", Data.Domain, sw.ElapsedMilliseconds / 1000.0);
        return JobExecutionResult.Success();
    }

    //

    public override string? SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    //

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<DeleteTenantJobData>(json);
    }
}


