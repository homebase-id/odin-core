using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.Hostname;
using Odin.Core.Serialization;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tenant.Container;

namespace Odin.Services.DataSubscription.Follower;

#nullable enable

public class SyncChannelFilesJobData
{
    public string? Tenant { get; set; }

    /// <summary>The identity whose channels are being fetched.</summary>
    public string? PeerIdentity { get; set; }

    /// <summary>
    /// The peer's client access token, AES-encrypted under the tenant's temporal key.
    /// </summary>
    /// <remarks>
    /// The job carries the credential because it cannot mint one. Minting requires the ICR key, which is
    /// master-key protected -- available to the request that accepted the connection, gone by the time a
    /// background job runs. The same technique <c>VersionUpgradeJob</c> uses to carry the owner's token.
    /// </remarks>
    public byte[]? EncryptedPeerToken { get; set; }

    public byte[]? Iv { get; set; }

    /// <summary>
    /// The owner's client auth token, encrypted the same way. Null when an app accepted.
    /// </summary>
    /// <remarks>
    /// Rebuilding the owner's context is what lets the job seal encrypted posts into the feed drive: the
    /// storage key is reachable from their key store key and nowhere else. Without it the job can still
    /// fetch, but only the unencrypted posts land -- which is the app-accept case, and the same limit the
    /// inline version had there.
    /// </remarks>
    public byte[]? EncryptedCallerToken { get; set; }

    public byte[]? CallerIv { get; set; }
}

/// <summary>
/// Fetches a newly connected identity's channel files into the feed drive, off the request that accepted
/// them.
/// </summary>
/// <remarks>
/// Accepting a connection used to run this inline. That put two retry-wrapped peer calls on a user-facing
/// request -- three attempts each against a 100s default HttpClient timeout, no cancellation token -- so a
/// slow or unreachable sender could hold the accept open for minutes. The accept no longer waits: it
/// schedules this and returns.
/// <para>
/// What the job cannot do is produce the feed drive's storage key, so encrypted posts are still skipped
/// (see the check in <see cref="FollowerService.SynchronizeChannelFilesAsync"/>). Deferring does not make
/// that worse or better -- it is the same limit the inline version had.
/// </para>
/// </remarks>
// ReSharper disable once ClassNeverInstantiated.Global (DI)
public class SyncChannelFilesJob(
    IMultiTenantContainer tenantContainer,
    ILogger<SyncChannelFilesJob> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("b2f1c6d4-3e57-4a19-9c0b-7d51e8a4f206");
    public override string JobType => JobTypeId.ToString();

    public SyncChannelFilesJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (!OdinId.IsValid(Data.Tenant) || !OdinId.IsValid(Data.PeerIdentity))
        {
            logger.LogError("SyncChannelFilesJob received an invalid tenant or peer identity; aborting");
            return JobExecutionResult.Abort();
        }

        try
        {
            await using var scope = tenantContainer.GetTenantScope(Data.Tenant!)
                .BeginLifetimeScope($"SyncChannelFilesJob:Run:{Data.Tenant}:{Guid.NewGuid()}");

            scope.Resolve<IStickyHostname>().Hostname = $"{Data.Tenant}&";

            var tenantContext = scope.Resolve<TenantContext>();
            var tokenBytes = AesCbc.Decrypt(Data.EncryptedPeerToken, tenantContext.TemporalEncryptionKey, Data.Iv);
            var peerToken = ClientAccessToken.FromPortableBytes(tokenBytes);

            var odinContext = await BuildContextAsync(scope, tenantContext);

            // The shared secret is the peer's; everything else the sync needs -- the ICR key to
            // authenticate, and the feed drive's storage key if it has one -- comes from the context.
            var followerService = scope.Resolve<FollowerService>();
            await followerService.SynchronizeChannelFilesAsync((OdinId)Data.PeerIdentity!, odinContext,
                peerToken.SharedSecret);

            peerToken.AccessTokenHalfKey.Wipe();
            peerToken.SharedSecret.Wipe();
        }
        catch (Exception e)
        {
            // Retried by the schedule. A peer that is down when the connection is accepted is the case
            // this job exists for, so failing here is ordinary rather than exceptional.
            logger.LogInformation(e, "SyncChannelFilesJob could not fetch channels from {peer} for {tenant}",
                Data.PeerIdentity, Data.Tenant);
            return JobExecutionResult.Fail();
        }

        return JobExecutionResult.Success();
    }

    /// <summary>
    /// The owner's context if their token was carried, otherwise a keyless one.
    /// </summary>
    private async Task<IOdinContext> BuildContextAsync(ILifetimeScope scope, TenantContext tenantContext)
    {
        if (Data.EncryptedCallerToken == null || Data.CallerIv == null)
        {
            return OdinContextUpgrades.BuildFeedSyncContext((OdinId)Data.Tenant!);
        }

        var callerBytes = AesCbc.Decrypt(Data.EncryptedCallerToken, tenantContext.TemporalEncryptionKey, Data.CallerIv);
        var callerToken = ClientAuthenticationToken.FromPortableBytes(callerBytes);

        var restored = new OdinContext { Tenant = default, AuthTokenCreated = null, Caller = null };
        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            AccessRegistrationId = null,
            DevicePushNotificationKey = null,
            ClientIdOrDomain = null
        };

        // Owner first, then app: both carry the ICR key the channel query mints its peer token from, and
        // that key is why the caller's context is carried at all -- a built one has none and cannot pull
        // encrypted headers. Only the owner's also carries the feed drive's storage key, so an app-built
        // context writes what needs no sealing and deposits the rest.
        var authService = scope.Resolve<OwnerAuthenticationService>();
        try
        {
            if (await authService.UpdateOdinContextAsync(callerToken, clientContext, restored))
            {
                return OdinContextUpgrades.PrepForSynchronizeChannelFiles(restored);
            }
        }
        catch (OdinSecurityException)
        {
            // An app token is not an owner token, and this rejects one by throwing rather than returning
            // false. Which of the two accepted the connection is not recorded, so it is asked rather than
            // assumed.
        }

        var appRegistrationService = scope.Resolve<IAppRegistrationService>();
        var appContext = await appRegistrationService.GetAppPermissionContextAsync(callerToken, restored);
        if (appContext != null)
        {
            return OdinContextUpgrades.PrepForSynchronizeChannelFiles(appContext);
        }

        // Neither validates any more -- the token expired between the accept and the drain.
        logger.LogInformation("SyncChannelFilesJob: the token carried from the accept no longer validates");
        return OdinContextUpgrades.BuildFeedSyncContext((OdinId)Data.Tenant!);

    }

    public override string? CreateJobHash()
    {
        var text = JobType + Data.Tenant + Data.PeerIdentity;
        return SHA256.HashData(text.ToUtf8ByteArray()).ToBase64();
    }

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<SyncChannelFilesJobData>(json);
    }
}
