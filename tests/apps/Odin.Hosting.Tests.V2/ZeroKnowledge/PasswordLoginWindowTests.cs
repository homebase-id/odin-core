#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// The phrase handover, the takeover and the password copy are accepted only right after a password
/// login (<c>ZeroKnowledgeKeyService.AssertRecentPasswordLogin</c>), so a session token stolen later
/// can't take the phrase over.  <c>Development:PasswordLoginWindowSeconds</c> shortens the 5 minutes
/// to one second here.
/// </summary>
[TestFixture]
public class PasswordLoginWindowTests : V2Fixture
{
    private const int WindowSeconds = 1;

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>
        {
            ["Development:PasswordLoginWindowSeconds"] = WindowSeconds.ToString()
        };

    [Test]
    public async Task AnOlderSessionCannotTakeThePhraseOverButAFreshLoginCan()
    {
        var stale = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(stale, OwnerLogin.DefaultPassword);
        await Task.Delay((WindowSeconds + 1) * 1000);

        var client = ZkOwnerSteps.Client(stale);
        DriveAsserts.AssertRejected(await client.HandOverPhrase(), OdinClientErrorCode.PasswordLoginRequired);
        DriveAsserts.AssertRejected(await client.TakeOverPhrase(ZkBrowser.MakePhraseCopies(
            Odin.Core.Cryptography.Crypto.BIP39Util.GenerateBIP39(new byte[16]), keys.MasterKey)), OdinClientErrorCode.PasswordLoginRequired);
        DriveAsserts.AssertRejected(await client.SavePassword(new ZkPasswordRequest
        {
            ZkMasterKeyEncryptedPassword = ZkBrowser.LockPassword("stolen session", keys.MasterKey)
        }), OdinClientErrorCode.PasswordLoginRequired);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(stale)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.OnServer),
            "the refusals took nothing over");

        var fresh = await LoginAsOwner();
        var handover = await ZkOwnerSteps.Client(fresh).HandOverPhrase();
        Assert.That(handover.StatusCode, Is.EqualTo(HttpStatusCode.OK), handover.Error?.Content);
    }
}
