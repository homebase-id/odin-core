using System.Collections.Generic;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Ganss.Xss;
using System.Linq;
using System;
using System.Web;

namespace Odin.Services.LinkMetaExtractor;



public static class Parser
{
    private static readonly HtmlSanitizer Sanitizer = new HtmlSanitizer();
    private const int MaxContentLength = 8192;
    private static readonly HashSet<string> InterestedInPrefixes = new HashSet<string> { "og", "twitter" };
    private static readonly HashSet<string> MetaAttributes = new HashSet<string> { "name", "property" };

    public static Dictionary<string, object> Parse(string content)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(content);

        var metadata = new Dictionary<string, object>();

        // Parse all meta tags once
        var metaNodes = doc.DocumentNode.SelectNodes("//meta");
        if (metaNodes != null)
        {
            foreach (var meta in metaNodes)
            {
                foreach (var attribute in MetaAttributes)
                {
                    var metaKey = meta.GetAttributeValue(attribute, null);
                    if (metaKey == null) 
                        continue;

                    metaKey = metaKey.Trim().ToLower();

                    if (!InterestedInPrefixes.Any(prefix => metaKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var contentValue = meta.GetAttributeValue("content", null);
                    if (string.IsNullOrWhiteSpace(contentValue)) 
                        continue;

                    AddOrAppend(metadata, metaKey, contentValue);
                }
            }
        }

        // Parse title
        ParseNode(doc.DocumentNode.SelectSingleNode("//title"), "title", metadata);

        // Parse description
        var descriptionNode = metaNodes?.FirstOrDefault(
            node => node.GetAttributeValue("name", "").Trim().Equals("description", StringComparison.OrdinalIgnoreCase)
        );
        ParseNode(descriptionNode, "description", metadata);
        return metadata;
    }


    private static void ParseNode(HtmlNode node, string key, Dictionary<string, object> dict)
    {
        if (node == null) return;

        var content = node.GetAttributeValue("content", null) ?? node.InnerText;
        if (!string.IsNullOrWhiteSpace(content))
        {
            AddOrAppend(dict, key, content);
        }
    }

    private static void AddOrAppend(Dictionary<string, object> dict, string key, string value)
    {
        // Pick a max value large enough for a huge GET URL (which might be a meta parameter)
        // Some providers like for example Instagram embed the image as data:.... which can be huge
        // If we want to support that someday then we'll need to change this to whatever max we want
        // to have and return the larger data.
        //
        if (value.Length > MaxContentLength)
            value = value.Substring(0, MaxContentLength - 3) + "...";

        value = HttpUtility.HtmlEncode(HttpUtility.HtmlDecode(Sanitizer.Sanitize(value)).Trim());

        if (!dict.ContainsKey(key))
        {
            dict[key] = value;
        }
        else if (dict[key] is List<string> list)
        {
            list.Add(value);
        }
        else
        {
            dict[key] = new List<string> { dict[key].ToString(), value };
        }
    }
}

/*

public static class Parser
{
    
    private static readonly HtmlSanitizer Sanitizer = new HtmlSanitizer();
    
        /// <summary>
        /// Parse content into a dictionary.
        /// </summary>
        /// <param name="content">The HTML string.</param>
        /// <returns>Dictionary with the parsed content.</returns>
        public static Dictionary<string, object> Parse(string content)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            var interestedIn = new List<string> { "og", "twitter" };
            var ogp = new Dictionary<string, object>();

            // Open graph
            var metas = doc.DocumentNode.SelectNodes("//meta");
            if (metas != null)
            {
                foreach (var meta in metas)
                {
                    foreach (var name in new[] { "name", "property" })
                    {
                        var metaAttribute = meta.GetAttributeValue(name, null);
                        if (metaAttribute == null) continue;

                        var metaBits = metaAttribute.Split(':');
                        if (interestedIn.Contains(metaBits[0]))
                        {
                            var contentAttribute = meta.GetAttributeValue("content", null);
                            if (contentAttribute == null) continue;
                            contentAttribute = Sanitizer.Sanitize(contentAttribute);
                            if (ogp.ContainsKey(metaAttribute) && !(ogp[metaAttribute] is List<string>))
                            {
                                ogp[metaBits[0]] = new List<string> { ogp[metaAttribute].ToString(), contentAttribute };
                            }
                            else if (ogp.ContainsKey(metaAttribute) && ogp[metaAttribute] is List<string>)
                            {
                                ((List<string>)ogp[metaAttribute]).Add(contentAttribute);
                            }
                            else
                            {
                                ogp[metaAttribute] = contentAttribute;
                            }
                        }
                    }
                }
            }


            // Basic Meta
            foreach (var basic in new[] { "title" })
            {
                var match = Regex.Match(content, $"<{basic}>(.*?)</{basic}>", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    ogp[basic] = Sanitizer.Sanitize(match.Groups[1].Value.Trim());
                }
            }

            if (metas != null)
            {
                foreach (var meta in metas)
                {
                    var nameAttribute = meta.GetAttributeValue("name", null)?.ToLower();
                    if (nameAttribute == "description")
                    {
                        ogp["description"] = Sanitizer.Sanitize(meta.GetAttributeValue("content", null));
                    }
                   
                }
            }

            return ogp;
        }
      
}*/