using System.Collections.Generic;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Ganss.Xss;

namespace Odin.Services.LinkMetaExtractor;

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
      
}