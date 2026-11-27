#nullable enable
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Configuration.VersionUpgrade.Version21tov22;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// v21 -&gt; v22 (#2136): a WebDrop drive made before BlockEnumeration existed stores it off, though its built-in
/// definition already blocks enumeration.  The upgrade stores what the drive does.
/// </summary>
[TestFixture]
public class WebDropBlockEnumerationMigrationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    private static readonly TargetDrive WebDrop = WellKnownAppDrives.WebDropDrive;

    [Test]
    public async Task AWebDropDriveStoredListable_StoresBlockEnumeration_AndStaysUnlistable()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (scope, ctx) = await MigrationContextAsync(frodo);
        var driveManager = scope.Resolve<DriveManager>();

        // As an older identity has it: the option stored off
        await driveManager.StoreBlockEnumerationAsync(WebDrop.Alias, false, ctx);
        Assert.That(await StoredAsync(driveManager), Is.False, "precondition: stored off, as before the option");
        Assert.That((await driveManager.GetDriveAsync(WebDrop.Alias))!.BlockEnumeration, Is.True,
            "precondition: the built-in definition still blocks it");

        var migration = scope.Resolve<V21ToV22VersionMigrationService>();
        Assert.ThrowsAsync<OdinSystemException>(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None),
            "validation must not pass while a WebDrop drive stores it off");

        await migration.StoreWebDropBlockEnumerationAsync(ctx, CancellationToken.None);
        await migration.StoreWebDropBlockEnumerationAsync(ctx, CancellationToken.None);

        Assert.That(await StoredAsync(driveManager), Is.True, "the stored value is what the drive does");
        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));

        var anonymous = await Host.AnonymousRefitFor<IDriveQueryHttpClientApiV2>(Identities.Frodo).GetBatch(WebDrop.Alias,
            new QueryBatchRequest
            {
                QueryParams = new FileQueryParamsV1 { TargetDrive = WebDrop },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10 }
            });
        Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "anonymous still can't list it");
    }

    // The stored value, not the getter, which answers with the built-in definition
    private static async Task<bool> StoredAsync(DriveManager driveManager) =>
        (await driveManager.GetDriveAsync(WebDrop.Alias))!.Data.BlockEnumeration;
}
