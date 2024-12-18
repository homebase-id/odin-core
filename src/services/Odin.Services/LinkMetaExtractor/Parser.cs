using System.Collections.Generic;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Ganss.Xss;
using System.Linq;
using System;

namespace Odin.Services.LinkMetaExtractor;



public static class Parser
{
    private static readonly HtmlSanitizer Sanitizer = new HtmlSanitizer();

    /// <summary>
    /// Parse content into a dictionary of metadata.
    /// </summary>
    /// <param name="content">The HTML string.</param>
    /// <returns>Dictionary with the parsed metadata.</returns>
    public static Dictionary<string, object> Parse(string content)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(content);

        var metadata = new Dictionary<string, object>();
        var interestedIn = new List<string> { "og", "twitter" };

        // Parse all meta tags once
        var metaNodes = doc.DocumentNode.SelectNodes("//meta");
        if (metaNodes != null)
        {
            foreach (var meta in metaNodes)
            {
                var attributes = new[] { "name", "property" };
                foreach (var attribute in attributes)
                {
                    // var metaKey = meta.GetAttributeValue(attribute, null);
                    // if (metaKey == null || !interestedIn.Any(metaKey.StartsWith)) continue;

                    var metaKey = meta.GetAttributeValue(attribute, null);
                    if (metaKey == null) continue;
                    metaKey = metaKey.Trim().ToLower();
                    if (!interestedIn.Any(prefix => metaKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;

                    var contentValue = meta.GetAttributeValue("content", null);
                    if (string.IsNullOrWhiteSpace(contentValue)) continue;

                    contentValue = Sanitizer.Sanitize(contentValue);
                    AddOrAppend(metadata, metaKey, contentValue);
                }
            }
        }

        // Parse title
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
        {
            metadata["title"] = Sanitizer.Sanitize(titleNode.InnerText.Trim());
        }

        // Parse description
        var descriptionNode = metaNodes?.FirstOrDefault(
            node => node.GetAttributeValue("name", "").Equals("description", System.StringComparison.OrdinalIgnoreCase)
        );
        if (descriptionNode != null)
        {
            var descriptionContent = descriptionNode.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(descriptionContent))
            {
                metadata["description"] = Sanitizer.Sanitize(descriptionContent).Trim();
            }
        }

        return metadata;
    }

    /// <summary>
    /// Adds or appends a value to a dictionary key.
    /// </summary>
    private static void AddOrAppend(Dictionary<string, object> dict, string key, string value)
    {
        value = value.Trim();
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