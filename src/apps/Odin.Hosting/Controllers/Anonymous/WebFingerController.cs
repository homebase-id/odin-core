using System.Collections.Generic;
using System.Text.Json.Serialization;
using MessagePack.Formatters;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Optimization.Cdn;

namespace Odin.Hosting.Controllers.Anonymous;

// Make routes in here are:
// - are accessible without authentication
// - are accessible using http

[ApiController]
[Route(".well-known/webfinger")]
public class WebFingerController(OdinContext context) : ControllerBase
{
    staticFileContentService


    [HttpGet]
    public IActionResult Get()
    {
        var domain = context.Tenant.DomainName;

        var response = new WebFingerResponse
        {
            Subject = $"acct:@{domain}",
            Aliases =
            [
                $"https://{domain}/",
                $"acct:@{domain}"
            ],
            Properties = new Dictionary<string, string>
            {
                { "http://schema.org/name", "John Doe" }, // SEB:TODO
                { "http://schema.org/url", $"https://{domain}/" },
                { "http://schema.org/email", "contact@john.doe.com" } // SEB:TODO
            },
            Links =
            [
                new WebFingerLink
                {
                    Rel = "self",
                    Type = "application/json",
                    Href = "https://john.doe.com/.well-known/webfinger",
                },

                new WebFingerLink
                {
                    Rel = "profile",
                    Type = "text/html",
                    Href = "https://john.doe.com/",
                },

                new WebFingerLink
                {
                    Rel = "avatar",
                    Type = "image/jpeg\"",
                    Href = "https://john.doe.com/avatar.jpg",
                },

                new WebFingerLink
                {
                    Rel = "me",
                    Type = "text/html",
                    Href = "https://github.com/johndoe/",

                    Titles = new Dictionary<string, string>
                    {
                        { "default", "Github Profile" }
                    }
                },

                new WebFingerLink
                {
                    Rel = "me",
                    Type = "text/html",
                    Href = "https://facebook.com/johndoe/",

                    Titles = new Dictionary<string, string>
                    {
                        { "default", "Facebook Profile" }
                    }
                }
            ]
        };

        return Ok(response);
    }
}

internal class WebFingerResponse
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; }

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = new();

    [JsonPropertyName("links")]
    public List<WebFingerLink> Links { get; set; } = [];
}

internal class WebFingerLink
{
    [JsonPropertyName("rel")]
    public string Rel { get; set; }

    [JsonPropertyName("href")]
    public string Href { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("titles")]
    public Dictionary<string, string> Titles { get; set; } = new();
}

