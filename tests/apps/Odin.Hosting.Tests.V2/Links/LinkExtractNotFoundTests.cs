#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.LinkMetaExtractor;
using Refit;

namespace Odin.Hosting.Tests.V2.Links;

/// <summary>
/// "Nothing to preview" must be a 404, on both the V1 and V2 routes. It used to be the controller
/// returning a null <see cref="LinkMeta"/>, which ASP.NET sends as a 204 with an empty body, and a
/// client that only special-cases 404 then tries to deserialize nothing (#1754).
/// </summary>
/// <remarks>
/// A URL over the extractor's 512-character limit is refused before any fetch, so this needs no
/// network and cannot be flaky on someone else's website.
/// </remarks>
[TestFixture]
public class LinkExtractNotFoundTests : V2Fixture
{
    private static readonly string TooLongUrl = "https://example.com/" + new string('a', 600);

    protected override string[] HostIdentities => [Identities.Frodo];

    [Test]
    public async Task V1_NothingToPreview_Is404()
    {
        var response = await (await AppClientAsync()).ExtractV1(TooLongUrl);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task V2_NothingToPreview_Is404()
    {
        var response = await (await AppClientAsync()).ExtractV2(TooLongUrl);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private async Task<ILinkExtractClient> AppClientAsync()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var app = await AppSession.SetupAsync(owner, new PermissionSetGrantRequest
        {
            Drives = [],
            PermissionSet = new PermissionSet(new List<int>())
        });

        var client = app.Factory.CreateHttpClient(app.Identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<ILinkExtractClient>(client, sharedSecret);
    }

    public interface ILinkExtractClient
    {
        // V1-relative: the app factory prefixes /api/apps/v1.
        [Get("/utils/links/extract")]
        Task<ApiResponse<LinkMeta>> ExtractV1([Query] string url);

        [Get("/api/v2/links/extract")]
        Task<ApiResponse<LinkMeta>> ExtractV2([Query] string url);
    }
}
