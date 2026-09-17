using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.PublicPage;

/// <summary>
/// Port of <c>PublicPage/HomebaseChannelSoftDeletedPostTests</c>.
///
/// A soft-deleted post leaves a tombstone (FileState.Deleted, Content = "", Payloads = null) that still
/// matches the channel post queries. The server-rendered pages and sitemap must skip it (#1698, #1699).
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>No caller matrix in the original and none here — the SSR and sitemap surfaces are anonymous.</item>
/// <item>The original's <c>[TearDown] AssertLogEvents()</c> was load-bearing: the link-preview and
/// sitemap paths swallow exceptions and still answer 200, so an Error log is the only signal. That
/// invariant is now the <see cref="V2Fixture"/> default (<c>AssertNoErrorLogEvents</c>), so it is
/// carried rather than dropped.</item>
/// <item>Frodo is the fixture default and the original pinned Frodo too.</item>
/// </list>
/// </remarks>
[TestFixture]
public class HomebaseChannelSoftDeletedPostTests : V2Fixture
{
    private const int PostFileType = 101;
    private const int ChannelDefinitionFileType = 103;

    [Test]
    public async Task PublicPagesSkipSoftDeletedPostInChannel()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var channelDrive = new TargetDrive { Alias = Guid.NewGuid(), Type = SystemDriveConstants.ChannelDriveType };
        const string channelSlug = "soft-delete-channel";
        await owner.Admin.CreateDrive(channelDrive, "Soft delete channel", allowAnonymousReads: true);

        await UploadAnonymousFile(owner.V1.Drive, channelDrive, ChannelDefinitionFileType, tags: null, userDate: null,
            OdinSystemSerializer.Serialize(new { name = "Soft delete channel", slug = channelSlug, description = "", showOnHomePage = true }));

        var now = UnixTimeUtc.Now().milliseconds;

        var livePostTag = Guid.NewGuid();
        await UploadAnonymousFile(owner.V1.Drive, channelDrive, PostFileType, [livePostTag], new UnixTimeUtc(now),
            OdinSystemSerializer.Serialize(new { id = livePostTag.ToString("N"), caption = "Live post", slug = "live-post", type = "Article" }));

        // Older than the live post, so it also lands in the live post's "See More" list
        var deletedPostTag = Guid.NewGuid();
        var deletedPost = await UploadAnonymousFile(owner.V1.Drive, channelDrive, PostFileType, [deletedPostTag],
            new UnixTimeUtc(now - 60_000),
            OdinSystemSerializer.Serialize(new { id = deletedPostTag.ToString("N"), caption = "Deleted post", slug = "deleted-post", type = "Article" }));

        var deleteResponse = await owner.V1.Drive.SoftDeleteFile(deletedPost);
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var client = Host.CreateAnonymousClient(Identities.Frodo);

        // Server-rendered channel page
        {
            var response = await client.GetAsync($"/ssr/posts/{channelSlug}");
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            Assert.That(body, Does.Contain("Live post"));
            Assert.That(body, Does.Not.Contain("Deleted post"));
        }

        // Server-rendered post page; the tombstone would be in the "See More" list
        {
            var response = await client.GetAsync($"/ssr/posts/{channelSlug}/{livePostTag}");
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            Assert.That(body, Does.Contain("Live post"));
        }

        // Addressing the deleted post directly by its tag is a not-found, not a server error
        {
            var response = await client.GetAsync($"/ssr/posts/{channelSlug}/{deletedPostTag}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        // Sitemap swallows exceptions and still returns 200, so assert on the listed posts
        {
            var response = await client.GetAsync("/sitemap.xml");
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            Assert.That(body, Does.Contain($"/posts/{channelSlug}/live-post"));
            Assert.That(body, Does.Not.Contain("deleted-post"));
        }
    }

    private static async Task<ExternalFileIdentifier> UploadAnonymousFile(UniversalDriveApiClient driveClient, TargetDrive targetDrive,
        int fileType, List<Guid> tags, UnixTimeUtc? userDate, string jsonContent)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Tags = tags,
                Content = jsonContent,
                FileType = fileType,
                UserDate = userDate
            },
            AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Anonymous }
        };

        var response = await driveClient.UploadNewMetadata(targetDrive, fileMetadata);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content!.File;
    }
}
