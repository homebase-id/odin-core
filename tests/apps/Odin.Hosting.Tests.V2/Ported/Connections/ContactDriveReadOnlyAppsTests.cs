#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version21tov22;
using Odin.Services.Contacts;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Chat, Feed and Mail hold only Read on the ContactDrive, so no client of theirs can write a contact file
/// directly and wipe the per-app data <see cref="ContactService"/> keeps; they write through the contact API
/// (#1614).  A new identity is registered that way, and v21 → v22 rewrites an older identity's grants.
/// </summary>
[TestFixture]
public class ContactDriveReadOnlyAppsTests : V2Fixture
{
    private static readonly TargetDrive ContactDrive = WellKnownAppDrives.ContactDrive;

    private static IEnumerable<Guid> BuiltinContactReaders()
    {
        yield return SystemAppConstants.ChatAppId;
        yield return SystemAppConstants.FeedAppId;
    }

    [Test, TestCaseSource(nameof(BuiltinContactReaders))]
    public async Task ABuiltinApp_CannotUploadToTheContactDrive_ButSavesThroughTheContactApi(Guid appId)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.ForRegisteredAppAsync(owner, appId);

        var upload = await app.Drives.Writer.UploadNewMetadata(ContactDrive.Alias,
            SampleMetadataData.Create(fileType: ContactService.ContactFileType));
        Assert.That(upload.IsSuccessStatusCode, Is.False,
            $"a direct upload to the ContactDrive should be refused, got {upload.StatusCode}");

        var create = await new V2ContactsClient(app.Identity, app.Factory).CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the contact API is the app's way to write a contact");
    }

    [Test]
    public async Task TheUpgrade_LocksChatFeedAndMailToRead_AndGivesFeedManageContacts()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var (scope, ctx) = await MigrationContextAsync(owner);
        var apps = scope.Resolve<AppRegistrationService>();

        // A v21 identity: Chat and Feed write the drive, Feed has no ManageContacts, and an old Mail registration.
        await RewindAsync(apps, SystemAppConstants.ChatAppId, ctx, dropManageContacts: false);
        await RewindAsync(apps, SystemAppConstants.FeedAppId, ctx, dropManageContacts: true);
        await owner.Admin.RegisterApp(SystemAppConstants.MailAppId, new PermissionSetGrantRequest
        {
            Drives = [Grant(ContactDrive, DrivePermission.ReadWrite | DrivePermission.React)],
            PermissionSet = new PermissionSet(PermissionKeys.ManageContacts)
        });
        var chatBefore = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);

        var migration = scope.Resolve<V21ToV22VersionMigrationService>();
        Assert.ThrowsAsync<OdinSystemException>(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None),
            "validation must not pass while an app can write the ContactDrive");

        await migration.LockContactDriveToReadAsync(ctx, CancellationToken.None);
        await migration.LockContactDriveToReadAsync(ctx, CancellationToken.None);
        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));

        var chat = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(ContactPermission(chat!), Is.EqualTo(DrivePermission.Read));
        Assert.That(chat!.Grant.DriveGrants.Where(g => g.PermissionedDrive.Drive != ContactDrive).Select(g => g.PermissionedDrive),
            Is.EquivalentTo(chatBefore!.Grant.DriveGrants.Where(g => g.PermissionedDrive.Drive != ContactDrive).Select(g => g.PermissionedDrive)),
            "the other drive grants are kept");
        Assert.That(chat.Grant.PermissionSet.Keys, Is.EquivalentTo(chatBefore.Grant.PermissionSet.Keys), "the keys are kept");

        var feed = await apps.GetAppRegistration(SystemAppConstants.FeedAppId, ctx);
        Assert.That(ContactPermission(feed!), Is.EqualTo(DrivePermission.Read));
        Assert.That(feed!.Grant.PermissionSet.HasKey(PermissionKeys.ManageContacts), Is.True, "Feed saves contacts through the API");

        var mail = await apps.GetAppRegistration(SystemAppConstants.MailAppId, ctx);
        Assert.That(ContactPermission(mail!), Is.EqualTo(DrivePermission.Read), "React goes with Write");
    }

    /// <summary>Puts a provisioned app back to its v21 grant: ReadWrite on the ContactDrive, and for Feed no ManageContacts.</summary>
    private static async Task RewindAsync(AppRegistrationService apps, Guid appId, IOdinContext ctx, bool dropManageContacts)
    {
        var app = await apps.GetAppRegistration(appId, ctx);
        Assert.That(ContactPermission(app!), Is.EqualTo(DrivePermission.Read), "precondition: a new identity's app holds Read");

        await apps.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
        {
            AppId = appId,
            PermissionSet = new PermissionSet(app!.Grant.PermissionSet.Keys
                .Where(k => !dropManageContacts || k != PermissionKeys.ManageContacts).ToList()),
            Drives = app.Grant.DriveGrants
                .Select(g => g.PermissionedDrive.Drive == ContactDrive
                    ? Grant(ContactDrive, DrivePermission.ReadWrite)
                    : new DriveGrantRequest { PermissionedDrive = g.PermissionedDrive })
                .ToList()
        }, ctx);
    }

    private static DrivePermission ContactPermission(RedactedAppRegistration app) =>
        app.Grant.DriveGrants.Single(g => g.PermissionedDrive.Drive == ContactDrive).PermissionedDrive.Permission;

    private static DriveGrantRequest Grant(TargetDrive drive, DrivePermission permission) => new()
    {
        PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission }
    };
}
