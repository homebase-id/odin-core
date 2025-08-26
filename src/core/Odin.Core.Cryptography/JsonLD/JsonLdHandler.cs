/**
 * Parts of the source code in this file has been translated/ported from jsonld.js library by Digital Bazaar (BSD 3-Clause license)
*/

using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JsonLd.Normalization
{
    public static class JsonLdHandler
    {
        /// <summary>
        /// Implements URDNA2015 normalization/canonicalization algorithm based on jsonld.js v5.2.0 implementation, 
        /// the same as Normalize
        /// </summary>
        /// <param name="json">serialized json document</param>
        /// <param name="options">options to be used during the document expansion process</param>
        /// <returns>normalized n-quads document as string</returns>
        public static async Task<string> Canonize(string json, ExpandOptions options = null)
        {
            return await NormalizeAsync(json, options);
        }

        /// <summary>
        /// Implements URDNA2015 normalization/canonicalization algorithm based on jsonld.js v5.2.0 implementation,
        /// the same as Canonize
        /// </summary>
        /// <param name="json">serialized json document</param>
        /// <param name="options">options to be used during the document expansion process</param>
        /// <returns>normalized n-quads document as string</returns>
        public static async Task<string> NormalizeAsync(string json, ExpandOptions options = null)
        {
            var dataset = await ToRDF(json, options);
            return URDNA2015.Normalize(dataset);
        }

        private static async Task<List<Quad>> ToRDF(string json, ExpandOptions options = null)
        {
            var expanded = await Expand(json, options);
            return expanded.ToRDF();
        }

        private static async Task<JToken> Expand(string json, ExpandOptions options = null)
        {
            var activeCtx = new ExpandContext();
            var token = JToken.Parse(json);
            var objects = new List<JObject>();
            if (token.Type == JTokenType.Array)
                objects.AddRange(token.ToArray().Where(t => t.Type == JTokenType.Object).Cast<JObject>());
            if (token.Type == JTokenType.Object)
                objects.Add((JObject)token);
            var result = new JArray();
            foreach (var doc in objects)
            {
                options ??= new();
                var expanded = await Expansion.Expand(activeCtx, doc, null, options);

                // optimize away @graph with no other properties
                if (expanded?.Type == JTokenType.Object)
                {
                    var expandedObj = (JObject)expanded;
                    if (expandedObj.TryGetValue("@graph", out var graphProp) && expandedObj.Properties().Count() == 1)
                        expanded = graphProp;
                }

                if (expanded != null)
                    result.Add(expanded);
            }

            return result;
        }
    }
}
