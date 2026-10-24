using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Base;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Security.ZeroKnowledge;

/// <summary>
/// The browser takes over the recovery phrase the owner wrote down (docs/zero-knowledge-recovery-plan.md,
/// decision 3): it locks the zero-knowledge master key with the phrase, and the server forgets every
/// copy it could open.
/// </summary>
/// <remarks>
/// Its own service because it spans the phrase, the zero-knowledge keys and server-dealt Shamir, and
/// <see cref="ShamirConfigurationService"/> sits above <see cref="ZeroKnowledgeKeyService"/>.
/// </remarks>
public class RecoveryPhraseTakeoverService(
    ILogger<RecoveryPhraseTakeoverService> logger,
    ZeroKnowledgeKeyService zeroKnowledgeKeyService,
    PasswordKeyRecoveryService passwordKeyRecoveryService,
    ShamirConfigurationService shamirConfigurationService,
    IdentityDatabase db)
{
    /// <summary>What the browser needs after login: the keys, and where the phrase is.</summary>
    public async Task<ZkStatusResponse> GetStatusAsync(IOdinContext odinContext)
    {
        var keys = await zeroKnowledgeKeyService.GetKeysAsync(odinContext);
        var (exists, takenOver) = await passwordKeyRecoveryService.GetPhraseStateAsync();

        var phrase = !exists ? RecoveryPhraseState.None
            : !takenOver ? RecoveryPhraseState.OnServer
            : keys?.PhraseKeyEncryptedZkMasterKey == null ? RecoveryPhraseState.NeedsReentry
            : RecoveryPhraseState.TakenOver;

        return new ZkStatusResponse
        {
            ZkSaltKek64 = keys?.ZkSaltKek64,
            ZkKekEncryptedZkMasterKey = keys?.ZkKekEncryptedZkMasterKey,
            ZkMasterKeyEncryptedPassword = keys?.ZkMasterKeyEncryptedPassword,
            RecoveryPhrase = phrase
        };
    }

    /// <summary>Step 1: the phrase, once, right after a password login.</summary>
    public async Task<ZkPhraseHandoverResponse> HandOverAsync(IOdinContext odinContext)
    {
        return new ZkPhraseHandoverResponse
        {
            Phrase = await passwordKeyRecoveryService.GetPhraseForTakeoverAsync(odinContext)
        };
    }

    /// <summary>
    /// Step 2: stores the browser's copies and forgets the server's, in one transaction.  Server-dealt
    /// Shamir that still locks the phrase is dealt again first, so it keeps working without it.
    /// </summary>
    public async Task TakeOverAsync(ZkPhraseCopiesRequest request, IOdinContext odinContext)
    {
        zeroKnowledgeKeyService.AssertRecentPasswordLogin(odinContext);
        request.Validate();

        await using var tx = await db.BeginStackedTransactionAsync();
        await shamirConfigurationService.MoveOffThePhraseAsync(odinContext);
        await zeroKnowledgeKeyService.SavePhraseCopiesAsync(request);
        await passwordKeyRecoveryService.CompleteTakeoverAsync(odinContext);
        tx.Commit();

        logger.LogInformation("Recovery phrase taken over by the browser");
    }

    /// <summary>
    /// After a reset with today's master key forgot the phrase copies: the owner typed the phrase in
    /// again, and the browser locked the new zero-knowledge master key with it.
    /// </summary>
    public async Task ReenterAsync(ZkPhraseCopiesRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        request.Validate();

        var status = await GetStatusAsync(odinContext);
        if (status.RecoveryPhrase != RecoveryPhraseState.NeedsReentry)
        {
            throw new OdinClientException($"The recovery phrase is {status.RecoveryPhrase}, not waiting to be typed in",
                status.RecoveryPhrase == RecoveryPhraseState.TakenOver
                    ? OdinClientErrorCode.RecoveryPhraseTakenOver
                    : OdinClientErrorCode.RecoveryPhraseNotTakenOver);
        }

        await passwordKeyRecoveryService.AssertPhraseCheckAsync(request.PhraseCheck64);
        await zeroKnowledgeKeyService.SavePhraseCopiesAsync(request);
    }
}
