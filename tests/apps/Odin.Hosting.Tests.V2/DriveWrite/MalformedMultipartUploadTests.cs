using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// Regression coverage for the V2 file-create endpoint when the multipart body is malformed or
/// truncated. Production correlation id c0a2543b-9468-462c-84c2-c4ddc2bb6a00 showed
/// <c>POST /api/v2/drives/{driveId}/files</c> throwing an unhandled
/// <c>IOException("Unexpected end of Stream...")</c> from <see cref="System.IO"/>'s MultipartReader
/// and returning a 500. A malformed client body is a client error and must surface as 400.
/// </summary>
[TestFixture]
public class MalformedMultipartUploadTests : V2Fixture
{
    [Test]
    public async Task TruncatedMultipartBodyReturnsBadRequestNotServerError()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive, "Malformed Upload Test Drive");

        using var client = owner.Factory.CreateHttpClient(owner.Identity, out _);

        // multipart/form-data content type, but a body that never contains the declared boundary:
        // a truncated / malformed upload. MultipartReader.ReadNextSectionAsync drains to end-of-stream
        // looking for the first boundary and throws IOException. That is the client's fault, not ours.
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("this-body-has-no-boundary"));
        content.Headers.ContentType =
            MediaTypeHeaderValue.Parse("multipart/form-data; boundary=----OdinMalformedTest");

        var url = $"https://{owner.Identity}/api/v2/drives/{targetDrive.Alias.Value}/files";
        var response = await client.PostAsync(url, content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
