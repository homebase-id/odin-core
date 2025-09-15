using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Login;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;

namespace Odin.Services.Authentication.Owner;

/// <summary>
/// Handles the assisting of owners ensuring their password and the like are in good health
/// </summary>
public class OwnerSecurityHealthService(
    OwnerSecretService secretService,
    PasswordKeyRecoveryService recoveryService,
    PublicPrivateKeyService publicPrivateKeyService,
    TableKeyValueCached keyValueTable)
{
    private static readonly Guid VerificationStorageId = Guid.Parse("475c72c0-bb9c-4dc9-a565-7e72319ff2b8");

    private const string VerificationStatusDataContextKey = "c45430e7-9c05-49fa-bc8b-d8c1f261f57e";

    private static readonly SingleKeyValueStorage VerificationStatusStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(VerificationStatusDataContextKey));


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

    public async Task<VerificationStatus> GetVerificationStatusAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        return await GetVerificationStatusInternalAsync();
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

        //todo: update when we build this bit
        status.DistributedRecoveryLastVerified = 0;

        await VerificationStatusStorage.UpsertAsync(keyValueTable, VerificationStorageId, status);
        return status;
    }
}