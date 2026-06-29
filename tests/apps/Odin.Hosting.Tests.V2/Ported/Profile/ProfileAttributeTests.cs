using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Profile;

namespace Odin.Hosting.Tests.V2.Ported.Profile;

/// <summary>
/// Covers the server-side V2 Profile attribute write API (<c>/api/v2/profile/attributes</c>): create,
/// in-place edit with optimistic concurrency, and delete. App callers are granted
/// <see cref="PermissionKeys.ManageProfile"/> plus a Read grant on the ProfileDrive (apps write via this
/// API, not directly), mirroring the contact-write model. Stored attribute files are asserted via the
/// owner's drive reader — they must match the odin-js attribute shape (fileType 77, four tags, groupId =
/// sectionId, the JSON attribute content).
/// </summary>
[TestFixture]
public class ProfileAttributeTests : V2Fixture
{
    private static readonly Guid ProfileDriveId = SystemDriveConstants.ProfileDrive.Alias;
    private static readonly Guid NameType = BuiltInProfileAttributes.Name;
    private static readonly Guid StatusType = BuiltInProfileAttributes.Status;

    // Standard profile id == ProfileDrive alias; PersonalInfoSection holds the Name/Status attributes.
    private static readonly string ExpectedProfileId = ProfileDriveId.ToString("N");
    private static readonly Guid PersonalInfoSectionId = new("158c7768-8016-2cb8-7f95-dcb3ecb587b0");

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
    public async Task SetAttribute_CreatesAnonymousNameAttribute_MatchingOdinJsShape(CallerKind kind)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = await GetProfileClientAsync(owner, kind, [PermissionKeys.ManageProfile]);

        var response = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = NameType,
            Visibility = ProfileAttributeVisibility.Anonymous,
            Data = new Dictionary<string, object>
            {
                ["givenName"] = "Frodo",
                ["surname"] = "Baggins"
            }
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");
        var id = response.Content!.Id;
        Assert.That(id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(response.Content.VersionTag, Is.Not.EqualTo(Guid.Empty));

        var header = await GetByUniqueIdAsync(owner, id);
        Assert.That(header, Is.Not.Null, "the created attribute file should be readable");
        var appData = header!.FileMetadata.AppData;

        Assert.That(appData.FileType, Is.EqualTo(ProfileAttributeService.AttributeFileType));
        Assert.That(header.FileMetadata.IsEncrypted, Is.False, "anonymous attributes are plaintext");
        Assert.That(appData.UniqueId, Is.EqualTo(id));
        Assert.That(appData.GroupId, Is.EqualTo(PersonalInfoSectionId));

        // odin-js tag order: [type, sectionId, profileId, id]
        Assert.That(appData.Tags, Is.EqualTo(new List<Guid> { NameType, PersonalInfoSectionId, ProfileDriveId, id }));

        var content = JsonSerializer.Deserialize<ProfileAttributeContent>(appData.Content)!;
        Assert.That(content.Type, Is.EqualTo(NameType.ToString("N")));
        Assert.That(content.ProfileId, Is.EqualTo(ExpectedProfileId));
        Assert.That(content.SectionId, Is.EqualTo(PersonalInfoSectionId.ToString("N")));
        Assert.That(content.Id, Is.EqualTo(id.ToString("N")));
        Assert.That(content.Data["givenName"].ToString(), Is.EqualTo("Frodo"));
        // The server computes displayName from given + surname, exactly as odin-js does.
        Assert.That(content.Data["displayName"].ToString(), Is.EqualTo("Frodo Baggins"));
    }

    [Test]
    public async Task SetAttribute_EditsExistingAttribute_AdvancesVersionTag()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = await GetProfileClientAsync(owner, CallerKind.App, [PermissionKeys.ManageProfile]);

        var created = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = StatusType,
            Visibility = ProfileAttributeVisibility.Anonymous,
            Data = new Dictionary<string, object> { ["status"] = "Out for second breakfast" }
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var id = created.Content!.Id;

        var edited = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = StatusType,
            Id = id,
            ExpectedVersionTag = created.Content.VersionTag,
            Visibility = ProfileAttributeVisibility.Anonymous,
            Data = new Dictionary<string, object> { ["status"] = "Off to Mordor" }
        });

        Assert.That(edited.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(edited.Content!.Id, Is.EqualTo(id), "editing keeps the same attribute id");
        Assert.That(edited.Content.VersionTag, Is.Not.EqualTo(created.Content.VersionTag));

        var header = await GetByUniqueIdAsync(owner, id);
        var content = JsonSerializer.Deserialize<ProfileAttributeContent>(header!.FileMetadata.AppData.Content)!;
        Assert.That(content.Data["status"].ToString(), Is.EqualTo("Off to Mordor"));
    }

    [Test]
    public async Task SetAttribute_StaleVersionTag_Returns409()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = await GetProfileClientAsync(owner, CallerKind.App, [PermissionKeys.ManageProfile]);

        var created = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = StatusType,
            Visibility = ProfileAttributeVisibility.Anonymous,
            Data = new Dictionary<string, object> { ["status"] = "v1" }
        });
        var id = created.Content!.Id;

        var conflict = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = StatusType,
            Id = id,
            ExpectedVersionTag = Guid.NewGuid(), // stale
            Visibility = ProfileAttributeVisibility.Anonymous,
            Data = new Dictionary<string, object> { ["status"] = "v2" }
        });

        Assert.That(conflict.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task SetAttribute_ConnectedVisibility_StoresEncrypted()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = await GetProfileClientAsync(owner, CallerKind.App, [PermissionKeys.ManageProfile]);

        var created = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = StatusType,
            Visibility = ProfileAttributeVisibility.Connected,
            Data = new Dictionary<string, object> { ["status"] = "secret status" }
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var header = await GetByUniqueIdAsync(owner, created.Content!.Id);
        Assert.That(header!.FileMetadata.IsEncrypted, Is.True, "connected attributes are encrypted at rest");
    }

    [Test]
    public async Task SetAttribute_AppWithoutManageProfile_IsForbidden()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = await GetProfileClientAsync(owner, CallerKind.App, permissionKeys: []);

        var response = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = NameType,
            Visibility = ProfileAttributeVisibility.Anonymous,
            Data = new Dictionary<string, object> { ["givenName"] = "Frodo" }
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"actual {response.StatusCode}");
    }

    [Test]
    public async Task DeleteAttribute_RemovesTheAttribute()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = await GetProfileClientAsync(owner, CallerKind.App, [PermissionKeys.ManageProfile]);

        var created = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = StatusType,
            Visibility = ProfileAttributeVisibility.Anonymous,
            Data = new Dictionary<string, object> { ["status"] = "ephemeral" }
        });
        var id = created.Content!.Id;

        var delete = await profile.DeleteAttributeAsync(id, created.Content.VersionTag);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), $"actual {delete.StatusCode}");
    }

    private static async Task<V2ProfileClient> GetProfileClientAsync(
        OwnerSession owner, CallerKind kind, IReadOnlyList<int> permissionKeys)
    {
        if (kind == CallerKind.Owner)
        {
            return new V2ProfileClient(owner.Identity, owner.Factory);
        }

        // Apps get a READ grant on the ProfileDrive (carrying the storage key the service needs to encrypt
        // non-public attributes); the API supplies Write via an ACL-bypass upgrade gated on ManageProfile.
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ProfileDrive,
            Odin.Services.Drives.DrivePermission.Read, permissionKeys);
        return new V2ProfileClient(app.Identity, app.Factory);
    }

    private static async Task<SharedSecretEncryptedFileHeader> GetByUniqueIdAsync(OwnerSession owner, Guid uniqueId)
    {
        var reader = new DriveReaderV2Client(owner.Identity, owner.Factory);
        var resp = await reader.GetFileHeaderByUniqueIdAsync(uniqueId, ProfileDriveId);
        return resp.IsSuccessStatusCode ? resp.Content : null;
    }
}
