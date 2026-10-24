#nullable enable
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
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

    /// <summary>The login's takeover: the phrase handed over once, the browser's copies back.</summary>
    public static async Task<string> TakeOverPhraseAsync(OwnerSession owner, byte[] zkMasterKey)
    {
        var handover = await Client(owner).HandOverPhrase();
        Assert.That(handover.StatusCode, Is.EqualTo(HttpStatusCode.OK), handover.Error?.Content);
        var phrase = handover.Content!.Phrase;

        var response = await Client(owner).TakeOverPhrase(ZkBrowser.MakePhraseCopies(phrase, zkMasterKey));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return phrase;
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
    Task<ApiResponse<ZkPhraseHandoverResponse>> HandOverPhrase();

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/phrase-takeover")]
    Task<ApiResponse<HttpContent>> TakeOverPhrase([Body] ZkPhraseCopiesRequest request);

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/phrase-reentry")]
    Task<ApiResponse<HttpContent>> ReenterPhrase([Body] ZkPhraseCopiesRequest request);

    [Get(OwnerApiPathConstants.SecurityV1 + "/zk/session-key")]
    Task<ApiResponse<ZkSessionKeyResponse>> GetSessionKey();

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/convert-shard-recovery-drive")]
    Task<ApiResponse<HttpContent>> ConvertShardRecoveryDrive([Body] ZkConvertDriveRequest request);
}

/// <summary>The security health check's phrase check.</summary>
public interface IZeroKnowledgeHealthClient
{
    [Post(OwnerApiPathConstants.SecurityRecoveryV1 + "/verify-recovery-key")]
    Task<ApiResponse<HttpContent>> VerifyRecoveryKey([Body] VerifyRecoveryKeyRequest request);
}

/// <summary>The forgot-password page's call, with no login.</summary>
public interface IZeroKnowledgeRecoveryClient
{
    [Get(OwnerApiPathConstants.AuthV1 + "/zk-phrase-recovery")]
    Task<ApiResponse<ZkPhraseRecoveryResponse>> GetPhraseRecovery();
}
