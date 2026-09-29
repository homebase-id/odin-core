#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;

namespace Odin.Hosting.Tests.V2.Ported.Authentication;

/// <summary>
/// Port of <c>OwnerApi/Authentication/AccountRecoveryTests_2</c> (class <c>AccountRecoveryTests2</c>).
/// Reading the recovery key outside its viewing window must be refused.
/// </summary>
/// <remarks>
/// <para>
/// <b>This fixture contributes zero test cases</b>, in the V1 framework and here alike: its only
/// method sits behind <c>#if I_AM_TODD</c>, a symbol nothing in the build defines. It is carried
/// across rather than deleted because "a port is a move" — deleting a disabled test during a
/// migration is a coverage decision, and this one belongs to whoever wrote the guard.
/// </para>
/// <para>
/// The original shortened the window with <c>RunBeforeAnyTests(envOverrides:)</c> setting
/// <c>Development__RecoveryKeyWaitingPeriodSeconds=1</c>. Environment variables are process-wide and
/// fixtures here run in parallel, so that becomes <see cref="ConfigOverrides"/>, which is applied to
/// this fixture's host alone. The <c>Thread.Sleep(1000)</c> inside the guarded test is carried
/// verbatim: it pairs with that one-second window, and turning it into an awaited delay would be a
/// rewrite of code no build compiles.
/// </para>
/// <para>
/// Latent defect carried, not fixed: the config key has no effect on the product today.
/// <c>PasswordKeyRecoveryService.GetWaitingPeriod</c> hard-codes
/// <c>TimeSpan.FromSeconds(RecoveryKeyWaitingPeriodSecondsForTesting)</c> under <c>DEBUG</c> and
/// fourteen days otherwise, and reads no configuration at all — so
/// <c>Development:RecoveryKeyWaitingPeriodSeconds</c> is dead. Verified by reading
/// <c>PasswordKeyRecoveryService</c>; the override is kept so the port stays a move.
/// </para>
/// </remarks>
public class AccountRecoveryTests2 : V2Fixture
{
    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>
        {
            ["Development:RecoveryKeyWaitingPeriodSeconds"] = "1"
        };

#if I_AM_TODD
    [Test]
    public async Task FailToGetAccountRecoveryKeyOutsideOfTimeWindow()
    {
        var owner = await LoginAsOwner();

        Thread.Sleep(1000); //sleep to ensure
        var response = await owner.RefitFor<ITestSecurityContextOwnerClient>().GetAccountRecoveryKey();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
#endif
}
