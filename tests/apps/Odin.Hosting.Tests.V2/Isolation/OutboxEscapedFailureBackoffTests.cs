#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Tests.V2.Isolation;

/// <summary>
/// A failure an outbox worker lets escape is retried on the attempt back-off, not at once (#1815).  Before, the
/// processor rescheduled it for "now", so all of <c>OutboxOperationMaxAttempts</c> went in well under a second.
/// </summary>
/// <remarks>
/// The item is a push notification whose stored data isn't JSON: its worker throws while reading it, and nothing in
/// the worker catches that.  Drained one pass at a time with no bring-forward, so it runs again only if the outbox
/// itself considers it due.
/// </remarks>
[TestFixture]
public class OutboxEscapedFailureBackoffTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
        ["Unhandled exception occured while processing an outbox"];

    [Test]
    public async Task AnEscapedFailure_IsRetriedOnTheBackoff_NotImmediately()
    {
        var tenant = Host.GetTenantScope(Identities.Frodo);
        var file = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.NewGuid() };
        var frodo = new OdinId(Identities.Frodo);
        await OutboxBackoff.Resolve(tenant).Outbox.AddItemAsync(new OutboxFileItem
        {
            Recipient = frodo,
            Priority = 100,
            Type = OutboxItemType.PushNotification,
            File = file,
            State = new OutboxItemState { Data = "{ not json".ToUtf8ByteArray() }
        });

        await OutboxBackoff.AssertFailedOnceAndParkedAsync(tenant, file.DriveId, file.FileId, frodo);
    }
}
