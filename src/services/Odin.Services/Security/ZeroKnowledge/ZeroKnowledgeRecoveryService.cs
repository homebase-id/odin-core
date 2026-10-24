using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Util;

namespace Odin.Services.Security.ZeroKnowledge;

/// <summary>
/// Recovery in the browser (docs/zero-knowledge-recovery-plan.md): the password copy recovery gives
/// back, and the takeover of the recovery phrase the owner wrote down (decision 3), in which the
/// browser locks the zero-knowledge master key with the phrase and the server forgets every copy it
/// could open.
/// </summary>
/// <remarks>
/// Its own service because it spans the password, the phrase, the zero-knowledge keys and
/// server-dealt Shamir, and <see cref="OwnerSecretService"/> and <see cref="ShamirConfigurationService"/>
/// sit above <see cref="ZeroKnowledgeKeyService"/>.
/// </remarks>
public class ZeroKnowledgeRecoveryService(
    ILogger<ZeroKnowledgeRecoveryService> logger,
    ZeroKnowledgeKeyService zeroKnowledgeKeyService,
    PasswordKeyRecoveryService passwordKeyRecoveryService,
    ShamirConfigurationService shamirConfigurationService,
    OwnerSecretService secretService,
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

    /// <summary>
    /// Writes the password copy, for keys made before recovery in the browser or a copy that no longer
    /// matches.  Only with the password itself: a stolen session must not be able to replace what
    /// recovery gives back.
    /// </summary>
    public async Task SavePasswordAsync(ZkPasswordRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        request.Validate();
        await secretService.AssertValidPasswordAsync(request.CurrentAuthenticationPasswordReply);
        await zeroKnowledgeKeyService.SavePasswordAsync(request.ZkMasterKeyEncryptedPassword);
    }

    /// <summary>
    /// Step 1: the phrase, once, and only with the password itself, so a stolen session can't read it.
    /// </summary>
    public async Task<ZkPhraseHandoverResponse> HandOverAsync(ZkPhraseHandoverRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        request.Validate();
        await secretService.AssertValidPasswordAsync(request.CurrentAuthenticationPasswordReply);

        return new ZkPhraseHandoverResponse
        {
            Phrase = await passwordKeyRecoveryService.GetPhraseForTakeoverAsync(odinContext)
        };
    }

    /// <summary>
    /// Step 2: stores the browser's copies and forgets the server's.  Needs the password itself and
    /// the phrase's check value.  Neither proves the copies open: the server can't open them, so it
    /// takes the browser's word, as with every zero-knowledge copy.  The reset copy it makes itself.
    /// </summary>
    /// <remarks>
    /// Server-dealt Shamir that still locks the phrase is dealt again first, in its own transaction:
    /// a failed deal must roll back on its own, and then it is removed rather than block this.
    /// </remarks>
    public async Task TakeOverAsync(ZkPhraseTakeoverRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        request.Validate();
        await secretService.AssertValidPasswordAsync(request.CurrentAuthenticationPasswordReply);
        await passwordKeyRecoveryService.AssertPhraseCheckAsync(request.PhraseCheck64, odinContext);

        await shamirConfigurationService.MoveOffThePhraseAsync(odinContext);

        await using var tx = await db.BeginStackedTransactionAsync();
        await zeroKnowledgeKeyService.SavePhraseCopiesAsync(request, odinContext);
        await passwordKeyRecoveryService.CompleteTakeoverAsync(request.PhraseCheck64);
        tx.Commit();

        logger.LogInformation("Recovery phrase taken over by the browser");
    }

    /// <summary>
    /// Recovery with the phrase when the password copy doesn't log in (someone with the password
    /// rewrote it): the reset key opens today's master key, the password is set as a reset sets it,
    /// and the recovered zero-knowledge master key is locked by the new password.  No login.
    /// </summary>
    public async Task ResetPasswordWithPhraseAsync(ZkPhraseResetRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        request.Validate();

        using var masterKey = await zeroKnowledgeKeyService.OpenMasterKeyWithResetKeyAsync(request.ResetKey64);
        await secretService.ResetPasswordUsingMasterKeyAsync(masterKey, request.PasswordReply, request.ZkKeys);

        logger.LogInformation("Password reset with the recovery phrase");
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

        await passwordKeyRecoveryService.AssertPhraseCheckAsync(request.PhraseCheck64, odinContext);
        await zeroKnowledgeKeyService.SavePhraseCopiesAsync(request, odinContext);
    }
}
