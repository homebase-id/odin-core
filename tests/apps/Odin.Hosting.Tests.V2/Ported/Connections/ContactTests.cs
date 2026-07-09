using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.PublicPage.Profile;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Covers the server-side V2 Contact write API (<c>/api/v2/contacts</c>): upsert (create + in-place
/// update with optimistic concurrency), delete, and sync. Each write scenario runs against both the
/// Owner caller and an App caller granted <see cref="PermissionKeys.ManageContacts"/>, and asserts the
/// stored contact file via the owner's drive reader (reads stay client-side — there is no contact read
/// API). Also asserts that an app <i>without</i> <c>ManageContacts</c> is forbidden.
/// </summary>
[TestFixture]
public class ContactTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly Guid ContactDriveId = SystemDriveConstants.ContactDrive.Alias;

    public enum CallerKind
    {
        Owner,
        App
    }

    public static IEnumerable<object[]> AllowedCallers()
    {
        yield return [CallerKind.Owner];
        yield return [CallerKind.App];
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task Upsert_WithOdinId_CreatesContact_KeyedDeterministically(CallerKind kind)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = await GetContactsClientAsync(owner, kind, PermissionKeyAllowance.Apps);

        var content = new ContactContent
        {
            OdinId = Identities.Sam,
            Name = new ContactName { DisplayName = "Samwise Gamgee", GivenName = "Samwise" },
            Email = new ContactEmail { Email = "sam@shire.example" }
        };

        var response = await contacts.CreateAsync(new CreateContactRequest { Content = content });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");

        var expectedUid = ContactService.ToContactUniqueId((OdinId)Identities.Sam);
        Assert.That(response.Content!.UniqueId, Is.EqualTo(expectedUid));
        Assert.That(response.Content.VersionTag, Is.Not.EqualTo(Guid.Empty));

        var header = await GetByUniqueIdAsync(owner, expectedUid);
        Assert.That(header, Is.Not.Null, "contact file should exist on the contact drive");
        Assert.That(header!.FileMetadata.IsEncrypted, Is.True, "contact content must be encrypted at rest");
        Assert.That(header.FileMetadata.AppData.FileType, Is.EqualTo(ContactService.ContactFileType));
        Assert.That(header.FileMetadata.AppData.UniqueId, Is.EqualTo(expectedUid));
        Assert.That(header.FileMetadata.AppData.Tags, Does.Contain(expectedUid), "should be tagged with the identity key");

        var stored = DecryptContent(owner, header);
        Assert.That(stored.OdinId, Is.EqualTo(Identities.Sam));
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Samwise Gamgee"));
        Assert.That(stored.Email!.Email, Is.EqualTo("sam@shire.example"));
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task Upsert_SameOdinId_UpdatesInPlaceAndMerges_NoDuplicate(CallerKind kind)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = await GetContactsClientAsync(owner, kind, PermissionKeyAllowance.Apps);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent
            {
                OdinId = Identities.Sam,
                Name = new ContactName { DisplayName = "Sam", GivenName = "Samwise" }
            }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        // Update with the version tag we just read; only set a new field — the merge must keep the
        // existing givenName rather than clobber it.
        var update = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = create.Content.VersionTag,
            Content = new ContactContent
            {
                OdinId = Identities.Sam,
                Email = new ContactEmail { Email = "sam@shire.example" }
            }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(update.Content!.UniqueId, Is.EqualTo(uid), "update must land on the same file");
        Assert.That(update.Content.VersionTag, Is.Not.EqualTo(create.Content.VersionTag), "version tag should advance");

        // Exactly one contact file exists for this identity (no duplicate).
        var count = await CountContactsAsync(owner);
        Assert.That(count, Is.EqualTo(1));

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        Assert.That(stored.Email!.Email, Is.EqualTo("sam@shire.example"), "new field applied");
        Assert.That(stored.Name!.GivenName, Is.EqualTo("Samwise"), "existing field preserved by merge");
    }

    [Test]
    public async Task Upsert_WithStaleVersionTag_Returns409ThenRetrySucceeds()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        // A stale version tag on an update to an existing contact is rejected (no silent overwrite).
        var conflict = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = Guid.NewGuid(), // not the current tag
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Changed" } }
        });
        Assert.That(conflict.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        // Client recovery: re-read the current version tag from the drive and retry.
        var current = await GetByUniqueIdAsync(owner, uid);
        Assert.That(current!.FileMetadata.VersionTag, Is.EqualTo(create.Content.VersionTag),
            "the conflicting write must not have advanced the stored version");

        var retry = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = current.FileMetadata.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Changed" } }
        });
        Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(retry.Content!.VersionTag, Is.Not.EqualTo(create.Content.VersionTag));

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Changed"));
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task Upsert_WithoutOdinId_CreatesRandomKeyedContact(CallerKind kind)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = await GetContactsClientAsync(owner, kind, PermissionKeyAllowance.Apps);

        var response = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { Name = new ContactName { DisplayName = "Barliman Butterbur" } }
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.UniqueId, Is.Not.EqualTo(Guid.Empty));

        var header = await GetByUniqueIdAsync(owner, response.Content.UniqueId);
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.FileType, Is.EqualTo(ContactService.ContactFileType));
        Assert.That(header.FileMetadata.AppData.Tags ?? new List<Guid>(), Is.Empty,
            "a no-odinId contact carries no identity tag");

        var stored = DecryptContent(owner, header);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Barliman Butterbur"));
        Assert.That(stored.OdinId, Is.Null);
    }

    [Test]
    public async Task Create_ExistingOdinId_Returns409()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var first = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // A second create for the same odinId collides on the deterministic uniqueId → 409 (update it).
        var dup = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam again" } }
        });
        Assert.That(dup.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await CountContactsAsync(owner), Is.EqualTo(1), "no duplicate file was created");
    }

    [Test]
    public async Task Update_NonExistent_Returns404()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var update = await contacts.UpdateAsync(Guid.NewGuid(), new UpdateContactRequest
        {
            VersionTag = Guid.NewGuid(),
            Content = new ContactContent { Name = new ContactName { DisplayName = "Nobody" } }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task Delete_RemovesContact(CallerKind kind)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = await GetContactsClientAsync(owner, kind, PermissionKeyAllowance.Apps);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var delete = await contacts.DeleteAsync(uid);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var count = await CountContactsAsync(owner);
        Assert.That(count, Is.EqualTo(0), "deleted contact should not surface in an active query");

        // Deleting again is a 404 — there's no active file left.
        var deleteAgain = await contacts.DeleteAsync(uid);
        Assert.That(deleteAgain.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_NonExistent_Returns404()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var delete = await contacts.DeleteAsync(Guid.NewGuid());
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task Sync_Returns202(CallerKind kind)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = await GetContactsClientAsync(owner, kind, PermissionKeyAllowance.Apps);

        var response = await contacts.SyncAsync(Identities.Sam);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    [Test]
    public async Task Source_RoundTripsAndIsPreservedAcrossUpdate()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent
            {
                OdinId = Identities.Sam,
                Source = "user",
                Name = new ContactName { DisplayName = "Sam" }
            }
        });
        var uid = create.Content!.UniqueId;
        Assert.That(DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!).Source, Is.EqualTo("user"));

        // An update that omits source must not clear it.
        var update = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = create.Content.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Email = new ContactEmail { Email = "sam@shire.example" } }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!).Source, Is.EqualTo("user"), "source preserved");

        // A later update can change it.
        var current = await GetByUniqueIdAsync(owner, uid);
        var update2 = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = current!.FileMetadata.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Source = "public" }
        });
        Assert.That(update2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!).Source, Is.EqualTo("public"));
    }

    // -----------------------------------------------------------------------------------------
    // serialized shape (odin-js byte-compat)
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task Contact_FullProductionShape_RoundTrips()
    {
        // Mirrors real production data (Michael Tefo) — every field populated must round-trip.
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent
            {
                OdinId = "id.example.com",
                Source = "contact",
                Name = new ContactName { DisplayName = "Michael Tefo", GivenName = "Michael", Surname = "Tefo" },
                Location = new ContactLocation { City = "Vegas", Country = "Denmark" },
                Phone = new ContactPhone { Number = "+45 11 11 11 11" },
                Email = new ContactEmail { Email = "tefo@example.com" },
                Birthday = new ContactBirthday { Date = "2025-02-20" }
            }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, create.Content!.UniqueId))!);
        Assert.That(stored.OdinId, Is.EqualTo("id.example.com"));
        Assert.That(stored.Source, Is.EqualTo("contact"));
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Michael Tefo"));
        Assert.That(stored.Name.GivenName, Is.EqualTo("Michael"));
        Assert.That(stored.Name.Surname, Is.EqualTo("Tefo"));
        Assert.That(stored.Location!.City, Is.EqualTo("Vegas"));
        Assert.That(stored.Location.Country, Is.EqualTo("Denmark"));
        Assert.That(stored.Phone!.Number, Is.EqualTo("+45 11 11 11 11"));
        Assert.That(stored.Email!.Email, Is.EqualTo("tefo@example.com"));
        Assert.That(stored.Birthday!.Date, Is.EqualTo("2025-02-20"));
    }

    [Test]
    public async Task Contact_EmptyValueObjects_AlwaysEmitted_AcrossCreateAndUpdate()
    {
        // Mirrors real production data (Peter Parker): location/phone/email/birthday are present even
        // when empty. We create WITHOUT them and they must still be emitted, and survive an update.
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent
            {
                OdinId = "peter.parker.demo.rocks",
                Source = "contact",
                Name = new ContactName { DisplayName = "Peter Parker", GivenName = "Peter", Surname = "Parker" }
            }
        });
        var uid = create.Content!.UniqueId;
        AssertFourEmitted(DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!));

        var update = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = create.Content.VersionTag,
            Content = new ContactContent { OdinId = "peter.parker.demo.rocks", Name = new ContactName { DisplayName = "Spider-Man" } }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        AssertFourEmitted(stored);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Spider-Man"));
    }

    [Test]
    public async Task Contact_Update_PreservesFullData()
    {
        // Existing full-data contact must not lose fields when only one is updated.
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent
            {
                OdinId = "id.example.com",
                Source = "contact",
                Name = new ContactName { DisplayName = "Michael Tefo", GivenName = "Michael", Surname = "Tefo" },
                Location = new ContactLocation { City = "Vegas", Country = "Denmark" },
                Phone = new ContactPhone { Number = "+45 11 11 11 11" },
                Email = new ContactEmail { Email = "tefo@example.com" },
                Birthday = new ContactBirthday { Date = "2025-02-20" }
            }
        });
        var uid = create.Content!.UniqueId;

        var update = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = create.Content.VersionTag,
            Content = new ContactContent { OdinId = "id.example.com", Name = new ContactName { DisplayName = "Mike Tefo" } }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        AssertFourEmitted(stored);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Mike Tefo"), "updated field");
        Assert.That(stored.Name.GivenName, Is.EqualTo("Michael"), "untouched name sub-field preserved");
        Assert.That(stored.Location!.City, Is.EqualTo("Vegas"));
        Assert.That(stored.Location.Country, Is.EqualTo("Denmark"));
        Assert.That(stored.Phone!.Number, Is.EqualTo("+45 11 11 11 11"));
        Assert.That(stored.Email!.Email, Is.EqualTo("tefo@example.com"));
        Assert.That(stored.Birthday!.Date, Is.EqualTo("2025-02-20"));
    }

    private static void AssertFourEmitted(ContactContent c)
    {
        Assert.That(c.Location, Is.Not.Null, "location must be emitted (even when empty)");
        Assert.That(c.Phone, Is.Not.Null, "phone must be emitted (even when empty)");
        Assert.That(c.Email, Is.Not.Null, "email must be emitted (even when empty)");
        Assert.That(c.Birthday, Is.Not.Null, "birthday must be emitted (even when empty)");
    }

    // -----------------------------------------------------------------------------------------
    // merge log
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task NewContact_HasNoMergeLog()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That(await HasMergeLogAsync(owner, create.Content!.UniqueId), Is.False,
            "a freshly created contact has no merge log");
    }

    [Test]
    public async Task FirstTimeFieldFill_DoesNotLog()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        // Filling a previously-empty field is not an overwrite → no log.
        var fill = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = create.Content.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Email = new ContactEmail { Email = "sam@shire.example" } }
        });
        Assert.That(fill.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That(await HasMergeLogAsync(owner, uid), Is.False, "first-time fills must not be logged");
    }

    [Test]
    public async Task Upsert_WithEmptyValueObjects_DoesNotClobberStoredValues()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent
            {
                OdinId = Identities.Sam,
                Name = new ContactName { DisplayName = "Sam" },
                Phone = new ContactPhone { Number = "555-0100" }
            }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        // An empty value-object (phone:{}) and an empty string (displayName:"") mean "leave alone",
        // not "clear" — neither may wipe the stored value.
        var update = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = create.Content.VersionTag,
            Content = new ContactContent
            {
                OdinId = Identities.Sam,
                Name = new ContactName { DisplayName = "" },
                Phone = new ContactPhone()
            }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        Assert.That(stored.Phone!.Number, Is.EqualTo("555-0100"), "an empty phone object must not wipe the stored number");
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Sam"), "an empty displayName must not wipe the stored name");
    }

    [Test]
    public async Task FirstTimeFieldFill_RotatesContentIv()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        var ss1 = owner.SharedSecret;
        var before = (await GetByUniqueIdAsync(owner, uid))!.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss1);

        // First-time fill: takes the no-merge-log update path, which must still rotate the IV.
        var fill = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = create.Content.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Email = new ContactEmail { Email = "sam@shire.example" } }
        });
        Assert.That(fill.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var ss2 = owner.SharedSecret;
        var after = (await GetByUniqueIdAsync(owner, uid))!.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss2);

        Assert.That(ByteArrayUtil.EquiByteArrayCompare(before.AesKey.GetKey(), after.AesKey.GetKey()), Is.True,
            "the contact's AES key is stable across updates");
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(before.Iv, after.Iv), Is.False,
            "the content IV must rotate on every update — key+IV reuse across versions leaks plaintext structure");
    }

    [Test, TestCaseSource(nameof(AllowedCallers))]
    public async Task OverwritingField_AppendsMergeLogEntry(CallerKind kind)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = await GetContactsClientAsync(owner, kind, PermissionKeyAllowance.Apps);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var update = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = create.Content.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Samwise Gamgee" } }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var log = await ReadMergeLogAsync(owner, uid);
        Assert.That(log, Has.Count.EqualTo(1));
        Assert.That(log[0].By, Is.EqualTo("api"));
        Assert.That(log[0].Changes, Does.ContainKey("name.displayName"));
        Assert.That(log[0].Changes["name.displayName"], Is.EqualTo("Sam"), "the log keeps the OLD value");
        Assert.That(log[0].At, Is.GreaterThan(0));

        // New value lives in Content; only the old value is in the log.
        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Samwise Gamgee"));
    }

    [Test]
    public async Task SuccessiveOverwrites_AccumulateMergeLogEntries()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent
            {
                OdinId = Identities.Sam,
                Name = new ContactName { DisplayName = "A" },
                Email = new ContactEmail { Email = "a@shire.example" }
            }
        });
        var uid = create.Content!.UniqueId;

        var first = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = create.Content.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "B" } }
        });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var second = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = first.Content!.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Email = new ContactEmail { Email = "b@shire.example" } }
        });
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var log = await ReadMergeLogAsync(owner, uid);
        Assert.That(log, Has.Count.EqualTo(2), "each overwrite appends an entry");
        Assert.That(log[0].Changes["name.displayName"], Is.EqualTo("A"));
        Assert.That(log[1].Changes["email.email"], Is.EqualTo("a@shire.example"));

        // Still a single contact file; the log is a payload, not a new file.
        Assert.That(await CountContactsAsync(owner), Is.EqualTo(1));
    }

    // -----------------------------------------------------------------------------------------
    // profile image
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task SetImage_StoresEncryptedImageAndThumbnail()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var (image, thumb) = await SetSampleImageAsync(owner, contacts, uid, create.Content.VersionTag);

        var header = await GetByUniqueIdAsync(owner, uid);
        var descriptor = header!.FileMetadata.Payloads?.FirstOrDefault(p => p.Key == ContactService.ProfileImagePayloadKey);
        Assert.That(descriptor, Is.Not.Null, "image payload descriptor should be present");
        Assert.That(descriptor!.ContentType, Is.EqualTo("image/jpeg"));
        Assert.That(descriptor.Thumbnails, Has.Count.EqualTo(1));
        Assert.That(descriptor.Thumbnails[0].PixelWidth, Is.EqualTo(32));

        // Image bytes round-trip (decrypt with the payload IV + file key).
        Assert.That(await ReadImagePayloadAsync(owner, uid), Is.EqualTo(image));

        // Thumbnail round-trips under the SAME IV as the payload.
        var ss = owner.SharedSecret;
        var fileKey = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);
        var reader = new DriveReaderV2Client(owner.Identity, owner.Factory);
        var tResp = await reader.GetThumbnailUniqueIdAsync(uid, ContactDriveId, 32, 32,
            ContactService.ProfileImagePayloadKey, directMatchOnly: true);
        Assert.That(tResp.IsSuccessStatusCode, Is.True, "thumbnail should be readable");
        var tPlain = new KeyHeader { Iv = descriptor.Iv, AesKey = fileKey.AesKey }
            .Decrypt(await tResp.Content!.ReadAsByteArrayAsync());
        Assert.That(tPlain, Is.EqualTo(thumb));

        // The contact content is untouched.
        Assert.That(DecryptContent(owner, header).Name!.DisplayName, Is.EqualTo("Sam"));
    }

    [Test]
    public async Task UpdatingField_PreservesImage()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;
        var (image, _) = await SetSampleImageAsync(owner, contacts, uid, create.Content.VersionTag);

        // Overwrite a field → writes a merge_log payload; the image payload must be carried forward.
        var current = await GetByUniqueIdAsync(owner, uid);
        var update = await contacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = current!.FileMetadata.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Samwise" } }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var header = await GetByUniqueIdAsync(owner, uid);
        var keys = (header!.FileMetadata.Payloads ?? new List<PayloadDescriptor>()).Select(p => p.Key).ToList();
        Assert.That(keys, Does.Contain(ContactService.ProfileImagePayloadKey), "image survives a field update");
        Assert.That(keys, Does.Contain(ContactMergeLog.PayloadKey), "merge_log was written");
        Assert.That(await ReadImagePayloadAsync(owner, uid), Is.EqualTo(image), "image bytes intact after field update");
    }

    [Test]
    public async Task DeleteImage_RemovesPayload_KeepsContact()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;
        await SetSampleImageAsync(owner, contacts, uid, create.Content.VersionTag);

        var current = await GetByUniqueIdAsync(owner, uid);
        var del = await contacts.DeleteImageAsync(uid, current!.FileMetadata.VersionTag);
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var header = await GetByUniqueIdAsync(owner, uid);
        Assert.That((header!.FileMetadata.Payloads ?? new List<PayloadDescriptor>())
            .Any(p => p.Key == ContactService.ProfileImagePayloadKey), Is.False, "image payload should be gone");
        Assert.That(DecryptContent(owner, header).Name!.DisplayName, Is.EqualTo("Sam"), "contact content preserved");

        // Deleting an absent image → 404.
        var current2 = await GetByUniqueIdAsync(owner, uid);
        var delAgain = await contacts.DeleteImageAsync(uid, current2!.FileMetadata.VersionTag);
        Assert.That(delAgain.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SetImage_NonExistentContact_Returns404()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var resp = await contacts.SetImageAsync(Guid.NewGuid(), new SetContactImageRequest
        {
            VersionTag = Guid.NewGuid(), ContentType = "image/jpeg", Iv = new byte[16], Content = [1, 2, 3]
        });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SetImage_StaleVersion_Returns409()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var resp = await contacts.SetImageAsync(uid, new SetContactImageRequest
        {
            VersionTag = Guid.NewGuid(), ContentType = "image/jpeg", Iv = new byte[16], Content = [1, 2, 3]
        });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    private static async Task<(byte[] image, byte[] thumb)> SetSampleImageAsync(
        OwnerSession owner, V2ContactsClient contacts, Guid uid, Guid versionTag)
    {
        // Client-side encryption: read the contact's file AES key from its header, then encrypt the
        // image + thumbnail under it with a fresh payload IV (the server stores the ciphertext as-is).
        var header = await GetByUniqueIdAsync(owner, uid);
        var ss = owner.SharedSecret;
        var fileKey = header!.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);

        var imagePlain = Enumerable.Range(0, 64).Select(i => (byte)(i * 3 + 1)).ToArray();
        var thumbPlain = Enumerable.Range(0, 16).Select(i => (byte)(i * 7 + 2)).ToArray();

        var iv = ByteArrayUtil.GetRndByteArray(16);
        var payloadKeyHeader = new KeyHeader { Iv = iv, AesKey = fileKey.AesKey };

        var resp = await contacts.SetImageAsync(uid, new SetContactImageRequest
        {
            VersionTag = versionTag,
            ContentType = "image/jpeg",
            Iv = iv,
            Content = payloadKeyHeader.EncryptDataAes(imagePlain),
            Thumbnails =
            [
                new ContactImageThumbnail
                {
                    PixelWidth = 32,
                    PixelHeight = 32,
                    ContentType = "image/jpeg",
                    Content = payloadKeyHeader.EncryptDataAes(thumbPlain)
                }
            ]
        });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"set image: {resp.StatusCode}");
        return (imagePlain, thumbPlain);
    }

    private static async Task<byte[]> ReadImagePayloadAsync(OwnerSession owner, Guid uid)
    {
        var header = await GetByUniqueIdAsync(owner, uid);
        var descriptor = header!.FileMetadata.Payloads!.First(p => p.Key == ContactService.ProfileImagePayloadKey);

        var reader = new DriveReaderV2Client(owner.Identity, owner.Factory);
        var resp = await reader.GetPayloadByUniqueIdAsync(uid, ContactDriveId, ContactService.ProfileImagePayloadKey);
        Assert.That(resp.IsSuccessStatusCode, Is.True, "image payload should be readable");
        var cipher = await resp.Content!.ReadAsByteArrayAsync();

        var ss = owner.SharedSecret;
        var fileKey = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);
        return new KeyHeader { Iv = descriptor.Iv, AesKey = fileKey.AesKey }.Decrypt(cipher);
    }

    // -----------------------------------------------------------------------------------------
    // enrichment / sync
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task Sync_EnrichesConnectedContact_FromPeerProfile_AndLogsAsEnrichment()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        // 1) Seed Sam's profile: an anonymous Name attribute on his ProfileDrive, tagged with the
        //    Name attribute type id (so the enrichment peer-query finds it).
        await SeedProfileNameAsync(sam, "Samwise Gamgee", "Samwise");

        // 2) Frodo creates a placeholder contact for Sam — BEFORE connecting, because the send hook
        //    upserts a stub for the recipient (a CreateAsync after connecting would 409).
        var contacts = new V2ContactsClient(frodo.Identity, frodo.Factory);
        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Placeholder" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        // 3) Connect Frodo <-> Sam so Frodo can peer-query Sam's ProfileDrive.
        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);
        var icr = await frodo.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(icr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));

        // 4) Sync → enrichment peer-queries Sam's profile and merges the result.
        var sync = await contacts.SyncAsync(Identities.Sam);
        Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        // 5) The placeholder name was overwritten by Sam's profile name.
        var stored = DecryptContent(frodo, (await GetByUniqueIdAsync(frodo, uid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Samwise Gamgee"));
        Assert.That(stored.Name.GivenName, Is.EqualTo("Samwise"));

        // 6) The overwrite was recorded in the merge log exactly once. Source can be either "api" (the
        // connection-flow's own card exchange already carried the real name, since Sam's public profile is
        // now published before the request completes) or "enrichment" (the explicit /sync call) --
        // whichever leg of the connection/sync flow gets to a real name first records it; the other is then
        // a no-op merge (same value, nothing to log).
        var log = await ReadMergeLogAsync(frodo, uid);
        Assert.That(log, Has.Count.EqualTo(1));
        Assert.That(log[0].By, Is.AnyOf("api", "enrichment"));
        Assert.That(log[0].Changes["name.displayName"], Is.EqualTo("Placeholder"));
    }

    [Test]
    public async Task Sync_EnrichesSocialLinkAndStatus_FromPeerProfile_KeyedByTypeId()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        // Seed Sam's profile with two social handles (one per network — data is a single {network: handle}
        // pair), a game handle, and a personal link.
        await SeedProfileAttributeAsync(sam, BuiltInProfileAttributes.Twitter,
            new Dictionary<string, object> { ["twitter"] = "@samwise" }, priority: 1000);
        await SeedProfileAttributeAsync(sam, BuiltInProfileAttributes.Instagram,
            new Dictionary<string, object> { ["instagram"] = "eerr33" }, priority: 999); // exact real-data shape
        await SeedProfileAttributeAsync(sam, BuiltInProfileAttributes.Github,
            new Dictionary<string, object> { ["github"] = "samwiseg" }, priority: 1001);
        await SeedProfileAttributeAsync(sam, BuiltInProfileAttributes.Steam,
            new Dictionary<string, object> { ["steam"] = "gardener" }, priority: 1002);
        await SeedProfileAttributeAsync(sam, BuiltInProfileAttributes.Link,
            new Dictionary<string, object> { ["link_text"] = "My site", ["link_target"] = "https://sam.shire" }, priority: 1003);
        await SeedProfileAttributeAsync(sam, BuiltInProfileAttributes.Status,
            new Dictionary<string, object> { ["status"] = "I am da man" }, priority: 1004);
        await SeedProfileAttributeAsync(sam, BuiltInProfileAttributes.Nickname,
            new Dictionary<string, object> { ["nickName"] = "Sam" }, priority: 1005);
        await SeedProfileAttributeAsync(sam, BuiltInProfileAttributes.Address,
            new Dictionary<string, object>
            {
                ["address1"] = "1 Bagshot Row", ["address2"] = "Bag End", ["postcode"] = "SH1 1AA",
                ["city"] = "Hobbiton", ["country"] = "The Shire"
            }, priority: 1006);

        var contacts = new V2ContactsClient(frodo.Identity, frodo.Factory);
        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Placeholder" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);

        var sync = await contacts.SyncAsync(Identities.Sam);
        Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var stored = DecryptContent(frodo, (await GetByUniqueIdAsync(frodo, uid))!);

        // Social/game handles are keyed by the attribute TYPE ID in the data's no-dash form, value = handle.
        Assert.That(stored.Social, Is.Not.Null);
        Assert.That(stored.Social![BuiltInProfileAttributes.Twitter.ToString("N")], Is.EqualTo("@samwise"));
        Assert.That(stored.Social!["345fef7bada5b100001e4c78111c86de"], Is.EqualTo("eerr33"), "instagram, exact real-data type id");
        Assert.That(stored.Social![BuiltInProfileAttributes.Github.ToString("N")], Is.EqualTo("samwiseg"));
        Assert.That(stored.Social![BuiltInProfileAttributes.Steam.ToString("N")], Is.EqualTo("gardener"), "game handles ride in social too");

        // The personal link flattens to its target URL.
        Assert.That(stored.Link, Is.EqualTo("https://sam.shire"));

        // Status / nickname flatten into their content fields.
        Assert.That(stored.Status, Is.EqualTo("I am da man"));
        Assert.That(stored.Nickname, Is.EqualTo("Sam"));

        // Location carries the full street address, not just city/country.
        Assert.That(stored.Location!.AddressLine1, Is.EqualTo("1 Bagshot Row"));
        Assert.That(stored.Location.AddressLine2, Is.EqualTo("Bag End"));
        Assert.That(stored.Location.Postcode, Is.EqualTo("SH1 1AA"));
        Assert.That(stored.Location.City, Is.EqualTo("Hobbiton"));
        Assert.That(stored.Location.Country, Is.EqualTo("The Shire"));
    }

    [Test]
    public async Task Sync_EnrichesExtData_FromPeerBioAttributes_RoundTrips()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        // Seed Sam's profile with the three bio-family attributes:
        //  - "Short bio" (1d89f51a…): a plain string ≤160 chars → flattened into the contact CONTENT header.
        //  - "Experience" (md5 full_bio) and "Bio" (md5 short_bio): rich-text → carried VERBATIM in ext_data.
        var experienceData = new Dictionary<string, object>
        {
            ["short_bio"] = "experience-title",
            ["full_bio"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["children"] = new object[] { new Dictionary<string, object> { ["text"] = "experience description" } },
                    ["type"] = "p",
                    ["id"] = "NF4tmpq6sK"
                }
            },
            ["experience_link"] = "https://experince.link",
            ["experience_image"] = "xprnc_key"
        };
        var bioData = new Dictionary<string, object>
        {
            ["short_bio"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "paragraph",
                    ["children"] = new object[] { new Dictionary<string, object> { ["text"] = "born born" } },
                    ["id"] = "NXHtACYXHc"
                }
            }
        };

        await SeedProfileAttributeAsync(sam, ContactProfileAttributes.Experience, experienceData, priority: 11000);
        await SeedProfileAttributeAsync(sam, ContactProfileAttributes.Bio, bioData, priority: 10000);
        await SeedProfileAttributeAsync(sam, ContactProfileAttributes.ShortBioType,
            new Dictionary<string, object> { ["short_bio"] = "a short bio yo" }, priority: 12000);

        // Placeholder contact + connect + sync (same pattern as the other enrichment tests).
        var contacts = new V2ContactsClient(frodo.Identity, frodo.Factory);
        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Placeholder" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);

        var sync = await contacts.SyncAsync(Identities.Sam);
        Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        // 1) The small "Short bio" string flattened into the content header (not ext_data).
        var stored = DecryptContent(frodo, (await GetByUniqueIdAsync(frodo, uid))!);
        Assert.That(stored.ShortBio, Is.EqualTo("a short bio yo"));

        // 2) ext_data payload carries Experience + Bio verbatim, keyed by type id (no-dash form).
        var extData = await ReadExtDataAsync(frodo, uid);
        Assert.That(extData, Is.Not.Null, "ext_data payload should be present");
        Assert.That(extData!.Attributes, Is.Not.Null);

        var expKey = ContactProfileAttributes.Experience.ToString("N");
        var bioKey = ContactProfileAttributes.Bio.ToString("N");
        Assert.That(extData.Attributes.ContainsKey(expKey), Is.True, "Experience should be in ext_data");
        Assert.That(extData.Attributes.ContainsKey(bioKey), Is.True, "Bio should be in ext_data");
        Assert.That(extData.Attributes.ContainsKey(ContactProfileAttributes.ShortBioType.ToString("N")), Is.False,
            "the plain-string Short bio belongs in the content header, not ext_data");

        // Rich text round-trips verbatim (structure and values preserved).
        var exp = extData.Attributes[expKey];
        Assert.That(exp.GetProperty("short_bio").GetString(), Is.EqualTo("experience-title"));
        Assert.That(exp.GetProperty("experience_link").GetString(), Is.EqualTo("https://experince.link"));
        var expText = exp.GetProperty("full_bio").EnumerateArray().First()
            .GetProperty("children").EnumerateArray().First().GetProperty("text").GetString();
        Assert.That(expText, Is.EqualTo("experience description"));

        var bio = extData.Attributes[bioKey];
        var bioText = bio.GetProperty("short_bio").EnumerateArray().First()
            .GetProperty("children").EnumerateArray().First().GetProperty("text").GetString();
        Assert.That(bioText, Is.EqualTo("born born"));
    }

    [Test]
    public async Task Sync_AppWithoutExplicitReadConnections_StillEnriches_ViaImpliedPermissions()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        await SeedProfileNameAsync(sam, "Samwise Gamgee", "Samwise");

        // The app does NOT request ReadConnections — ManageContacts implies it (plus
        // ReadConnectionRequests/ReadCircleMembership) at permission-context creation. UseTransitRead
        // is still needed for the peer profile query itself.
        var contacts = await GetContactsClientAsync(frodo, CallerKind.App,
            [PermissionKeys.ManageContacts, PermissionKeys.UseTransitRead]);

        // Create the placeholder BEFORE connecting (the send hook upserts a stub; a later create 409s).
        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Placeholder" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);

        var sync = await contacts.SyncAsync(Identities.Sam);
        Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        // Without the implied ReadConnections, the connection check inside enrichment fails silently
        // and the placeholder would survive.
        var stored = DecryptContent(frodo, (await GetByUniqueIdAsync(frodo, uid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Samwise Gamgee"),
            "the app should resolve connection status via the implied ReadConnections and enrich from the peer profile");
    }

    [Test]
    public async Task Sync_MultipleSameTypeAttributes_LowestPriorityNonEmptyWins()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        // Three Name attributes. The top-priority one is empty (must be skipped, not chosen as the
        // winner), then the primary, then a lower-priority secondary. Selection is by authored
        // Priority (lower wins), not query/created order.
        await SeedProfileNameAsync(sam, displayName: "", priority: 0);
        await SeedProfileNameAsync(sam, displayName: "Samwise Gamgee", priority: 1);
        await SeedProfileNameAsync(sam, displayName: "Sam (secondary)", priority: 2);

        var contacts = new V2ContactsClient(frodo.Identity, frodo.Factory);
        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Placeholder" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);

        var sync = await contacts.SyncAsync(Identities.Sam);
        Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var stored = DecryptContent(frodo, (await GetByUniqueIdAsync(frodo, uid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Samwise Gamgee"),
            "the lowest-priority-number non-empty attribute should win (empty higher-priority skipped)");
    }

    [Test]
    public async Task Sync_CreatesContactStub_WhenNoneExists()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        // Phase 1: contacts are not auto-created on the connection lifecycle; the client drives it by
        // calling /sync, which ensures the contact exists even before any profile data is available.
        var uid = ContactService.ToContactUniqueId((OdinId)Identities.Sam);
        Assert.That(await GetByUniqueIdAsync(frodo, uid), Is.Null, "precondition: no contact yet");

        var contacts = new V2ContactsClient(frodo.Identity, frodo.Factory);
        var sync = await contacts.SyncAsync(Identities.Sam);
        Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var header = await GetByUniqueIdAsync(frodo, uid);
        Assert.That(header, Is.Not.Null, "/sync should ensure the contact stub exists");
        Assert.That(header!.FileMetadata.AppData.FileType, Is.EqualTo(ContactService.ContactFileType));
        Assert.That(header.FileMetadata.AppData.UniqueId, Is.EqualTo(uid));
    }

    [Test]
    public async Task Upsert_AppWithoutManageContacts_IsForbidden()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        // App granted no permission keys → ManageContacts assertion fails.
        var contacts = await GetContactsClientAsync(owner, CallerKind.App, permissionKeys: []);

        var response = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // -----------------------------------------------------------------------------------------
    // connection lifecycle (contact created on send + accept)
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task ConnectionFlow_CreatesContacts_OnSendAndAccept()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Sending a request stubs a contact for the recipient on the sender's drive.
        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);

        var samUid = ContactService.ToContactUniqueId((OdinId)Identities.Sam);
        var frodoSideSam = await GetByUniqueIdAsync(frodo, samUid);
        Assert.That(frodoSideSam, Is.Not.Null, "sending a request should stub a contact for the recipient");
        Assert.That(DecryptContent(frodo, frodoSideSam!).Source, Is.EqualTo("contact"));

        // Accepting creates a contact for the sender, named from the card they sent ("Test Test").
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);

        var frodoUid = ContactService.ToContactUniqueId((OdinId)Identities.Frodo);
        var samSideFrodo = await GetByUniqueIdAsync(sam, frodoUid);
        Assert.That(samSideFrodo, Is.Not.Null, "accepting a request should create a contact for the sender");
        var stored = DecryptContent(sam, samSideFrodo!);
        Assert.That(stored.OdinId, Is.EqualTo(Identities.Frodo));
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Test Test"));
        Assert.That(stored.Source, Is.EqualTo("contact"));
    }

    [Test]
    public async Task Send_NamesContact_FromRecipientPublicCard()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Sam publishes a public profile card (served at pub/profile); the recipient returns it on the
        // DeliverConnectionRequest response, so the sender names the contact in the same round-trip.
        var samStatic = new UniversalStaticFileApiClient(sam.Identity, sam.Factory);
        var publish = await samStatic.PublishPublicProfileCard(new PublishPublicProfileCardRequest
        {
            ProfileCardJson = "{\"name\":\"Samwise Gamgee\"}"
        });
        Assert.That(publish.IsSuccessStatusCode, Is.True, "publishing the public profile card should succeed");

        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);

        var samUid = ContactService.ToContactUniqueId((OdinId)Identities.Sam);
        var stored = DecryptContent(frodo, (await GetByUniqueIdAsync(frodo, samUid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Samwise Gamgee"),
            "the recipient's public card returned on delivery names the contact");
        Assert.That(stored.Source, Is.EqualTo("contact"));
    }

    [Test]
    public async Task Accept_CreatesRichContact_FromSentCard()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Frodo shares a full contact card on the request; Sam's accepted contact is built from it.
        var frodosCard = new ContactContent
        {
            Source = "user",
            Name = new ContactName { DisplayName = "Frodo Baggins", GivenName = "Frodo", Surname = "Baggins" },
            Location = new ContactLocation { City = "Hobbiton", Country = "The Shire" },
            Email = new ContactEmail { Email = "frodo@shire.example" }
        };

        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity, contactCard: frodosCard)).IsSuccessStatusCode, Is.True);
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);

        var frodoUid = ContactService.ToContactUniqueId((OdinId)Identities.Frodo);
        var stored = DecryptContent(sam, (await GetByUniqueIdAsync(sam, frodoUid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Frodo Baggins"));
        Assert.That(stored.Name.Surname, Is.EqualTo("Baggins"));
        Assert.That(stored.Location!.City, Is.EqualTo("Hobbiton"));
        Assert.That(stored.Email!.Email, Is.EqualTo("frodo@shire.example"));
        Assert.That(stored.OdinId, Is.EqualTo(Identities.Frodo));
        Assert.That(stored.Source, Is.EqualTo("contact"), "connection-derived source overrides the card's own source");
    }

    // -----------------------------------------------------------------------------------------
    // per-app data (appData[appId]) — the JSON-tier app blob
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task AppData_AppWritesBlob_RidesInlineInContent_NotAPayload()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var contacts = new V2ContactsClient(app.Identity, app.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        const string blob = "{\"loc\":\"shire\"}";
        var set = await contacts.SetAppDataAsync(uid, new SetContactAppDataRequest
        {
            Content = blob,
            VersionTag = create.Content.VersionTag
        });
        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(set.Content!.VersionTag, Is.Not.EqualTo(create.Content.VersionTag), "the write advances the version tag");

        var header = await GetByUniqueIdAsync(owner, uid);
        var stored = DecryptContent(owner, header!);
        Assert.That(stored.AppData, Is.Not.Null);
        Assert.That(stored.AppData![app.AppId.ToString()], Is.EqualTo(blob), "the app's blob rides inline in the content");

        // The governing rule: list/display reads never touch payloads — app data is in the content JSON.
        Assert.That(header!.FileMetadata.Payloads ?? new List<PayloadDescriptor>(), Is.Empty,
            "app data must ride inline in the content JSON, not as a payload");
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Sam"), "core field preserved");
    }

    [Test]
    public async Task AppData_DifferentApp_NeitherSeesNorOverwritesTheOther()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appA = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var appB = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var contactsA = new V2ContactsClient(appA.Identity, appA.Factory);
        var contactsB = new V2ContactsClient(appB.Identity, appB.Factory);

        var create = await contactsA.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var setA = await contactsA.SetAppDataAsync(uid, new SetContactAppDataRequest { Content = "\"A\"", VersionTag = create.Content!.VersionTag });
        Assert.That(setA.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var setB = await contactsB.SetAppDataAsync(uid, new SetContactAppDataRequest { Content = "\"B\"", VersionTag = setA.Content!.VersionTag });
        Assert.That(setB.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        Assert.That(stored.AppData![appA.AppId.ToString()], Is.EqualTo("\"A\""), "app A's blob is intact after app B writes");
        Assert.That(stored.AppData![appB.AppId.ToString()], Is.EqualTo("\"B\""), "app B writes its own slot");
        Assert.That(stored.AppData.Count, Is.EqualTo(2), "each app keeps a distinct slot");
    }

    [Test]
    public async Task AppData_CoreUpdate_PreservesAppBlob()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var appContacts = new V2ContactsClient(app.Identity, app.Factory);
        var ownerContacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await ownerContacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var set = await appContacts.SetAppDataAsync(uid, new SetContactAppDataRequest { Content = "\"keep\"", VersionTag = create.Content!.VersionTag });
        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // An owner core update carries no appData; the merge must preserve the app blob untouched.
        var current = await GetByUniqueIdAsync(owner, uid);
        var update = await ownerContacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = current!.FileMetadata.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Samwise" } }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Samwise"), "core field updated");
        Assert.That(stored.AppData![app.AppId.ToString()], Is.EqualTo("\"keep\""), "core update preserves the app blob");
    }

    [Test]
    public async Task AppData_Delete_ClearsOnlyThatAppsBlob()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appA = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var appB = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var contactsA = new V2ContactsClient(appA.Identity, appA.Factory);
        var contactsB = new V2ContactsClient(appB.Identity, appB.Factory);

        var create = await contactsA.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;
        await contactsA.SetAppDataAsync(uid, new SetContactAppDataRequest { Content = "\"A\"", VersionTag = create.Content!.VersionTag });
        var setB = await contactsB.SetAppDataAsync(uid, new SetContactAppDataRequest { Content = "\"B\"", VersionTag = (await GetByUniqueIdAsync(owner, uid))!.FileMetadata.VersionTag });
        Assert.That(setB.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var del = await contactsA.DeleteAppDataAsync(uid, setB.Content!.VersionTag);
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stored = DecryptContent(owner, (await GetByUniqueIdAsync(owner, uid))!);
        Assert.That(stored.AppData!.ContainsKey(appA.AppId.ToString()), Is.False, "app A's blob is gone");
        Assert.That(stored.AppData![appB.AppId.ToString()], Is.EqualTo("\"B\""), "app B's blob is untouched");

        // Deleting an absent blob → 404.
        var afterDelTag = (await GetByUniqueIdAsync(owner, uid))!.FileMetadata.VersionTag;
        var delAgain = await contactsA.DeleteAppDataAsync(uid, afterDelTag);
        Assert.That(delAgain.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task AppData_OverCap_Rejected()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var contacts = new V2ContactsClient(app.Identity, app.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var tooBig = "\"" + new string('x', ContactService.AppDataBlobMaxBytes + 1) + "\"";
        var set = await contacts.SetAppDataAsync(uid, new SetContactAppDataRequest { Content = tooBig, VersionTag = create.Content.VersionTag });
        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "an over-cap blob is rejected; use a payload for bulk data");
    }

    [Test]
    public async Task AppData_OwnerToken_IsBadRequest_NoAppToStamp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        // The owner console is not an app client, so there is no appId to stamp the blob with.
        var set = await contacts.SetAppDataAsync(uid, new SetContactAppDataRequest { Content = "\"x\"", VersionTag = create.Content.VersionTag });
        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CoreField_OverCap_Rejected()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var contacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent
            {
                OdinId = Identities.Sam,
                Name = new ContactName { DisplayName = new string('a', 257) } // > 256-char cap
            }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "an over-cap core field is rejected");
    }

    // -----------------------------------------------------------------------------------------
    // per-app bulk data (appextdata payload) — the on-demand tier
    // -----------------------------------------------------------------------------------------

    [Test]
    public async Task AppExtData_AppWritesBulkBlob_StoredAsPayload_NotInContent()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var contacts = new V2ContactsClient(app.Identity, app.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        // A blob far larger than the inline AppDataBlobMaxBytes cap → belongs in the bulk payload.
        var bulk = "\"" + new string('b', ContactService.AppDataBlobMaxBytes * 10) + "\"";
        var set = await contacts.SetAppExtDataAsync(uid, new SetContactAppExtDataRequest
        {
            Content = bulk,
            VersionTag = create.Content.VersionTag
        });
        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var header = await GetByUniqueIdAsync(owner, uid);
        Assert.That(header!.FileMetadata.Payloads?.Any(p => p.Key == ContactAppData.PayloadKey), Is.True,
            "bulk data is stored as the appextdata payload");

        // The governing rule: bulk data is NOT in the list/display content — it's fetched on demand.
        var content = DecryptContent(owner, header);
        Assert.That(content.AppData, Is.Null, "bulk data must not ride inline in the contact content");

        // The payload decrypts to this app's slot, verbatim.
        var appExt = await ReadAppExtDataAsync(owner, uid);
        Assert.That(appExt!.AppData![app.AppId.ToString()], Is.EqualTo(bulk));
    }

    [Test]
    public async Task AppExtData_DifferentApp_NeitherSeesNorOverwritesTheOther()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appA = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var appB = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var contactsA = new V2ContactsClient(appA.Identity, appA.Factory);
        var contactsB = new V2ContactsClient(appB.Identity, appB.Factory);

        var create = await contactsA.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var setA = await contactsA.SetAppExtDataAsync(uid, new SetContactAppExtDataRequest { Content = "\"A-bulk\"", VersionTag = create.Content.VersionTag });
        Assert.That(setA.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var setB = await contactsB.SetAppExtDataAsync(uid, new SetContactAppExtDataRequest { Content = "\"B-bulk\"", VersionTag = setA.Content!.VersionTag });
        Assert.That(setB.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var appExt = await ReadAppExtDataAsync(owner, uid);
        Assert.That(appExt!.AppData![appA.AppId.ToString()], Is.EqualTo("\"A-bulk\""), "app A's bulk blob is intact");
        Assert.That(appExt.AppData![appB.AppId.ToString()], Is.EqualTo("\"B-bulk\""), "app B has its own bulk slot");
        Assert.That(appExt.AppData.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task AppExtData_CoreUpdate_PreservesBulkPayload()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var appContacts = new V2ContactsClient(app.Identity, app.Factory);
        var ownerContacts = new V2ContactsClient(owner.Identity, owner.Factory);

        var create = await ownerContacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var set = await appContacts.SetAppExtDataAsync(uid, new SetContactAppExtDataRequest { Content = "\"keep-bulk\"", VersionTag = create.Content.VersionTag });
        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // An owner core update (which writes the merge_log payload) must carry the appextdata forward.
        var current = await GetByUniqueIdAsync(owner, uid);
        var update = await ownerContacts.UpdateAsync(uid, new UpdateContactRequest
        {
            VersionTag = current!.FileMetadata.VersionTag,
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Samwise" } }
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var appExt = await ReadAppExtDataAsync(owner, uid);
        Assert.That(appExt!.AppData![app.AppId.ToString()], Is.EqualTo("\"keep-bulk\""), "core update preserves the bulk payload");
    }

    [Test]
    public async Task AppExtData_Delete_DropsPayloadWhenEmpty_KeepsOtherApp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appA = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var appB = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var contactsA = new V2ContactsClient(appA.Identity, appA.Factory);
        var contactsB = new V2ContactsClient(appB.Identity, appB.Factory);

        var create = await contactsA.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;
        await contactsA.SetAppExtDataAsync(uid, new SetContactAppExtDataRequest { Content = "\"A\"", VersionTag = create.Content.VersionTag });
        var setB = await contactsB.SetAppExtDataAsync(uid, new SetContactAppExtDataRequest { Content = "\"B\"", VersionTag = (await GetByUniqueIdAsync(owner, uid))!.FileMetadata.VersionTag });

        // Deleting A's slot keeps the payload (B remains).
        var delA = await contactsA.DeleteAppExtDataAsync(uid, setB.Content!.VersionTag);
        Assert.That(delA.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var afterA = await ReadAppExtDataAsync(owner, uid);
        Assert.That(afterA!.AppData!.ContainsKey(appA.AppId.ToString()), Is.False, "A's bulk blob is gone");
        Assert.That(afterA.AppData![appB.AppId.ToString()], Is.EqualTo("\"B\""), "B's bulk blob is untouched");

        // Deleting B's slot empties the map → the payload is dropped entirely.
        var afterATag = (await GetByUniqueIdAsync(owner, uid))!.FileMetadata.VersionTag;
        var delB = await contactsB.DeleteAppExtDataAsync(uid, afterATag);
        Assert.That(delB.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var header = await GetByUniqueIdAsync(owner, uid);
        Assert.That(header!.FileMetadata.Payloads?.Any(p => p.Key == ContactAppData.PayloadKey) ?? false, Is.False,
            "an emptied appextdata payload is dropped");

        // Deleting again → 404.
        var delAgain = await contactsB.DeleteAppExtDataAsync(uid, header.FileMetadata.VersionTag);
        Assert.That(delAgain.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task AppExtData_OverCap_Rejected()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read,
            PermissionKeyAllowance.Apps);
        var contacts = new V2ContactsClient(app.Identity, app.Factory);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Sam" } }
        });
        var uid = create.Content!.UniqueId;

        var tooBig = "\"" + new string('x', ContactService.AppExtDataBlobMaxBytes + 1) + "\"";
        var set = await contacts.SetAppExtDataAsync(uid, new SetContactAppExtDataRequest { Content = tooBig, VersionTag = create.Content.VersionTag });
        Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "an over-cap bulk blob is rejected");
    }

    // -----------------------------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a contacts client for the requested caller. The App is registered on the owner's
    /// identity (with a throwaway drive grant + the given permission keys), so the owner can always
    /// read the resulting contact files back.
    /// </summary>
    private static async Task<V2ContactsClient> GetContactsClientAsync(
        OwnerSession owner, CallerKind kind, IReadOnlyList<int> permissionKeys)
    {
        if (kind == CallerKind.Owner)
        {
            return new V2ContactsClient(owner.Identity, owner.Factory);
        }

        // The app gets a READ grant on the contact drive (per the plan, apps are read-only on the
        // contact drive and write via this API). The Read grant carries the drive storage key, which
        // ContactService needs to encrypt content at rest; the API supplies the Write permission via
        // an ACL-bypass upgrade.
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ContactDrive, DrivePermission.Read, permissionKeys);
        return new V2ContactsClient(app.Identity, app.Factory);
    }

    /// <summary>
    /// Seeds an anonymous Name profile attribute on the identity's ProfileDrive, tagged with the Name
    /// attribute type id (so the enrichment peer-query finds it).
    /// </summary>
    private static Task SeedProfileNameAsync(OwnerSession identity, string displayName, string givenName = null,
        int? priority = null)
    {
        var data = new Dictionary<string, object> { ["displayName"] = displayName };
        if (givenName != null)
        {
            data["givenName"] = givenName;
        }

        return SeedProfileAttributeAsync(identity, AttributeTypeId("name"), data, priority);
    }

    /// <summary>
    /// Seeds an anonymous profile attribute of the given type on the identity's ProfileDrive, tagged with
    /// its type id (so the enrichment peer-query finds it). <paramref name="data"/> is the attribute's
    /// <c>data</c> object (values may be nested arrays/objects, e.g. rich text).
    /// </summary>
    private static async Task SeedProfileAttributeAsync(OwnerSession identity, Guid type,
        Dictionary<string, object> data, int? priority = null)
    {
        var attribute = new ProfileBlock
        {
            Type = type.ToString(),
            Priority = priority,
            Data = data
        };

        var seed = await identity.Drives.Writer.CreateNewUnencryptedFile(
            SystemDriveConstants.ProfileDrive.Alias,
            new UploadFileMetadata
            {
                IsEncrypted = false,
                AccessControlList = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Anonymous },
                AppData = new UploadAppFileMetaData
                {
                    FileType = ContactProfileAttributes.AttributeFileType,
                    Tags = [type],
                    Content = OdinSystemSerializer.Serialize(attribute)
                }
            },
            new UploadManifest(),
            []);
        Assert.That(seed.IsSuccessStatusCode, Is.True, "seeding the profile attribute should succeed");
    }

    private static async Task<Odin.Services.Apps.SharedSecretEncryptedFileHeader> GetByUniqueIdAsync(
        OwnerSession owner, Guid uniqueId)
    {
        var reader = new DriveReaderV2Client(owner.Identity, owner.Factory);
        var resp = await reader.GetFileHeaderByUniqueIdAsync(uniqueId, ContactDriveId);
        return resp.IsSuccessStatusCode ? resp.Content : null;
    }

    private static async Task<int> CountContactsAsync(OwnerSession owner)
    {
        var reader = new DriveReaderV2Client(owner.Identity, owner.Factory);
        var resp = await reader.GetBatchAsync(ContactDriveId, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = SystemDriveConstants.ContactDrive,
                FileType = [ContactService.ContactFileType]
            },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });

        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        // Soft-deleted headers stay queryable (for sync); count only live contacts.
        return resp.Content!.SearchResults.Count(r => r.FileState == FileState.Active);
    }

    private static ContactContent DecryptContent(OwnerSession owner, Odin.Services.Apps.SharedSecretEncryptedFileHeader header)
    {
        var sharedSecret = owner.SharedSecret;
        var keyHeader = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
        var plain = keyHeader.Decrypt(Convert.FromBase64String(header.FileMetadata.AppData.Content));
        return JsonSerializer.Deserialize<ContactContent>(plain.ToStringFromUtf8Bytes())!;
    }

    /// <summary>md5(typeName)-as-Guid — the profile attribute type id, matching odin-js toGuidId.</summary>
    private static Guid AttributeTypeId(string typeName)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(typeName));
        return new Guid(BitConverter.ToString(hash).Replace("-", "").ToLower());
    }

    private static async Task<bool> HasMergeLogAsync(OwnerSession owner, Guid uniqueId)
    {
        var header = await GetByUniqueIdAsync(owner, uniqueId);
        return header?.FileMetadata.Payloads?.Any(p => p.Key == ContactMergeLog.PayloadKey) ?? false;
    }

    /// <summary>
    /// Reads and decrypts the contact's <c>merge_log</c> payload. The payload is AES-encrypted with the
    /// file key under its own IV (from the payload descriptor on the header).
    /// </summary>
    private static async Task<List<ContactMergeLogEntry>> ReadMergeLogAsync(OwnerSession owner, Guid uniqueId)
    {
        var header = await GetByUniqueIdAsync(owner, uniqueId);
        var descriptor = header?.FileMetadata.Payloads?.FirstOrDefault(p => p.Key == ContactMergeLog.PayloadKey);
        if (descriptor == null)
        {
            return new List<ContactMergeLogEntry>();
        }

        var reader = new DriveReaderV2Client(owner.Identity, owner.Factory);
        var resp = await reader.GetPayloadByUniqueIdAsync(uniqueId, ContactDriveId, ContactMergeLog.PayloadKey);
        Assert.That(resp.IsSuccessStatusCode, Is.True, "merge_log payload should be readable");
        var cipher = await resp.Content!.ReadAsByteArrayAsync();

        var sharedSecret = owner.SharedSecret;
        var fileKeyHeader = header!.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
        var plain = new KeyHeader { Iv = descriptor.Iv, AesKey = fileKeyHeader.AesKey }.Decrypt(cipher);
        return JsonSerializer.Deserialize<List<ContactMergeLogEntry>>(plain.ToStringFromUtf8Bytes())!;
    }

    /// <summary>
    /// Reads and decrypts the contact's <c>ext_data</c> payload (AES-encrypted with the file key under its
    /// own IV from the payload descriptor). Returns null when the payload is absent.
    /// </summary>
    private static async Task<ContactExtData> ReadExtDataAsync(OwnerSession owner, Guid uniqueId)
    {
        var header = await GetByUniqueIdAsync(owner, uniqueId);
        var descriptor = header?.FileMetadata.Payloads?.FirstOrDefault(p => p.Key == ContactExtData.PayloadKey);
        if (descriptor == null)
        {
            return null;
        }

        var reader = new DriveReaderV2Client(owner.Identity, owner.Factory);
        var resp = await reader.GetPayloadByUniqueIdAsync(uniqueId, ContactDriveId, ContactExtData.PayloadKey);
        Assert.That(resp.IsSuccessStatusCode, Is.True, "ext_data payload should be readable");
        var cipher = await resp.Content!.ReadAsByteArrayAsync();

        var sharedSecret = owner.SharedSecret;
        var fileKeyHeader = header!.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
        var plain = new KeyHeader { Iv = descriptor.Iv, AesKey = fileKeyHeader.AesKey }.Decrypt(cipher);
        return ContactExtData.Deserialize(plain);
    }

    /// <summary>
    /// Reads and decrypts the contact's <c>appextdata</c> payload (AES-encrypted with the file key under
    /// its own IV from the payload descriptor). Returns null when the payload is absent.
    /// </summary>
    private static async Task<ContactAppData> ReadAppExtDataAsync(OwnerSession owner, Guid uniqueId)
    {
        var header = await GetByUniqueIdAsync(owner, uniqueId);
        var descriptor = header?.FileMetadata.Payloads?.FirstOrDefault(p => p.Key == ContactAppData.PayloadKey);
        if (descriptor == null)
        {
            return null;
        }

        var reader = new DriveReaderV2Client(owner.Identity, owner.Factory);
        var resp = await reader.GetPayloadByUniqueIdAsync(uniqueId, ContactDriveId, ContactAppData.PayloadKey);
        Assert.That(resp.IsSuccessStatusCode, Is.True, "appextdata payload should be readable");
        var cipher = await resp.Content!.ReadAsByteArrayAsync();

        var sharedSecret = owner.SharedSecret;
        var fileKeyHeader = header!.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
        var plain = new KeyHeader { Iv = descriptor.Iv, AesKey = fileKeyHeader.AesKey }.Decrypt(cipher);
        return ContactAppData.Deserialize(plain);
    }
}
