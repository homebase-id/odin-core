using System;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Security.Email;
using Odin.Services.Security.Health.RiskAnalyzer;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Security.Health;

/// <summary>
/// Handles the assisting of owners ensuring their password and the like are in good health
/// </summary>
public class OwnerSecurityHealthService(
    OwnerSecretService secretService,
    PasswordKeyRecoveryService recoveryService,
    TenantConfigService tenantConfigService,
    ShamirConfigurationService shamirConfigurationService,
    ShamirReadinessCheckerService readinessCheckerService,
    PublicPrivateKeyService publicPrivateKeyService,
    IDriveManager driveManager,
    ILogger<OwnerSecurityHealthService> logger,
    RecoveryNotifier recoveryNotifier,
    TableKeyValueCached keyValueTable)
{
    private static readonly Guid VerificationStorageId = Guid.Parse("475c72c0-bb9c-4dc9-a565-7e72319ff2b8");
    private const string VerificationStatusDataContextKey = "c45430e7-9c05-49fa-bc8b-d8c1f261f57e";

    private static readonly SingleKeyValueStorage VerificationStatusStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(VerificationStatusDataContextKey));

    private static readonly Guid PeriodicSecurityHealthCheckStatusStorageId = Guid.Parse("25d29d27-8997-4fbc-b50e-e609a27633dc");
    private const string PeriodicSecurityHealthCheckStatusContextKey = "a91549c6-b7d1-435d-8962-2cf5d1e36c65";

    private static readonly SingleKeyValueStorage PeriodicSecurityHealthCheckStatusStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(PeriodicSecurityHealthCheckStatusContextKey));


    public async Task VerifyPasswordAsync(PasswordReply reply, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        _ = await secretService.AssertValidPasswordAsync(reply);
        await UpdateVerificationStatusInternalAsync(updatePasswordLastVerified: true);
    }

    public async Task VerifyRecoveryKeyAsync(VerifyRecoveryKeyRequest request, IOdinContext odinContext)
    {
        var decryptedBytes = await publicPrivateKeyService.EccDecryptPayload(request.EncryptedRecoveryKey, odinContext);
        var recoveryKey = decryptedBytes.ToStringFromUtf8Bytes();

        // this throws an OdinSecurityException if the key is invalid
        var mk = await recoveryService.AssertValidKeyAsync(recoveryKey);
        mk.Wipe();
        await UpdateVerificationStatusInternalAsync(updateRecoveryKeyLastVerified: true);
        await recoveryService.ConfirmInitialRecoveryKeyStorage(odinContext);
    }

    public async Task<RecoveryInfo> GetRecoveryInfo(bool live, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwner();

        var package = await shamirConfigurationService.GetDealerShardPackage(odinContext);
        var recoveryInfo = await recoveryService.GetRecoveryInfo();


        if (package == null)
        {
            return new RecoveryInfo
            {
                IsConfigured = false,
                ConfigurationUpdated = null,
                UsesAutomaticRecovery = false,
                Email = recoveryInfo?.Email,
                EmailLastVerified = recoveryInfo?.EmailLastVerified,
                Status = await GetVerificationStatusInternalAsync(),
                HasRecoveryKeyBeenViewed = await recoveryService.HasRecoveryKeyBeenViewed(),
                RecoveryRisk = null
            };
        }

        PeriodicSecurityHealthCheckStatus healthCheckStatus;
        if (live)
        {
            // get the latest
            healthCheckStatus = await UpdateHealthCheck(odinContext);
        }
        else
        {
            healthCheckStatus = await PeriodicSecurityHealthCheckStatusStorage
                .GetAsync<PeriodicSecurityHealthCheckStatus>(keyValueTable, PeriodicSecurityHealthCheckStatusStorageId);
        }

        return new RecoveryInfo()
        {
            IsConfigured = true,
            ConfigurationUpdated = package.Updated,
            Email = recoveryInfo?.Email,
            UsesAutomaticRecovery = package.UsesAutomatedRecovery,
            EmailLastVerified = recoveryInfo?.EmailLastVerified,
            Status = await GetVerificationStatusInternalAsync(),
            HasRecoveryKeyBeenViewed = await recoveryService.HasRecoveryKeyBeenViewed(),
            RecoveryRisk = DealerShardAnalyzer.Analyze(package, healthCheckStatus)
        };
    }

    /// <summary>
    /// Sends an email to the new email address for verification
    /// </summary>
    public async Task StartUpdateRecoveryEmail(string newEmail, PasswordReply passwordReply, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        if (!MailAddress.TryCreate(newEmail, out var email))
        {
            throw new OdinClientException("Invalid email address", OdinClientErrorCode.InvalidEmail);
        }

        _ = await secretService.AssertValidPasswordAsync(passwordReply);
        await recoveryService.StartUpdateRecoveryEmail(email, odinContext);
    }

    public async Task FinalizeUpdateRecoveryEmail(Guid nonceId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        await recoveryService.UpdateAccountRecoveryEmail(nonceId);
        
        logger.LogDebug("Account Recovery Email verified, enabling security health report");
        var request = new UpdateFlagRequest()
        {
            FlagName = TenantConfigFlagNames.SendMonthlySecurityHealthReport.ToString(),
            Value = true.ToString()
        };

        await tenantConfigService.UpdateSystemFlagAsync(request, odinContext);
    }

    /// <summary>
    /// Checks the health of distributed shards; writes results to storage
    /// </summary>
    public async Task<PeriodicSecurityHealthCheckStatus> RunHealthCheck(IOdinContext odinContext)
    {
        var shardDrive = await driveManager.GetDriveAsync(SystemDriveConstants.ShardRecoveryDrive.Alias);
        if (null == shardDrive)
        {
            logger.LogDebug("Shard recovery drive is not configured, skipping health check");
            return new PeriodicSecurityHealthCheckStatus()
            {
                LastUpdated = UnixTimeUtc.Now(),
                IsConfigured = false
            };
        }

        var dealerShardPackage = await shamirConfigurationService.GetDealerShardPackage(odinContext);

        if (null == dealerShardPackage)
        {
            logger.LogDebug("Dealer shard package is not configured, skipping health check");
            return new PeriodicSecurityHealthCheckStatus()
            {
                LastUpdated = UnixTimeUtc.Now(),
                IsConfigured = false
            };
        }

        var healthResult = new PeriodicSecurityHealthCheckStatus
        {
            LastUpdated = UnixTimeUtc.Now(),
            IsConfigured = true
        };

        var verificationResult = await readinessCheckerService.VerifyRemotePlayerShards(odinContext);

        foreach (var (odinId, result) in verificationResult.Players)
        {
            var envelope = dealerShardPackage.Envelopes.FirstOrDefault(e => e.Player.OdinId == odinId);
            if (null == envelope)
            {
                // verification issue occured; this should not occur
                throw new OdinSystemException($"Missing a PlayerEnvelop for {odinId}.  Could not find it in the verification results");
            }

            var playerResult = new PlayerShardHealthResult
            {
                Player = envelope.Player,
                IsValid = result.IsValid,
                TrustLevel = result.TrustLevel,
                IsMissing = false,
                ShardId = envelope.ShardId
            };

            healthResult.Players.Add(playerResult);
        }

        return healthResult;
    }

    public async Task NotifyUser(IOdinContext odinContext)
    {
        var recoveryInfo = await GetRecoveryInfo(live: true, odinContext);
        await recoveryNotifier.NotifyUser(odinContext.Tenant, recoveryInfo, odinContext);
    }

    public async Task<bool> GetSecurityNeedsAttentionStatus(IOdinContext odinContext)
    {
        if (!(await recoveryService.HasRecoveryKeyBeenViewed()))
        {
            return true;
        }

        var recoveryInfo = await GetRecoveryInfo(live: false, odinContext);

        if (recoveryInfo is null)
        {
            return true;
        }

        if (!recoveryInfo.IsConfigured ||
            string.IsNullOrEmpty(recoveryInfo.Email) ||
            !recoveryInfo.EmailLastVerified.HasValue ||
            !recoveryInfo.RecoveryRisk.IsRecoverable)
        {
            return true;
        }

        var maxWait = TimeSpan.FromDays(30 * 6);
        var now = DateTime.UtcNow;

        bool IsStale(DateTime dt) => now - dt.ToUniversalTime() > maxWait;

        if (IsStale(recoveryInfo.EmailLastVerified.Value.ToDateTime()) ||
            IsStale(recoveryInfo.Status.RecoveryKeyLastVerified.ToDateTime()) ||
            IsStale(recoveryInfo.Status.PasswordLastVerified.ToDateTime()))
        {
            return true;
        }

        return false;
    }
    
    private async Task<VerificationStatus> GetVerificationStatusInternalAsync()
    {
        var status = await VerificationStatusStorage.GetAsync<VerificationStatus>(keyValueTable, VerificationStorageId);

        return status ?? new VerificationStatus
        {
            PasswordLastVerified = default,
            RecoveryKeyLastVerified = default
        };
    }

    private async Task UpdateVerificationStatusInternalAsync(bool updatePasswordLastVerified = false,
        bool updateRecoveryKeyLastVerified = false)
    {
        var status = await GetVerificationStatusInternalAsync();

        if (updatePasswordLastVerified)
        {
            status.PasswordLastVerified = UnixTimeUtc.Now();
        }

        if (updateRecoveryKeyLastVerified)
        {
            status.RecoveryKeyLastVerified = UnixTimeUtc.Now();
        }

        await VerificationStatusStorage.UpsertAsync(keyValueTable, VerificationStorageId, status);
    }

    private async Task<PeriodicSecurityHealthCheckStatus> UpdateHealthCheck(IOdinContext odinContext)
    {
        var healthResult = await RunHealthCheck(odinContext);
        await PeriodicSecurityHealthCheckStatusStorage.UpsertAsync(keyValueTable, PeriodicSecurityHealthCheckStatusStorageId, healthResult);
        return healthResult;
    }
}