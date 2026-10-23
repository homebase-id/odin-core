#nullable enable
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.Owner;
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

    public static async Task<ZkBrowser.Keys> CreateKeysAsync(OwnerSession owner, string password, byte[] loginKeys)
    {
        var keys = ZkBrowser.MakeKeys(password, loginKeys);
        var response = await Client(owner).CreateKeys(keys.Request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return keys;
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

    [Post(OwnerApiPathConstants.SecurityV1 + "/zk/convert-shard-recovery-drive")]
    Task<ApiResponse<HttpContent>> ConvertShardRecoveryDrive([Body] ZkConvertDriveRequest request);
}
