using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.LinkPreview.Profile;
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

        var (image, thumb) = await SetSampleImageAsync(contacts, uid, create.Content.VersionTag);

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
        Assert.That(tPlain, Is.EqualTo(thumb.Content));

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
        var (image, _) = await SetSampleImageAsync(contacts, uid, create.Content.VersionTag);

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
        await SetSampleImageAsync(contacts, uid, create.Content.VersionTag);

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
            VersionTag = Guid.NewGuid(), ContentType = "image/jpeg", Content = [1, 2, 3]
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
            VersionTag = Guid.NewGuid(), ContentType = "image/jpeg", Content = [1, 2, 3]
        });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    private static async Task<(byte[] image, ContactImageThumbnail thumb)> SetSampleImageAsync(
        V2ContactsClient contacts, Guid uid, Guid versionTag)
    {
        var image = Enumerable.Range(0, 64).Select(i => (byte)(i * 3 + 1)).ToArray();
        var thumb = new ContactImageThumbnail
        {
            PixelWidth = 32,
            PixelHeight = 32,
            ContentType = "image/jpeg",
            Content = Enumerable.Range(0, 16).Select(i => (byte)(i * 7 + 2)).ToArray()
        };

        var resp = await contacts.SetImageAsync(uid, new SetContactImageRequest
        {
            VersionTag = versionTag,
            ContentType = "image/jpeg",
            Content = image,
            Thumbnails = [thumb]
        });
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"set image: {resp.StatusCode}");
        return (image, thumb);
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

        // 2) Connect Frodo <-> Sam so Frodo can peer-query Sam's ProfileDrive.
        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);
        var icr = await frodo.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(icr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));

        // 3) Frodo creates a contact for Sam with a placeholder name.
        var contacts = new V2ContactsClient(frodo.Identity, frodo.Factory);
        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Placeholder" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

        // 4) Sync → enrichment peer-queries Sam's profile and merges the result.
        var sync = await contacts.SyncAsync(Identities.Sam);
        Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        // 5) The placeholder name was overwritten by Sam's profile name.
        var stored = DecryptContent(frodo, (await GetByUniqueIdAsync(frodo, uid))!);
        Assert.That(stored.Name!.DisplayName, Is.EqualTo("Samwise Gamgee"));
        Assert.That(stored.Name.GivenName, Is.EqualTo("Samwise"));

        // 6) The overwrite was recorded in the merge log, tagged as enrichment.
        var log = await ReadMergeLogAsync(frodo, uid);
        Assert.That(log, Has.Count.EqualTo(1));
        Assert.That(log[0].By, Is.EqualTo("enrichment"));
        Assert.That(log[0].Changes["name.displayName"], Is.EqualTo("Placeholder"));
    }

    [Test]
    public async Task Sync_AppWithoutExplicitReadConnections_StillEnriches_ViaImpliedPermissions()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        await SeedProfileNameAsync(sam, "Samwise Gamgee", "Samwise");

        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);

        // The app does NOT request ReadConnections — ManageContacts implies it (plus
        // ReadConnectionRequests/ReadCircleMembership) at permission-context creation. UseTransitRead
        // is still needed for the peer profile query itself.
        var contacts = await GetContactsClientAsync(frodo, CallerKind.App,
            [PermissionKeys.ManageContacts, PermissionKeys.UseTransitRead]);

        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Placeholder" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

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

        Assert.That((await frodo.Connections.SendConnectionRequest(sam.Identity)).IsSuccessStatusCode, Is.True);
        Assert.That((await sam.Connections.AcceptConnectionRequest(frodo.Identity)).IsSuccessStatusCode, Is.True);

        var contacts = new V2ContactsClient(frodo.Identity, frodo.Factory);
        var create = await contacts.CreateAsync(new CreateContactRequest
        {
            Content = new ContactContent { OdinId = Identities.Sam, Name = new ContactName { DisplayName = "Placeholder" } }
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uid = create.Content!.UniqueId;

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
    private static async Task SeedProfileNameAsync(OwnerSession identity, string displayName, string givenName = null,
        int? priority = null)
    {
        var nameType = AttributeTypeId("name");
        var data = new Dictionary<string, object> { ["displayName"] = displayName };
        if (givenName != null)
        {
            data["givenName"] = givenName;
        }

        var attribute = new ProfileBlock
        {
            Type = nameType.ToString(),
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
                    Tags = [nameType],
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
}
