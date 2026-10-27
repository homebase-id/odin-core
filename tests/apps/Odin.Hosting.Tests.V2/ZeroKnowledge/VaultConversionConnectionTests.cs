#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Services.Apps.Builtin;
using Odin.Services.Drives;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkAppSteps;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Converting Vault takes its key off the grants connections hold through the Vault circle, whether the connection
/// exists or is still a sent request carrying its own copy (docs/zero-knowledge-drive-api-plan.md, 3.3).
/// </summary>
[TestFixture]
public class VaultConversionConnectionTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [TestCase(false, TestName = "AConnectionsVaultCircleGrantLosesTheKey")]
    [TestCase(true, TestName = "ASentRequestsVaultCircleGrantLosesTheKeyBeforeItIsAccepted")]
    public async Task AVaultCircleGrantLosesTheKeyAndKeepsItsPermission(bool convertWhileTheRequestIsPending)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);

        var send = await frodo.Connections.SendConnectionRequest(sam.Identity, [BuiltinCircles.VaultCircle.Id]);
        Assert.That(send.IsSuccessStatusCode, Is.True, "arrange: sending the request");

        if (convertWhileTheRequestIsPending)
        {
            await ConvertVaultAsync(frodo, zkMasterKey);
        }

        var accept = await sam.Connections.AcceptConnectionRequest(frodo.Identity);
        Assert.That(accept.IsSuccessStatusCode, Is.True, "arrange: accepting the request");

        if (!convertWhileTheRequestIsPending)
        {
            var before = await VaultGrantAsync(frodo, sam);
            Assert.That(before.HasStorageKey, Is.True, "arrange: the circle grant carries the key, before");
            await ConvertVaultAsync(frodo, zkMasterKey);
        }

        var grant = await VaultGrantAsync(frodo, sam);
        Assert.That(grant.HasStorageKey, Is.False, "no copy of the key left on the circle grant");
        Assert.That(grant.PermissionedDrive.Permission, Is.EqualTo(DrivePermission.ReadWrite), "the permission stays");
    }

    private static async Task<Odin.Services.Authorization.ExchangeGrants.RedactedDriveGrant> VaultGrantAsync(OwnerSession frodo, OwnerSession sam)
    {
        var info = await frodo.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(info.StatusCode, Is.EqualTo(HttpStatusCode.OK), info.Error?.Content);
        return info.Content!.AccessGrant.CircleGrants.SelectMany(c => c.DriveGrants).Single(g => g.PermissionedDrive.Drive == Vault);
    }
}
