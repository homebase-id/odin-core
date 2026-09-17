#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.AccountManagement;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Services.Authentication.Owner;

namespace Odin.Hosting.Tests.V2.Ported.AccountManagement;

/// <summary>
/// Port of <c>_Universal/Owner/AccountManagement/DeleteAccountTests</c>. Marking an account for
/// deletion stamps <c>PlannedDeletionDate</c>; un-marking clears it. Both calls re-prove the
/// password, so each test computes a fresh authentication password reply against an anonymous
/// nonce.
/// </summary>
/// <remarks>
/// <para>
/// Deviations from the original, all checked:
/// <list type="bullet">
/// <item>The original called <c>OldOwnerApi.SetupOwnerAccount</c> with its own password
/// (<c>8833CC039d!!~!</c>) because <c>WebScaffold</c> shares an already-provisioned identity. Here
/// the fixture's own login sets the password, so the reply is computed against
/// <see cref="OwnerLogin.DefaultPassword"/>. The password value is incidental — what is under test
/// is that the endpoint accepts a correct one.</item>
/// <item>The original used Frodo for the mark test and TomBombadil for the unmark test, because
/// deleting an account is one-way within a shared <c>WebScaffold</c> run. Per-test reset removes
/// that constraint, so both run as the fixture default; a second identity would cost a tenant
/// materialisation and a reset per test for nothing.</item>
/// <item><c>DeleteAccount</c> / <c>UndeleteAccount</c> / <c>GetAccountStatus</c> are the system
/// under test, so they go through <see cref="OwnerSession.RefitFor{T}"/> rather than
/// <c>owner.Admin</c>.</item>
/// <item><c>PlannedDeletionDate &gt; UnixTimeUtc.Now()</c> is compared on <c>.milliseconds</c>:
/// <see cref="UnixTimeUtc"/> is not <c>IComparable</c>, so <c>Is.GreaterThan</c> on the struct
/// itself compiles and throws at run time.</item>
/// </list>
/// </para>
/// <para>No caller matrix in the original and none added; <c>SetupCallerWithOwner</c> is not in play.</para>
/// </remarks>
[TestFixture]
public class DeleteAccountTests : V2Fixture
{
    [Test]
    public async Task CanMarkAccountForDeletion()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerAccountManagement>();

        var deleteAccountResponse = await svc.DeleteAccount(await BuildDeleteRequestAsync(owner));
        Assert.That(deleteAccountResponse.IsSuccessStatusCode, Is.True);

        var getStatusResponse = await svc.GetAccountStatus();
        Assert.That(getStatusResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getStatusResponse.Content!.PlannedDeletionDate, Is.Not.Null);
        Assert.That(getStatusResponse.Content.PlannedDeletionDate!.Value.milliseconds,
            Is.GreaterThan(UnixTimeUtc.Now().milliseconds));
    }

    [Test]
    public async Task CanUnmarkAccountForDeletion()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitOwnerAccountManagement>();

        //setup for delete
        var deleteAccountResponse = await svc.DeleteAccount(await BuildDeleteRequestAsync(owner));
        Assert.That(deleteAccountResponse.IsSuccessStatusCode, Is.True);

        // make sure we're set to delete
        var getStatusResponse = await svc.GetAccountStatus();
        Assert.That(getStatusResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getStatusResponse.Content!.PlannedDeletionDate, Is.Not.Null);
        Assert.That(getStatusResponse.Content.PlannedDeletionDate!.Value.milliseconds,
            Is.GreaterThan(UnixTimeUtc.Now().milliseconds));

        var unmarkAccountResponse = await svc.UndeleteAccount(await BuildDeleteRequestAsync(owner));
        Assert.That(unmarkAccountResponse.IsSuccessStatusCode, Is.True);

        var getStatusResponse2 = await svc.GetAccountStatus();
        Assert.That(getStatusResponse2.IsSuccessStatusCode, Is.True);
        Assert.That(getStatusResponse2.Content!.PlannedDeletionDate, Is.Null);
    }

    /// <summary>
    /// The anonymous half of the password dance, via <see cref="OwnerPasswordFlow"/>: pull a fresh
    /// authentication nonce off the identity and fold the password into it. Mirrors
    /// <c>OwnerApiTestUtils.CalculateAuthenticationPasswordReply</c>, which the original reached
    /// through <c>OwnerAccountManagementApiClient</c>.
    /// </summary>
    private async Task<DeleteAccountRequest> BuildDeleteRequestAsync(OwnerSession owner)
    {
        using var client = Host.CreateAnonymousClient(owner.Identity.DomainName);
        var eccKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        return new DeleteAccountRequest
        {
            CurrentAuthenticationPasswordReply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(
                client, OwnerLogin.DefaultPassword, eccKey)
        };
    }
}
