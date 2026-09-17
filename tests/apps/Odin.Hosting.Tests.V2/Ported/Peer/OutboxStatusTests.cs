using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Outbox/OutboxStatusTests.cs
///
/// Who may read a drive's outbox status, and what it says once a send has been delivered.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>The one live caller matrix in the outbox set, carried whole: Guest[Write] is refused with
/// <c>NotFound</c>, App[Write] and Owner both read it.</item>
/// <item>The seed is <b>not</b> gated on <c>expected == OK</c>: the drive status endpoint is the
/// system under test for every row, including the refused one, and the Guest row's refusal is only
/// meaningful against a drive that exists and has been sent from.</item>
/// <item><c>SetupCallerWithOwner</c> is not used — the drive must exist on both identities and the
/// connection must be in place before the caller is built, exactly as the original ordered it
/// (<c>PrepareScenario</c> → upload → wait → <c>callerContext.Initialize</c>).</item>
/// <item><c>WaitForEmptyOutbox</c> becomes <c>Sync.DrainOutboxAsync()</c> — the V1 call is a passive
/// poll on the outbox background service, which the fast host registers but never starts. The
/// original's comment beside it ("the outbox processes superfast, so we probably need to load it up
/// with a bunch of items and not wait on it") and the two assertion messages saying the same thing
/// describe a race this framework does not have: the drain is synchronous, so the outbox is
/// genuinely empty by the time status is read. The assertions are unchanged; their messages are
/// dropped, since NUnit prints both sides and the note no longer applies.</item>
/// <item>The <c>[SetUp]</c>/<c>[TearDown]</c> pair only cleared and re-asserted <c>WebScaffold</c>
/// log events; that lifecycle belongs to the scaffold and is gone.</item>
/// <item>Trailing <c>DeleteScenario</c> calls were cleanup only and are dropped — per-test reset
/// covers them.</item>
/// </list>
/// </remarks>
[TestFixture]
public class OutboxStatusTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    public static IEnumerable<object[]> StatusCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.NotFound];
        yield return [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(StatusCases))]
    public async Task DriveStatusReportsResult(CallerSpec spec, HttpStatusCode expected)
    {
        var senderOwnerClient = await LoginAsOwner(Identities.Frodo);
        var recipientOwnerClient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Write;

        var targetDrive = spec.TargetDrive;
        await OutboxScenario.PrepareAsync(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipientOwnerClient.Identity.DomainName],
            RemoteTargetDrive = default
        };

        var (uploadResult, _, _) = await PeerTransferScenario.UploadEncryptedMetadataAsync(
            senderOwnerClient, targetDrive, transitOptions);

        Assert.That(uploadResult.RecipientStatus[recipientOwnerClient.Identity], Is.EqualTo(TransferStatus.Enqueued));

        var caller = await spec.Build(senderOwnerClient);
        var getStatusResponse = await caller.V1.Drive.GetDriveStatus(targetDrive);
        Assert.That(getStatusResponse.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var status = getStatusResponse.Content;
        Assert.That(status.Outbox.TotalItems, Is.EqualTo(0));
        Assert.That(status.Outbox.CheckedOutCount, Is.EqualTo(0));
    }
}
