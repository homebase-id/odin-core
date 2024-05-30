using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Odin.Services.LinkMetaExtractor;

public static class Parser
{
      /// <summary>
        /// Parse content into a dictionary.
        /// </summary>
        /// <param name="content">The HTML string.</param>
        /// <returns>Dictionary with the parsed content.</returns>
        public static Dictionary<string, object> Parse(string content)
        {
            var doc = new HtmlDocument();

            // Fudge to handle a situation when an encoding isn't present
            if (!content.Contains("xml encoding="))
            {
                content = "<?xml encoding=\"utf-8\" ?>" + content;
            }

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
                                ogp[metaBits[0]] = contentAttribute;
                            }
                        }
                    }
                }
            }

            // OEmbed
            var links = doc.DocumentNode.SelectNodes("//link");
            if (links != null)
            {
                foreach (var link in links)
                {
                    var relAttribute = link.GetAttributeValue("rel", null);
                    if (string.Equals(relAttribute, "alternate", StringComparison.OrdinalIgnoreCase))
                    {
                        var typeAttribute = link.GetAttributeValue("type", null)?.ToLower();
                        var hrefAttribute = link.GetAttributeValue("href", null);
                        if (hrefAttribute == null) continue;

                        if (typeAttribute == "application/json+oembed")
                        {
                            AddToOEmbed(ogp, "jsonp", hrefAttribute);
                        }
                        else if (typeAttribute == "text/json+oembed")
                        {
                            AddToOEmbed(ogp, "json", hrefAttribute);
                        }
                        else if (typeAttribute == "text/xml+oembed")
                        {
                            AddToOEmbed(ogp, "xml", hrefAttribute);
                        }
                    }
                }

                ogp = ParseTwitterOEmbed(links, ogp);
            }

            // Basics
            foreach (var basic in new[] { "title" })
            {
                var match = Regex.Match(content, $"<{basic}>(.*?)</{basic}>", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    ogp[basic] = match.Groups[1].Value.Trim();
                }
            }

            if (metas != null)
            {
                foreach (var meta in metas)
                {
                    var nameAttribute = meta.GetAttributeValue("name", null)?.ToLower();
                    if (nameAttribute == "description")
                    {
                        ogp["description"] = meta.GetAttributeValue("content", null);
                    }
                    else if (nameAttribute == "keywords")
                    {
                        ogp["keywords"] = meta.GetAttributeValue("content", null);
                    }
                }
            }

            return ogp;
        }

        private static void AddToOEmbed(Dictionary<string, object> ogp, string type, string href)
        {
            if (!ogp.ContainsKey("oembed"))
            {
                ogp["oembed"] = new Dictionary<string, List<string>>();
            }

            var oembed = (Dictionary<string, List<string>>)ogp["oembed"];
            if (!oembed.ContainsKey(type))
            {
                oembed[type] = new List<string>();
            }

            oembed[type].Add(href);
        }

        private static Dictionary<string, object> ParseTwitterOEmbed(HtmlNodeCollection metas, Dictionary<string, object> ogp)
        {
            if (ogp.ContainsKey("oembed") && ((Dictionary<string, List<string>>)ogp["oembed"]).ContainsKey("jsonp"))
            {
                return ogp;
            }

            var canonicalLinks = metas
                .Where(meta => meta.GetAttributeValue("rel", null) == "canonical")
                .ToList();

            if (canonicalLinks.Count > 0)
            {
                var firstCanonicalLink = canonicalLinks[0].GetAttributeValue("href", null);
                if (!string.IsNullOrWhiteSpace(firstCanonicalLink) && Regex.IsMatch(firstCanonicalLink, @"^https://(www\.|mobile\.)?twitter\.com", RegexOptions.IgnoreCase))
                {
                    ogp["oembed"] = new Dictionary<string, List<string>>
                    {
                        { "jsonp", new List<string> { $"https://publish.twitter.com/oembed?url={firstCanonicalLink}&align=center" } }
                    };
                }
            }

            return ogp;
        }
}