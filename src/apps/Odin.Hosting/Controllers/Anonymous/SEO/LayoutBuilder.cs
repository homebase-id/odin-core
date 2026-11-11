using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.AspNetCore.Http;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.PublicPage;
using Odin.Services.PublicPage.PersonMetadata.SchemaDotOrg;
using Odin.Services.PublicPage.Seo;

namespace Odin.Hosting.Controllers.Anonymous.SEO;

public static class LayoutBuilder
{
    private const int MaxDescriptionLength = 250;

    public static string Wrap(string headContent, string body)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine(headContent);

        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine(body);
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    public static StringBuilder BuildHeadContent(
        string title,
        string description,
        string siteType,
        string imageUrl,
        PersonSchema person,
        HttpContext httpContext,
        IOdinContext odinContext,
        ArticleSeo article = null)
    {
        title = Truncate(title, MaxDescriptionLength);
        description = Truncate(description, MaxDescriptionLength);
        var siteName = string.IsNullOrWhiteSpace(person?.Name) ? odinContext.Tenant.DomainName : person.Name;

        // var request = HttpContext.Request;
        // var canonical = new UriBuilder(request.Scheme, request.Host.Host)
        // {
        //     Path = request.Path.Value.Replace($"/{LinkPreviewDefaults.SsrPath}", "", StringComparison.OrdinalIgnoreCase),
        //     Query = request.QueryString.Value
        // }.ToString();
        //
        var humanReadableVersion = GetHumanReadableVersion(httpContext);

        var b = new StringBuilder(1024);

        // SEO
        b.AppendLine($"<title>{HttpUtility.HtmlEncode(title)}</title>");
        b.AppendLine($"<meta name='description' content='{HttpUtility.HtmlEncode(description)}'/>");

        // Open Graph, Twitter card, and article tags
        b.Append(PageSeo.BuildMetaTags(title, description, siteName, siteType, humanReadableVersion, imageUrl, article));

        b.AppendLine($"<link rel='alternate' type='application/rss+xml' title='{HttpUtility.HtmlEncode(siteName)}' " +
                     $"href='https://{odinContext.Tenant.DomainName}/feed.xml' />");

        b.AppendLine($"<link rel='alternate' href='{GetDisplayUrlWithSsr(httpContext)}' />");
        b.AppendLine($"<link rel='canonical' href='{humanReadableVersion}' />");
        b.AppendLine(PrepareIdentityContent(person, odinContext));

        return b;
    }


    private static string PrepareIdentityContent(PersonSchema person, IOdinContext odinContext)
    {
        StringBuilder b = new StringBuilder(500);

        string odinId = odinContext.Tenant;

        b.Append($"<meta property='profile:first_name' content='{person?.GivenName}'/>\n");
        b.Append($"<meta property='profile:last_name' content='{person?.FamilyName}'/>\n");
        b.Append($"<meta property='profile:username' content='{odinId}'/>\n");
        b.Append($"<link rel='webfinger' href='https://{odinId}/.well-known/webfinger?resource=acct:@{odinId}'/>\n");
        b.Append($"<link rel='did' href='https://{odinId}/.well-known/did.json'/>\n");
        b.Append("<script type='application/ld+json'>\n");

        var options = new JsonSerializerOptions(OdinSystemSerializer.JsonSerializerOptions!)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        b.Append(OdinSystemSerializer.Serialize(person, options) + "\n");
        b.Append("</script>");

        return b.ToString();
    }

    private static string GetHumanReadableVersion(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var path = request.Path.HasValue ? request.Path.Value : "";
        return new UriBuilder(request.Scheme, request.Host.Host)
        {
            Path = path.Replace($"/{LinkPreviewDefaults.SsrPath}", "", StringComparison.OrdinalIgnoreCase),
            Query = request.QueryString.Value ?? ""
        }.ToString();
    }

    private static string GetDisplayUrlWithSsr(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var originalPath = request.Path.Value ?? "";

        // Avoid double /ssr prefix
        var ssrPrefix = $"/{LinkPreviewDefaults.SsrPath}";
        var pathWithSsr = originalPath.StartsWith(ssrPrefix, StringComparison.OrdinalIgnoreCase)
            ? originalPath
            : $"{ssrPrefix}{originalPath}";

        return new UriBuilder(request.Scheme, request.Host.Host)
        {
            Path = pathWithSsr,
            Query = request.QueryString.Value ?? ""
        }.ToString();
    }

    private static string Truncate(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input) || maxLength <= 0)
        {
            return input;
        }

        if (input.Length <= maxLength)
        {
            return input;
        }

        return input.Substring(0, maxLength);
    }
}