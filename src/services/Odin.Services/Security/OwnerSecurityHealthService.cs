using System;
using System.Net.Mail;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Cache;
using Odin.Core.Time;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Security;

/// <summary>
/// Handles the assisting of owners ensuring their password and the like are in good health
/// </summary>
public class OwnerSecurityHealthService(
    OwnerSecretService secretService,
    PasswordKeyRecoveryService recoveryService,
    ShamirConfigurationService shamirConfigurationService,
    PublicPrivateKeyService publicPrivateKeyService,
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
    }

    public async Task<RecoveryInfo> GetRecoveryInfo(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        return new RecoveryInfo()
        {
            Email = await recoveryService.GetRecoveryEmail(),
            Status = await GetVerificationStatusInternalAsync()
        };
    }

    public async Task<VerificationStatus> GetVerificationStatusAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsOwnerOrSystem();
        return await GetVerificationStatusInternalAsync();
    }

    /// <summary>
    /// Sends an email to the new email address for verification
    /// </summary>
    public async Task StartUpdateRecoveryEmail(string newEmail, PasswordReply passwordReply, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        if (MailAddress.TryCreate(newEmail, out var email))
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
    }

    /// <summary>
    /// Checks the health of distributed shards
    /// </summary>
    public async Task RunHeathCheck(IOdinContext odinContext)
    {
        var dealerShardPackage = await shamirConfigurationService.GetDealerShardPackage(odinContext);

        var healthResult = new PeriodicSecurityHealthCheckStatus
        {
            LastUpdated = UnixTimeUtc.Now(),
            IsConfigured = null != dealerShardPackage
        };

        if (healthResult.IsConfigured)
        {
            var verificationResult = await shamirConfigurationService.VerifyRemotePlayerShards(odinContext);

            foreach (var (odinId, result) in verificationResult.Players)
            {
                var playerResult = new PlayerShardHealthResult
                {
                    PlayerId = (OdinId)odinId,
                    IsValid = result.IsValid,
                };

                healthResult.Players.Add(playerResult);
            }
        }

        await PeriodicSecurityHealthCheckStatusStorage.UpsertAsync(keyValueTable, PeriodicSecurityHealthCheckStatusStorageId, healthResult);
    }

    private async Task<VerificationStatus> GetVerificationStatusInternalAsync()
    {
        var status = await VerificationStatusStorage.GetAsync<VerificationStatus>(keyValueTable, VerificationStorageId);
        var healthCheckStatus =
            await PeriodicSecurityHealthCheckStatusStorage.GetAsync<PeriodicSecurityHealthCheckStatus>(keyValueTable,
                PeriodicSecurityHealthCheckStatusStorageId);
        
        return status ?? new VerificationStatus
        {
            PasswordLastVerified = default,
            RecoveryKeyLastVerified = default,
            PeriodicSecurityHealthCheckStatus = healthCheckStatus
        };
    }

    private async Task<VerificationStatus> UpdateVerificationStatusInternalAsync(bool updatePasswordLastVerified = false,
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
        return status;
    }
}