using System.Collections.Generic;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Ganss.Xss;
using System.Linq;
using System;
using System.Web;
using System.Net;

namespace Odin.Services.LinkMetaExtractor;



public static class Parser
{
    private const int MaxContentLength = 2 * 1024 * 1024 + 500; // We can accept embedded images up to 2MB and a little
    private static readonly HashSet<string> InterestedInPrefixes = new HashSet<string> { "og", "twitter" };
    private static readonly HashSet<string> MetaAttributes = new HashSet<string> { "name", "property" };


    public static string RemoveControlCharacters(string content)
    {
        // Clean out control characters
        return Regex.Replace(content, @"[\x00-\x1F\x7F]", "");
    }


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
        value = RemoveControlCharacters(WebUtility.HtmlDecode(value));

        if (string.IsNullOrWhiteSpace(value))
            return;

        if (value.Length > MaxContentLength)
            value = value.Substring(0, MaxContentLength - 3) + "...";

        value = value.Trim();
        key = key.Trim().ToLower();

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
