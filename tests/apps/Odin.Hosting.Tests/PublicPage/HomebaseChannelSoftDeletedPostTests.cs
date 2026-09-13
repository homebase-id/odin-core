using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.PublicPage;

/// <summary>
/// A soft-deleted post leaves a tombstone (FileState.Deleted, Content = "", Payloads = null) that still
/// matches the channel post queries. The server-rendered pages and sitemap must skip it (#1698, #1699).
/// </summary>
public class HomebaseChannelSoftDeletedPostTests
{
    private const int PostFileType = 101;
    private const int ChannelDefinitionFileType = 103;

    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        // Fails on any Error log, which catches the paths that swallow the exception (link preview, sitemap)
        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task PublicPagesSkipSoftDeletedPostInChannel()
    {
        var identity = TestIdentities.Frodo;
        var ownerClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var channelDrive = new TargetDrive { Alias = Guid.NewGuid(), Type = SystemDriveConstants.ChannelDriveType };
        const string channelSlug = "soft-delete-channel";
        await ownerClient.DriveManager.CreateDrive(channelDrive, "Soft delete channel", "", allowAnonymousReads: true);

        await UploadAnonymousFile(ownerClient.DriveRedux, channelDrive, ChannelDefinitionFileType, tags: null, userDate: null,
            OdinSystemSerializer.Serialize(new { name = "Soft delete channel", slug = channelSlug, description = "", showOnHomePage = true }));

        var now = UnixTimeUtc.Now().milliseconds;

        var livePostTag = Guid.NewGuid();
        await UploadAnonymousFile(ownerClient.DriveRedux, channelDrive, PostFileType, [livePostTag], new UnixTimeUtc(now),
            OdinSystemSerializer.Serialize(new { id = livePostTag.ToString("N"), caption = "Live post", slug = "live-post", type = "Article" }));

        // Older than the live post, so it also lands in the live post's "See More" list
        var deletedPostTag = Guid.NewGuid();
        var deletedPost = await UploadAnonymousFile(ownerClient.DriveRedux, channelDrive, PostFileType, [deletedPostTag],
            new UnixTimeUtc(now - 60_000),
            OdinSystemSerializer.Serialize(new { id = deletedPostTag.ToString("N"), caption = "Deleted post", slug = "deleted-post", type = "Article" }));

        var deleteResponse = await ownerClient.DriveRedux.SoftDeleteFile(deletedPost);
        Assert.That(deleteResponse.IsSuccessStatusCode, Is.True, $"soft delete failed: {deleteResponse.StatusCode}");

        var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);

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
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        return response.Content!.File;
    }
}
