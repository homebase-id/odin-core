using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.LinkPreview.PersonMetadata;
using Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;
using Odin.Services.Optimization.Cdn;

namespace Odin.Services.LinkPreview;

/// <summary>
/// Loads information about this tenant and their profile information for server side rendering requirements
/// </summary>
public class HomebaseProfileContentService(
    StaticFileContentService staticFileContentService,
    IHttpContextAccessor httpContextAccessor)
{
    public string GetPublicImageUrl(IOdinContext odinContext)
    {
        var context = httpContextAccessor.HttpContext;
        var imageUrl = $"{context.Request.Scheme}://{odinContext.Tenant}/{LinkPreviewDefaults.PublicImagePath}";
        return imageUrl;
    }

    public async Task<PersonSchema> LoadPersonSchema()
    {
        // read the profile file.
        var (_, fileExists, fileStream) =
            await staticFileContentService.GetStaticFileStreamAsync(StaticFileConstants.PublicProfileCardFileName);

        FrontEndProfile profile = null;

        if (fileExists)
        {
            using var reader = new StreamReader(fileStream);
            var data = await reader.ReadToEndAsync();
            profile = OdinSystemSerializer.Deserialize<FrontEndProfile>(data);
        }

        var context = httpContextAccessor.HttpContext;
        string odinId = context.Request.Host.Host;

        var person = new PersonSchema
        {
            Name = profile?.Name,
            GivenName = profile?.GiveName,
            FamilyName = profile?.FamilyName,
            Email = null,
            Description = profile?.BioSummary ?? profile?.Bio,
            Bio = profile?.Bio,
            BioSummary = profile?.BioSummary,
            BirthDate = null,
            JobTitle = null,
            Image = AppendJpgIfNoExtension(profile?.Image ?? ""),
            SameAs = profile?.SameAs?.Select(s => s.Url).ToList() ?? [],
            Identifier =
            [
                $"{context.Request.Scheme}://{odinId}/.well-known/webfinger?resource=acct:@{odinId}",
                $"{context.Request.Scheme}://{odinId}/.well-known/did.json"
            ]
        };
        return person;
    }

    private static string AppendJpgIfNoExtension(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
        {
            return url;
        }

        string path = uri.AbsolutePath;

        if (string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            string newPath = path + ".jpg";

            UriBuilder builder = new UriBuilder(uri)
            {
                Path = newPath
            };

            return builder.Uri.ToString();
        }

        return url;
    }
}