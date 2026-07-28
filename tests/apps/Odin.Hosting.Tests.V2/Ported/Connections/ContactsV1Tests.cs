using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Covers the V1 contact write API (<c>/api/owner/v1/contacts</c> and <c>/api/apps/v1/contacts</c>) —
/// the V1 twin of the V2 <see cref="ContactTests"/> that exists so the legacy odin-js client (blocked on
/// V2) can write contacts through the appData-preserving <see cref="ContactService"/> instead of a
/// wholesale direct-drive write. Both shells delegate to the same service as V2, so these tests focus on
/// the V1-specific concerns: that the owner and app routes/auth are wired, and — the whole reason the API
/// exists — that a core contact update through V1 does not wipe a per-app <c>appData</c> slot.
/// </summary>
[TestFixture]
public class ContactsV1Tests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly Guid ContactDriveId = SystemDriveConstants.ContactDrive.Alias;

    [Test]
    public async Task V1_OwnerCoreUpdate_PreservesAppBlob()
    {
        // The regression this whole API prevents: an app sets a per-app blob, then a core contact update
        // (carrying no appData — exactly what odin-js sends) must NOT wipe it. Every write here goes
        // through a V1 route.
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);

        var ownerContacts = new V1ContactsClient(owner.Identity, owner.Factory);
        var appContacts = new V1ContactsClient(app.Identity, app.Factory);

        // Owner creates the contact via the V1 owner route.
        var create = await ownerContacts.OwnerCreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"owner create: {create.StatusCode}");
        var uid = create.Content!.UniqueId;

        // App stamps its per-app blob via the V1 app route.
        var set = await appContacts.AppSetAppDataAsync(uid, new SetContactAppDataRequest
        {
            Content = "\"keep\"",
            VersionTag = create.Content.VersionTag
        });
        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"app set-app-data: {set.StatusCode}");

        // Owner core update via the V1 owner route — carries no appData (like odin-js).
        var current = await GetByUniqueIdAsync(owner, uid);
        var update = await ownerContacts.OwnerUpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = current!.FileMetadata.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Samwise" } }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"owner update: {update.StatusCode}");

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Samwise"), "core field updated via V1");
        Assert.That(stored.AppData, Is.Not.Null, "the per-app blob must survive a V1 core update");
        Assert.That(stored.AppData![app.AppId.ToString()], Is.EqualTo("\"keep\""),
            "a V1 core update must preserve the app blob (the whole point of routing through ContactService)");
    }

    [Test]
    public async Task V1_App_CanCreateContact_KeyedDeterministically()
    {
        // Proves the app shell + app-token auth are wired on the V1 route.
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var appContacts = new V1ContactsClient(app.Identity, app.Factory);

        var create = await appContacts.AppCreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"app create: {create.StatusCode}");
        Assert.That(create.Content!.UniqueId, Is.EqualTo(ContactService.ToContactUniqueId((OdinId)Identities.Sam)));

        var header = await GetByUniqueIdAsync(owner, create.Content.UniqueId);
        Assert.That(header, Is.Not.Null, "the app-created contact should be readable by the owner");
        Assert.That(header!.FileMetadata.AppData.FileType, Is.EqualTo(ContactService.ContactFileType));
    }

    [Test]
    public async Task V1_OwnerAppData_IsBadRequest_NoAppToStamp()
    {
        // Mirrors the V2 behavior: the owner console is not an app client, so there is no appId to stamp.
        var owner = await LoginAsOwner(Identities.Frodo);
        var ownerContacts = new V1ContactsClient(owner.Identity, owner.Factory);

        var create = await ownerContacts.OwnerCreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var set = await ownerContacts.OwnerSetAppDataAsync(create.Content!.UniqueId, new SetContactAppDataRequest
        {
            Content = "\"x\"",
            VersionTag = create.Content.VersionTag
        });
        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // -----------------------------------------------------------------------------------------
    // helpers (mirror ContactTests — reads stay client-side via the drive reader)
    // -----------------------------------------------------------------------------------------

    private static async Task<Odin.Services.Apps.SharedSecretEncryptedFileHeader> GetByUniqueIdAsync(
        OwnerSession owner, Guid uniqueId)
    {
        var reader = new DriveReaderV2Client(owner.Identity, owner.Factory);
        var resp = await reader.GetFileHeaderByUniqueIdAsync(uniqueId, ContactDriveId);
        return resp.IsSuccessStatusCode ? resp.Content : null;
    }

    private static ContactContent DecryptContent(OwnerSession owner, Odin.Services.Apps.SharedSecretEncryptedFileHeader header)
    {
        var sharedSecret = owner.SharedSecret;
        var keyHeader = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
        var plain = keyHeader.Decrypt(Convert.FromBase64String(header.FileMetadata.AppData.Content));
        return JsonSerializer.Deserialize<ContactContent>(plain.ToStringFromUtf8Bytes())!;
    }
}
