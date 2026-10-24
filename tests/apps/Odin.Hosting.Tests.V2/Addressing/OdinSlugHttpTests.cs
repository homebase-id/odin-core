using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Drives;
using Refit;

namespace Odin.Hosting.Tests.V2.Addressing;

/// <summary>
/// A malformed slug never gets past the edge: in a JSON body or a route it is a 400, never a 500, and
/// it never reaches a service.  The typed clients can't send one (an <see cref="OdinSlug"/> can't hold
/// it), so these send raw values.
/// </summary>
public class OdinSlugHttpTests : V2Fixture
{
    [TestCase("my chat")]
    [TestCase("my_chat")]
    [TestCase("chat'")]
    [TestCase("")]
    [TestCase("   ")]
    public async Task AMalformedSlugInAJsonBodyIsA400(string driveSlug)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var (client, ss) = owner.NewAdminHttpClient();

        var response = await RefitCreator.RestServiceFor<IRawDriveCreate>(client, ss).CreateDrive(new Dictionary<string, object>
        {
            ["targetDrive"] = TargetDrive.NewTargetDrive(),
            ["name"] = "Malformed slug",
            ["metadata"] = "",
            ["driveSlug"] = driveSlug,
            ["driveTypeSlug"] = "note"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), response.Error?.Content);
        Assert.That(response.Error?.Content, Does.Contain("\"errorCode\":\"argumentError\""));
    }

    [Test]
    public async Task AnUpperCaseSlugInAJsonBodyIsStoredLowerCase()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var (client, ss) = owner.NewAdminHttpClient();
        var drive = TargetDrive.NewTargetDrive();
        var slug = "T" + drive.Alias.Value.ToString("N")[..13].ToUpperInvariant();

        var response = await RefitCreator.RestServiceFor<IRawDriveCreate>(client, ss).CreateDrive(new Dictionary<string, object>
        {
            ["targetDrive"] = drive,
            ["name"] = "Upper case slug",
            ["metadata"] = "",
            ["driveSlug"] = slug,
            ["driveTypeSlug"] = "NOTE"
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);

        var created = await owner.Admin.GetDrive(drive);
        Assert.That(created.DriveSlug.Value, Is.EqualTo(slug.ToLowerInvariant()));
        Assert.That(created.DriveTypeSlug.Value, Is.EqualTo("note"));
    }

    [TestCase("bad_app", "chat")]
    [TestCase("chat", "bad.drive")]
    public async Task AMalformedSlugInARouteIsA400(string appSlug, string driveSlug)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var (client, ss) = owner.NewAdminHttpClient();

        var response = await RefitCreator.RestServiceFor<IAppDriveHttpClientApiV2>(client, ss).GetAppDrive(appSlug, driveSlug);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), response.Error?.Content);
        Assert.That(response.Error?.Content, Does.Contain("\"errorCode\":\"argumentError\""));
    }

    [Test]
    public async Task AMalformedTypeFilterIsA400()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var (client, ss) = owner.NewAdminHttpClient();

        var response = await RefitCreator.RestServiceFor<IAppDriveHttpClientApiV2>(client, ss).GetAppDrives("chat", type: "not a slug");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), response.Error?.Content);
        Assert.That(response.Error?.Content, Does.Contain("\"errorCode\":\"argumentError\""));
    }

    public interface IRawDriveCreate
    {
        [Post(OwnerApiPathConstants.DriveManagementV1 + "/create")]
        Task<ApiResponse<HttpContent>> CreateDrive([Body] Dictionary<string, object> request);
    }
}
