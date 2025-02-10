using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Serialization;
using Odin.Hosting.PersonMetadata.SchemaDotOrg;
using Odin.Services.Optimization.Cdn;

namespace Odin.Hosting.PersonMetadata;

public static class IndexMetadata
{
    public static async Task<string> InjectIdentityMetadata(string indexFilePath, HttpContext context)
    {
        const string placeholder = "@@identifier-content@@";

        string odinId = context.Request.Host.Host;
        string defaultDescription = "Decentralized identity powered by Homebase.id";

        var person = await GeneratePersonSchema(context);

        var title = $"{person?.Name ?? odinId} | Homebase";
        var fallBackImage = $"{context.Request.Scheme}://{odinId}/pub/image";

        var description = person?.Description ?? defaultDescription;
        var metadata = $"<title>{title}</title>";
        metadata += $"<meta property='description' content='{description}'/>\n";
        metadata += $"<meta property='og:title' content='{title}'/>\n";
        metadata += $"<meta property='og:description' content='{description}'/>\n";
        metadata += $"<meta property='og:image' content='{person?.Image ?? fallBackImage}'/>\n";
        metadata += $"<meta property='og:url' content='{context.Request.GetDisplayUrl()}'/>\n";
        metadata += $"<meta property='og:site_name' content='{title}'/>\n";
        metadata += $"<meta property='og:type' content='website'/>\n";

        metadata += $"<meta property='profile:first_name' content='{person?.GivenName}'/>\n";
        metadata += $"<meta property='profile:last_name' content='{person?.FamilyName}'/>\n";
        metadata += $"<meta property='profile:username' content='{context.Request.Host}'/>\n";

        string webFinger = $"{context.Request.Scheme}://{odinId}/.well-known/webfinger?resource=acct:@{odinId}";
        metadata += $"<link rel='webfinger' content='{webFinger}'/>\n";

        metadata += $"<script type='application/ld+json'>\n";
        metadata += OdinSystemSerializer.Serialize(person) + "\n";
        metadata += $"</script>";

        var content = await File.ReadAllTextAsync(indexFilePath, context.RequestAborted);
        var updatedContent = content.Replace(placeholder, metadata);
        return updatedContent;
    }

    public static async Task<PersonSchema> GeneratePersonSchema(HttpContext context)
    {
        // read the profile file.
        var svc = context.RequestServices.GetRequiredService<StaticFileContentService>();
        var (_, fileExists, fileStream) = await svc.GetStaticFileStreamAsync(StaticFileConstants.PublicProfileCardFileName);

        FrontEndProfile profile = null;
        
        if (fileExists)
        {
            using var reader = new StreamReader(fileStream);
            var data = await reader.ReadToEndAsync();
            reader.Close();
            profile = OdinSystemSerializer.Deserialize<FrontEndProfile>(data);
        }

        var person = new PersonSchema
        {
            Name = profile?.Name,
            GivenName = profile?.Name,
            FamilyName = profile?.Name,
            Email = "",
            Description = profile?.Bio,
            BirthDate = "",
            JobTitle = "",
            Image = profile?.Image
            // WorksFor = new OrganizationSchema { Name = "Tech Corp" },
            // Address = new AddressSchema
            // {
            //     StreetAddress = "123 Main St",
            //     AddressLocality = "San Francisco",
            //     AddressRegion = "CA",
            //     PostalCode = "94105",
            //     AddressCountry = "USA"
            // },
            // SameAs = new List<string>
            // {
            //     "https://www.linkedin.com/in/johndoe",
            //     "https://twitter.com/johndoe"
            // }
        };

        return person;
    }
}