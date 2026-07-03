using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Optimization.Cdn;
using Odin.Services.Profile;

namespace Odin.Hosting.Tests.V2.Ported.Profile;

/// <summary>
/// Covers <see cref="ProfilePublishService"/>: profile attribute writes made through
/// <see cref="ProfileAttributeService"/> must republish the public, static-file artifacts
/// (<c>/cdn/sitedata.json</c>, <c>/pub/image</c>, <c>/pub/profile</c>) the same way odin-js's owner-app UI
/// does today, since a server-side write bypasses that client-side publish step entirely.
/// </summary>
[TestFixture]
public class ProfileAttributePublishingTests : V2Fixture
{
    private static readonly Guid NameType = BuiltInProfileAttributes.Name;
    private static readonly Guid NicknameType = BuiltInProfileAttributes.Nickname;
    private static readonly Guid PhotoType = BuiltInProfileAttributes.Photo;
    private static readonly Guid BioType = BuiltInProfileAttributes.Bio;

    // SectionOutput/StaticFile have no [JsonPropertyName] attributes and the server serializes camelCase
    // with string enums, so plain Deserialize<> would silently leave every property null / throw on enums.
    private static readonly JsonSerializerOptions SiteDataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public async Task SetAttribute_TriggeringType_RepublishesSiteDataAndProfileCard()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = new V2ProfileClient(owner.Identity, owner.Factory);

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

        using var client = Host.CreateClient();

        var siteDataResp = await client.GetAsync($"https://{Identities.Frodo}/cdn/sitedata.json");
        Assert.That(siteDataResp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {siteDataResp.StatusCode}");
        var rawBody = await siteDataResp.Content.ReadAsStringAsync();
        var sections = JsonSerializer.Deserialize<List<SectionOutput>>(rawBody, SiteDataJsonOptions)!;
        var nameSection = sections.SingleOrDefault(s => s.Name == "name");
        Assert.That(nameSection, Is.Not.Null, "sitedata.json should carry a 'name' section");
        Assert.That(nameSection!.Files, Has.Count.EqualTo(1));

        var cardResp = await client.GetAsync($"https://{Identities.Frodo}/pub/profile");
        Assert.That(cardResp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {cardResp.StatusCode}");
        var cardBody = await cardResp.Content.ReadAsStringAsync();
        Assert.That(cardBody, Does.Contain("Frodo Baggins"));
    }

    [Test]
    public async Task SetAttribute_NonTriggeringType_DoesNotPublishAnything()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = new V2ProfileClient(owner.Identity, owner.Factory);

        var response = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = NicknameType,
            Visibility = ProfileAttributeVisibility.Anonymous,
            Data = new Dictionary<string, object> { ["nickName"] = "Underhill" }
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");

        using var client = Host.CreateClient();

        // Nickname isn't part of odin-js's DEFAULT_SECTIONS or ProfileCardAttributeTypes -- preserving
        // today's behavior where not every attribute type triggers a republish. Nothing has ever been
        // published for this identity, so all three static files are still 404.
        var siteDataResp = await client.GetAsync($"https://{Identities.Frodo}/cdn/sitedata.json");
        Assert.That(siteDataResp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), $"actual {siteDataResp.StatusCode}");

        var cardResp = await client.GetAsync($"https://{Identities.Frodo}/pub/profile");
        Assert.That(cardResp.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), $"actual {cardResp.StatusCode}");
    }

    [Test]
    public async Task SetPhotoAttribute_RepublishesPublicImage()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = new V2ProfileClient(owner.Identity, owner.Factory);

        var thumbBytes = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

        var response = await profile.SetPhotoAttributeAsync(new SetPhotoAttributeRequest
        {
            Visibility = ProfileAttributeVisibility.Anonymous,
            ContentType = "image/webp",
            Content = Enumerable.Repeat((byte)0xAB, 128).ToArray(),
            Thumbnails =
            [
                new ProfilePhotoThumbnail
                {
                    PixelWidth = 250,
                    PixelHeight = 250,
                    ContentType = "image/jpeg",
                    Content = thumbBytes
                }
            ]
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");

        using var client = Host.CreateClient();
        var imageResp = await client.GetAsync($"https://{Identities.Frodo}/pub/image");
        Assert.That(imageResp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {imageResp.StatusCode}");
        Assert.That(imageResp.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/jpeg"));

        var bytes = await imageResp.Content.ReadAsByteArrayAsync();
        Assert.That(bytes, Is.EqualTo(thumbBytes), "the 250px thumbnail should be reused as the public image");
    }

    [Test]
    public async Task DeleteAttribute_PublicBio_UpdatesProfileCard()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var profile = new V2ProfileClient(owner.Identity, owner.Factory);

        var created = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = BioType,
            Visibility = ProfileAttributeVisibility.Anonymous,
            Data = new Dictionary<string, object> { ["short_bio"] = "A hobbit of the Shire." }
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var id = created.Content!.Id;

        using var client = Host.CreateClient();
        var cardBeforeResp = await client.GetAsync($"https://{Identities.Frodo}/pub/profile");
        Assert.That((await cardBeforeResp.Content.ReadAsStringAsync()), Does.Contain("A hobbit of the Shire."));

        var delete = await profile.DeleteAttributeAsync(id, created.Content.VersionTag);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), $"actual {delete.StatusCode}");

        var cardAfterResp = await client.GetAsync($"https://{Identities.Frodo}/pub/profile");
        Assert.That(cardAfterResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await cardAfterResp.Content.ReadAsStringAsync()), Does.Not.Contain("A hobbit of the Shire."));
    }

    [Test]
    public async Task SetAttribute_ConnectedVisibility_RepublishesButExcludesEncryptedContent()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, SystemDriveConstants.ProfileDrive,
            DrivePermission.Read, [PermissionKeys.ManageProfile]);
        var profile = new V2ProfileClient(app.Identity, app.Factory);

        var response = await profile.SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = NameType,
            Visibility = ProfileAttributeVisibility.Connected,
            Data = new Dictionary<string, object> { ["givenName"] = "Secret" }
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {response.StatusCode}");

        using var client = Host.CreateClient();

        // The write is of a triggering type, so sitedata.json still gets rebuilt -- but this attribute is
        // Connected (encrypted at rest), so it's filtered out of the public output (same filter
        // StaticFileContentService already applies when serving these sections).
        var siteDataResp = await client.GetAsync($"https://{Identities.Frodo}/cdn/sitedata.json");
        Assert.That(siteDataResp.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"actual {siteDataResp.StatusCode}");
        var sections = JsonSerializer.Deserialize<List<SectionOutput>>(await siteDataResp.Content.ReadAsStringAsync(), SiteDataJsonOptions)!;
        var nameSection = sections.SingleOrDefault(s => s.Name == "name");
        Assert.That(nameSection, Is.Not.Null);
        Assert.That(nameSection!.Files, Is.Empty, "a Connected (encrypted) attribute must not appear in the public sitedata.json");
    }
}
