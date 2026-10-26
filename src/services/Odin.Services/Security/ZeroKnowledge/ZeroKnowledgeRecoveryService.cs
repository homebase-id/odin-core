using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
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
    PublicPrivateKeyService publicPrivateKeyService,
    ShamirRecoveryService shamirRecoveryService,
    IdentityDatabase db)
{
    /// <summary>What the browser needs after login: the keys, and where the phrase is.</summary>
    public async Task<ZkStatusResponse> GetStatusAsync(IOdinContext odinContext)
    {
        var keys = await zeroKnowledgeKeyService.GetKeysAsync(odinContext);
        var (exists, takenOver) = await passwordKeyRecoveryService.GetPhraseStateAsync();

        var phrase = !exists ? RecoveryPhraseState.None
            : !takenOver ? RecoveryPhraseState.OnServer
            : keys?.HasPhraseCopies() != true ? RecoveryPhraseState.NeedsReentry
            : RecoveryPhraseState.TakenOver;

        return new ZkStatusResponse
        {
            ZkSaltKek64 = keys?.ZkSaltKek64,
            ZkKekEncryptedZkMasterKey = keys?.ZkKekEncryptedZkMasterKey,
            RecoveryPhrase = phrase,
            PhrasePublicKeyJwk = keys?.PhrasePublicKeyJwk,
            HasPasswordCopy = keys?.PhrasePublicKeyEncryptedPassword != null,
            ShamirPublicKeyJwk = keys?.HasShamirCopies() == true ? keys.ShamirPublicKeyJwk : null,
            HasShamirPasswordCopy = keys?.ShamirPublicKeyEncryptedPassword != null,
            DealNeedsDealing = await DealNeedsDealingAsync(keys, odinContext)
        };
    }

    /// <summary>
    /// Whether the deal can't recover as it is, so a password login deals it again, to the same holders
    /// with the same threshold, then writes the password copy: an automated deal that gives back nothing
    /// in the browser (made by the server before the browser dealt every deal, or its copies forgotten
    /// by a reset with today's master key), or any deal dealt for another recovery email than today's,
    /// which every holder now refuses.
    /// </summary>
    /// <remarks>A server-made deal has no copies either: the server forgets them when it deals.</remarks>
    public async Task<bool> DealNeedsDealingAsync(IOdinContext odinContext) =>
        await DealNeedsDealingAsync(await zeroKnowledgeKeyService.GetKeysAsync(odinContext), odinContext);

    private async Task<bool> DealNeedsDealingAsync(ZkKeyRecord keys, IOdinContext odinContext)
    {
        // Best effort: the login's key steps read this status, and must not fail over a deal
        try
        {
            var package = await shamirConfigurationService.GetDealerShardPackage(odinContext);
            if (package == null)
            {
                return false;
            }

            if (package.UsesAutomatedRecovery)
            {
                // Only a deal the browser can deal again: without this host's automated identities the
                // flag would stay up for a login that deals nothing
                if (shamirConfigurationService.GetAutomatedRecoveryPlayers() == null)
                {
                    return false;
                }

                if (keys?.HasShamirCopies() != true)
                {
                    return true;
                }
            }

            // A deal from before deals recorded the hash can't tell: a server-made automated one is
            // caught above, a server-made friend one is removed, and any other recovers as it is
            var email = await passwordKeyRecoveryService.GetRecoveryEmailAsync();
            return email != null && package.RecoveryEmailHash != null &&
                   package.RecoveryEmailHash != PasswordKeyRecoveryService.HashRecoveryEmail(email);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Loud: a deal that can't be read is never dealt again, and the next recovery loses the
            // zero-knowledge data
            logger.LogError(e, "Could not read the dealer package for the zero-knowledge status");
            return false;
        }
    }

    /// <summary>
    /// Writes the password copies that are missing (after re-entering the phrase, a friend deal, or a
    /// password change that sent none).  Only with the password itself: a stolen session must not be
    /// able to replace what recovery gives back.
    /// </summary>
    public async Task SavePasswordAsync(ZkPasswordRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        request.Validate();
        await secretService.AssertValidPasswordAsync(request.CurrentAuthenticationPasswordReply);
        await zeroKnowledgeKeyService.SavePasswordAsync(request);
    }

    /// <summary>
    /// Step 1: the phrase, once, and only with the password itself, so a stolen session can't read it.
    /// Server-dealt Shamir that still locks the phrase moves off it first
    /// (<see cref="ShamirConfigurationService.MoveOffThePhraseAsync"/>): if that fails, the phrase is
    /// not handed over, so a failure that repeats doesn't hand it over at every login.
    /// </summary>
    public async Task<ZkPhraseHandoverResponse> HandOverAsync(ZkPhraseHandoverRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        request.Validate();
        await secretService.AssertValidPasswordAsync(request.CurrentAuthenticationPasswordReply);
        await passwordKeyRecoveryService.AssertNotTakenOverAsync();
        await shamirConfigurationService.MoveOffThePhraseAsync(odinContext);

        return new ZkPhraseHandoverResponse
        {
            Phrase = await passwordKeyRecoveryService.GetPhraseForTakeoverAsync(odinContext)
        };
    }

    /// <summary>
    /// Step 2: stores what the browser made from the phrase, and the password sealed to the phrase's
    /// new public key, and forgets the server's copies.  Needs the password itself and the phrase's
    /// check value.  Neither proves the copies open: the server can't open them, so it takes the
    /// browser's word, as with every zero-knowledge copy.  The reset copy it makes itself.
    /// </summary>
    /// <remarks>
    /// The handover moved server-dealt Shamir off the phrase; this checks again, as no deal may lock a
    /// phrase the server forgets.
    /// </remarks>
    public async Task TakeOverAsync(ZkPhraseTakeoverRequest request, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        request.Validate();
        await secretService.AssertValidPasswordAsync(request.CurrentAuthenticationPasswordReply);
        await passwordKeyRecoveryService.AssertPhraseCheckAsync(request.PhraseCheck64, odinContext);

        // Everything the transaction below needs, before moving the deal, which can't be undone
        await passwordKeyRecoveryService.AssertNotTakenOverAsync();
        await zeroKnowledgeKeyService.AssertKeysExistAsync();

        await shamirConfigurationService.MoveOffThePhraseAsync(odinContext);

        await using var tx = await db.BeginStackedTransactionAsync();
        await zeroKnowledgeKeyService.SavePhraseCopiesAsync(request, request.PhrasePublicKeyEncryptedPassword, odinContext);
        await passwordKeyRecoveryService.CompleteTakeoverAsync(request.PhraseCheck64);
        tx.Commit();

        logger.LogInformation("Recovery phrase taken over by the browser");
    }

    /// <summary>
    /// What recovery with the phrase fetches, with no login, or why there is nothing: not taken over
    /// yet (the page uses the old reset), or taken over and its copies forgotten by a reset with
    /// today's master key (the phrase can't recover until the owner types it in again).
    /// </summary>
    public async Task<(ZkPhraseRecoveryResponse Recovery, bool NeedsReentry)> GetPhraseRecoveryAsync()
    {
        var recovery = await zeroKnowledgeKeyService.GetPhraseRecoveryAsync();
        return (recovery, recovery == null && (await passwordKeyRecoveryService.GetPhraseStateAsync()).TakenOver);
    }

    /// <summary>
    /// Recovery with the phrase or with friends when the password copy doesn't log in (someone with
    /// the password sealed junk) or is missing: the reset key opens today's master key, the password is
    /// set as a reset sets it, and the recovered zero-knowledge master key is locked by the new
    /// password.  No login.
    /// </summary>
    public async Task ResetPasswordWithResetKeyAsync(ZkResetRequest request, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        request.Validate();

        byte[] decrypted;
        try
        {
            decrypted = await publicPrivateKeyService.EccDecryptPayload(request.EncryptedResetKey, odinContext);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new OdinSecurityException("The reset key does not open");
        }

        using var resetKey = ZkBlob.AssertBase64(decrypted.ToStringFromUtf8Bytes(), nameof(request.EncryptedResetKey),
            exactLength: ZkBlob.KeyLength).ToSensitiveByteArray();
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(decrypted);
        using var masterKey = await zeroKnowledgeKeyService.OpenMasterKeyWithResetKeyAsync(resetKey);

        // Recovery with friends is over once the password is set, as a finalized recovery ends it: both
        // or neither
        await using var tx = await db.BeginStackedTransactionAsync();
        await secretService.ResetPasswordUsingMasterKeyAsync(masterKey, request.PasswordReply, request.ZkKeys);
        await shamirRecoveryService.EndRecoveryModeAfterResetAsync(odinContext);
        tx.Commit();

        logger.LogInformation("Password reset with a recovery reset key");
    }

    /// <summary>
    /// After a reset with today's master key forgot the phrase copies: the owner typed the phrase in
    /// again, and the browser locked the new zero-knowledge master key with it and made a new key
    /// pair.  No password copy: the browser has no password here, so the next password login seals it.
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
        await zeroKnowledgeKeyService.SavePhraseCopiesAsync(request, phrasePublicKeyEncryptedPassword: null, odinContext);
    }
}
