#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Drives;
using Odin.Services.Security.ZeroKnowledge;
using Refit;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// What the owner console's browser does with the zero-knowledge endpoints, for tests that need the
/// keys or a converted Shard Recovery drive as a precondition.
/// </summary>
internal static class ZkOwnerSteps
{
    public static IZeroKnowledgeOwnerClient Client(OwnerSession owner) => owner.RefitFor<IZeroKnowledgeOwnerClient>();

    public static async Task<ZkStatusResponse> GetStatusAsync(OwnerSession owner)
    {
        var response = await Client(owner).GetStatus();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return response.Content!;
    }

    /// <summary>Where the browser looks to see whether the drive is converted.</summary>
    public static async Task<OwnerClientDriveData> GetShardRecoveryDriveAsync(OwnerSession owner) =>
        (await owner.Admin.GetDrives()).Single(d => d.TargetDriveInfo == WellKnownAppDrives.ShardRecoveryDrive);

    public static async Task<ZkBrowser.Keys> CreateKeysAsync(OwnerSession owner, string password)
    {
        var keys = ZkBrowser.MakeKeys(password);
        var response = await Client(owner).CreateKeys(keys.Request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return keys;
    }

    /// <summary>Asks for the phrase with proof of <paramref name="password"/>, as the login does.</summary>
    public static async Task<ApiResponse<ZkPhraseHandoverResponse>> HandOverPhraseAsync(OwnerSession owner, string password)
    {
        return await Client(owner).HandOverPhrase(new ZkPhraseHandoverRequest
        {
            CurrentAuthenticationPasswordReply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(
                owner.Host, owner.Identity.DomainName, password)
        });
    }

    /// <summary>Writes the password copies given, with proof of <paramref name="password"/>, as the login does.</summary>
    public static async Task<ApiResponse<HttpContent>> SavePasswordAsync(OwnerSession owner, string password,
        string? phraseCopy, string? friendsCopy = null)
    {
        return await Client(owner).SavePassword(new ZkPasswordRequest
        {
            PhrasePublicKeyEncryptedPassword = phraseCopy,
            ShamirPublicKeyEncryptedPassword = friendsCopy,
            CurrentAuthenticationPasswordReply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(
                owner.Host, owner.Identity.DomainName, password)
        });
    }

    /// <summary>
    /// The takeover's request, as the login builds it: the copies, <paramref name="password"/> sealed
    /// to the new public key, and proof of <paramref name="password"/>.
    /// </summary>
    public static async Task<ZkPhraseTakeoverRequest> MakeTakeoverAsync(OwnerSession owner, string password,
        string phrase, byte[] zkMasterKey)
    {
        var request = ZkBrowser.MakePhraseCopies<ZkPhraseTakeoverRequest>(phrase, zkMasterKey);
        request.PhrasePublicKeyEncryptedPassword = ZkBrowser.SealPassword(password, request.PhrasePublicKeyJwk);
        request.CurrentAuthenticationPasswordReply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(
            owner.Host, owner.Identity.DomainName, password);
        return request;
    }

    /// <summary>The login's takeover: the phrase handed over once, the browser's copies back.</summary>
    public static async Task<string> TakeOverPhraseAsync(OwnerSession owner, string password, byte[] zkMasterKey)
    {
        var handover = await HandOverPhraseAsync(owner, password);
        Assert.That(handover.StatusCode, Is.EqualTo(HttpStatusCode.OK), handover.Error?.Content);
        var phrase = handover.Content!.Phrase;

        var response = await Client(owner).TakeOverPhrase(await MakeTakeoverAsync(owner, password, phrase, zkMasterKey));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return phrase;
    }

    /// <summary>
    /// What the first login since plan A does: the zero-knowledge keys, and the Shard Recovery drive
    /// converted.  Returns what the browser then holds.
    /// </summary>
    public static async Task<(byte[] ZkMasterKey, byte[] WriteOnlyPrivateKey, byte[] DriveKey)> MakeZeroKnowledgeAsync(
        OwnerSession owner, string password)
    {
        var keys = await CreateKeysAsync(owner, password);
        var driveKeys = await ConvertShardRecoveryDriveAsync(owner, keys.MasterKey);
        return (keys.MasterKey, driveKeys.WriteOnlyPrivateKey, driveKeys.DriveKey);
    }

    public static async Task<ZkBrowser.DriveKeys> ConvertShardRecoveryDriveAsync(OwnerSession owner, byte[] zkMasterKey)
    {
        var driveKeys = ZkBrowser.MakeDriveKeys(WellKnownAppDrives.ShardRecoveryDrive.Alias, zkMasterKey);
        var response = await Client(owner).ConvertShardRecoveryDrive(driveKeys.Request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return driveKeys;
    }
}

public interface IZeroKnowledgeOwnerClient
{
    [Get(OwnerApiPathConstants.SecurityV1 + "/zk")]
    Task<ApiResponse<ZkStatusResponse>> GetStatus();

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/keys")]
    Task<ApiResponse<HttpContent>> CreateKeys([Body] ZkKeysRequest request);

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/password")]
    Task<ApiResponse<HttpContent>> SavePassword([Body] ZkPasswordRequest request);

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/phrase-handover")]
    Task<ApiResponse<ZkPhraseHandoverResponse>> HandOverPhrase([Body] ZkPhraseHandoverRequest request);

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/phrase-takeover")]
    Task<ApiResponse<HttpContent>> TakeOverPhrase([Body] ZkPhraseTakeoverRequest request);

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/phrase-reentry")]
    Task<ApiResponse<HttpContent>> ReenterPhrase([Body] ZkPhraseCopiesRequest request);

    [Get(OwnerApiPathConstants.SecurityV1 + "/zk/session-key")]
    Task<ApiResponse<ZkSessionKeyResponse>> GetSessionKey();

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/convert-shard-recovery-drive")]
    Task<ApiResponse<HttpContent>> ConvertShardRecoveryDrive([Body] ZkConvertDriveRequest request);

    [Get(OwnerApiPathConstants.SecurityV1 + "/zk/app-keys")]
    Task<ApiResponse<List<ZkAppKeysResponse>>> GetAppKeys();

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/app-keys")]
    Task<ApiResponse<HttpContent>> SaveAppKeys([Body] ZkAppKeysRequest request);

    [Get(OwnerApiPathConstants.SecurityV1 + "/zk/drives/{driveId}/key-for-conversion")]
    Task<ApiResponse<ZkDriveKeyResponse>> GetDriveKeyForConversion(Guid driveId);

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/convert-drive")]
    Task<ApiResponse<HttpContent>> ConvertDrive([Body] ZkConvertDriveKeepingKeyRequest request);
}
