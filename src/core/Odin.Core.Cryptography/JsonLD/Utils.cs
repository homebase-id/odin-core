/**
 * Parts of the source code in this file has been translated/ported from jsonld.js and rdf-canonize libraries by Digital Bazaar (BSD 3-Clause license)
*/

using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace JsonLd.Normalization
{
    internal static class Utils
    {
        public static JToken[] NormalizeContext(JToken context)
        {   // normalize local context to an array of @context objects
            // process `@context`
            if (context.Type == JTokenType.Object &&
                ((JObject)context).TryGetValue("@context", out var contextCtx) &&
                contextCtx.Type == JTokenType.Array)
            {
                context = contextCtx;
            }
            // context is one or more contexts
            return context.Type == JTokenType.Array ? context.ToArray() : new[] { context };
        }

        public static string PrependBase(string baseVal, string iri)
        {
            if (string.IsNullOrEmpty(baseVal))
                return iri;
            return PrependBase(ParseUri(baseVal), iri);
        }
        public static string PrependBase(Dictionary<string, string> baseVal, string iri)
        {
            // skip IRI processing
            if (baseVal == null)
                return iri;

            // already an absolute IRI
            if (AbsoluteIriRegex.IsMatch(iri))
                return iri;

            // parse given IRI
            var rel = ParseUri(iri);

            // per RFC3986 5.2.2
            var transform = new Dictionary<string, string>() {
                { "protocol", GetFromDict(baseVal, "protocol", "") }
            };

            var relAuthority = GetFromDict(rel, "authority");
            var relPath = GetFromDict(rel, "path");
            var relQuery = GetFromDict(rel, "query");
            if (relAuthority is not null)
            {
                transform["authority"] = relAuthority;
                transform["path"] = relPath;
                transform["query"] = relQuery;
            }
            else
            {
                transform["authority"] = GetFromDict(baseVal, "authority");

                if ((relPath ?? "") == "")
                {
                    transform["path"] = GetFromDict(baseVal, "path");
                    if (relQuery is not null)
                        transform["query"] = relQuery;
                    else
                        transform["query"] = GetFromDict(baseVal, "query");
                }
                else
                {
                    if (relPath.IndexOf('/') == 0)
                        transform["path"] = relPath;// IRI represents an absolute path
                    else
                    {
                        // merge paths
                        var path = GetFromDict(baseVal, "path");

                        // append relative path to the end of the last directory from base
                        path = path.Substring(0, path.LastIndexOf('/') + 1);
                        if ((path.Length > 0 || (GetFromDict(baseVal, "authority")?.Any() ?? false)) && !path.EndsWith('/'))
                            path += '/';
                        path += relPath;

                        transform["path"] = path;
                    }
                    transform["query"] = relQuery;
                }
            }

            if ((relPath ?? "") != "")
            {
                // remove slashes and dots in path
                transform["path"] = RemoveDotSegments(transform["path"]);
            }

            // construct URL
            var rval = transform["protocol"];
            if (GetFromDict(transform, "authority") is not null)
                rval += "//" + transform["authority"];

            rval += transform["path"];
            if (GetFromDict(transform, "query") is not null)
                rval += '?' + transform["query"];

            if (GetFromDict(rel, "fragment") is not null)
                rval += '#' + rel["fragment"];

            // handle empty base
            if (rval == "")
                rval = "./";

            return rval;
        }

        public static JArray AsArray(JToken token)
        {
            if (token.Type == JTokenType.Array)
                return (JArray)token;
            var newArray = new JArray();
            newArray.Add(token);
            return newArray;
        }

        public static bool IsKeyword(string v)
        {
            if (string.IsNullOrEmpty(v))
                return false;
            if (v[0] != '@')
                return false;
            switch (v)
            {
                case "@base":
                case "@container":
                case "@context":
                case "@default":
                case "@direction":
                case "@embed":
                case "@explicit":
                case "@graph":
                case "@id":
                case "@included":
                case "@index":
                case "@json":
                case "@language":
                case "@list":
                case "@nest":
                case "@none":
                case "@omitDefault":
                case "@prefix":
                case "@preserve":
                case "@protected":
                case "@requireAll":
                case "@reverse":
                case "@set":
                case "@type":
                case "@value":
                case "@version":
                case "@vocab":
                    return true;
            }
            return false;
        }

        // define URL parser
        // parseUri 1.2.2
        // (c) Steven Levithan <stevenlevithan.com>
        // MIT License
        // with local jsonld.js modifications
        private static string[] UriKeys = {
            "href", "protocol", "scheme", "authority", "auth", "user", "password",
            "hostname", "port", "path", "directory", "file", "query", "fragment"
        };
        private static Regex UriRegex = new(@"^(([^:\/?#]+):)?(?:\/\/((?:(([^:@]*)(?::([^:@]*))?)?@)?([^:\/?#]*)(?::(\d*))?))?(?:(((?:[^?#\/]*\/)*)([^?#]*))(?:\?([^#]*))?(?:#(.*))?)");

        public static Dictionary<string, string> ParseUri(string str)
        {
            var parsed = new Dictionary<string, string>();
            var m = UriRegex.Match(str);
            var i = UriKeys.Length;
            while (i-- > 0)
            {
                parsed[UriKeys[i]] = m.Groups[i].Success ? m.Groups[i].Value : null;
            }

            // remove default ports in found in URLs
            if (parsed["scheme"] == "https" && parsed["port"] == "443" ||
              parsed["scheme"] == "http" && parsed["port"] == "80")
            {
                parsed["href"] = parsed["href"].Replace(":" + parsed["port"], "");
                parsed["authority"] = parsed["authority"].Replace(":" + parsed["port"], "");
                parsed["port"] = null;
            }

            parsed["normalizedPath"] = RemoveDotSegments(parsed["path"]);
            return parsed;
        }

        public static string RemoveDotSegments(string path)
        {
            // RFC 3986 5.2.4 (reworked)

            // empty path shortcut
            if (!path.Any())
                return "";

            var input = path.Split('/');
            var output = new List<string>();

            for (int i = 0; i < input.Length; ++i)
            {
                var next = input[i];
                var done = input.Length == i + 1;

                if (next == ".")
                {
                    if (done)
                        output.Add("");// ensure output has trailing /
                    continue;
                }

                if (next == "..")
                {
                    if (output.Count > 0)
                        output.RemoveAt(output.Count - 1);
                    if (done)
                        output.Add("");// ensure output has trailing /
                    continue;
                }

                output.Add(next);
            }

            // if path was absolute, ensure output has leading /
            if (path[0] == '/' && output.Any() && output[0] != "")
                output = output.Prepend("").ToList();

            if (output.Count == 1 && output[0] == "")
                return "/";

            return string.Join("/", output);
        }

        public static bool IsEmptyObject(JToken element) => element is null || element.Type == JTokenType.Null ||
                                                                               element.Type == JTokenType.Undefined;

        public static bool IsBlankNode(JToken v)
        {
            // Note: A value is a blank node if all of these hold true:
            // 1. It is an Object.
            // 2. If it has an @id key its value begins with '_:'.
            // 3. It has no keys OR is not a @value, @set, or @list.
            if (v.Type == JTokenType.Object)
            {
                var obj = (JObject)v;
                if (obj.TryGetValue("@id", out var idProp))
                    return idProp.Type == JTokenType.String && idProp.Value<string>().IndexOf("_:") == 0;

                return !obj.Properties().Any() ||
                       !(obj.ContainsKey("@value") || obj.ContainsKey("@set") || obj.ContainsKey("@list"));
            }
            return false;
        }

        public static bool IsGraphSubject(JToken v)
        {
            // Note: A value is a subject if all of these hold true:
            // 1. It is an Object.
            // 2. It is not a @value, @set, or @list.
            // 3. It has more than 1 key OR any existing key is not @id.
            if (v.Type == JTokenType.Object)
            {
                var obj = (JObject)v;
                if (!(obj.ContainsKey("@value") || obj.ContainsKey("@set") || obj.ContainsKey("@list")))
                {
                    var keyCount = obj.Properties().Count();
                    return keyCount > 1 || !obj.ContainsKey("@id");
                }
            }
            return false;
        }

        public static bool IsGraphSubjectReference(JToken v)
        {
            // Note: A value is a subject reference if all of these hold true:
            // 1. It is an Object.
            // 2. It has a single key: @id.
            if (v.Type == JTokenType.Object)
            {
                var obj = (JObject)v;
                return obj.Properties().Count() == 1 && obj.ContainsKey("@id");
            }
            return false;
        }

        public static bool IsGraphValue(JToken v)
        {
            // Note: A value is a @value if all of these hold true:
            // 1. It is an Object.
            // 2. It has the @value property.
            return v.Type == JTokenType.Object && ((JObject)v).ContainsKey("@value");
        }

        public static bool IsGraphList(JToken v)
        {
            // Note: A value is a @list if all of these hold true:
            // 1. It is an Object.
            // 2. It has the @list property.
            return v.Type == JTokenType.Object && ((JObject)v).ContainsKey("@list");
        }

        public static bool IsGraph(JToken v)
        {
            // Note: A value is a graph if all of these hold true:
            // 1. It is an object.
            // 2. It has an `@graph` key.
            // 3. It may have '@id' or '@index'
            return v.Type == JTokenType.Object && ((JObject)v).ContainsKey("@graph") &&
              ((JObject)v).Properties().Where(prop => prop.Name != "@id" && prop.Name != "@index").Count() == 1;
        }

        public static bool HasGraphValue(JObject subject, string property, JToken value)
        {
            if (HasProperty(subject, property))
            {
                var val = subject[property];
                var isList = IsGraphList(val);
                if (val.Type == JTokenType.Array || isList)
                {
                    if (isList)
                    {
                        val = val["@list"];
                        if (val.Type != JTokenType.Array)
                            throw new JsonLdParseException($"\"@list\" field in \"{property}\" has to be an array");
                    }
                    var valArray = (JArray)val;
                    for (var i = 0; i < valArray.Count; ++i)
                    {
                        if (CompareValues(value, valArray[i]))
                            return true;
                    }
                }
                else if (value.Type != JTokenType.Array)
                {
                    // avoid matching the set of values with an array value parameter
                    return CompareValues(value, val);
                }
            }
            return false;
        }

        public static bool HasProperty(JObject subject, string property)
        {
            if (subject.TryGetValue(property, out var propertyProp))
            {
                return propertyProp.Type != JTokenType.Array || ((JArray)propertyProp).Count > 0;
            }
            return false;
        }

        public static bool CompareValues(JToken v1, JToken v2)
        {
            // 1. equal primitives
            if (v1 == v2 || JToken.EqualityComparer.Equals(v1, v2))
            {
                return true;
            }

            // 2. equal @values
            if (IsGraphValue(v1) && IsGraphValue(v2) &&
              v1["@value"] == v2["@value"] &&
              v1["@type"] == v2["@type"] &&
              v1["@language"] == v2["@language"] &&
              v1["@index"] == v2["@index"])
            {
                return true;
            }

            // 3. equal @ids
            if (v1.Type == JTokenType.Object && ((JObject)v1).TryGetValue("@id", out var v1IdProp) &&
                v2.Type == JTokenType.Object && ((JObject)v2).TryGetValue("@id", out var v2IdProp))
            {
                return v1IdProp == v2IdProp;
            }

            return false;
        }

        public static void AddValue(JObject subject, string property, JToken value,
                                    bool propertyIsArray = false, bool allowDuplicate = false,
                                    bool valueIsArray = false, bool prependValue = false)
        {
            if (valueIsArray)
            {
                subject[property] = value;
            }
            else if (value.Type == JTokenType.Array)
            {
                var valueArray = (JArray)value;
                if (valueArray.Count == 0 && propertyIsArray && !subject.ContainsKey(property))
                {
                    subject[property] = new JArray();
                }
                if (prependValue)
                {
                    if (subject.TryGetValue(property, out var prevValsProp) && prevValsProp.Type == JTokenType.Array)
                    {
                        foreach (var prevVal in prevValsProp.ToArray())
                            valueArray.Add(prevVal);
                    }
                    subject[property] = new JArray();
                }
                for (var i = 0; i < valueArray.Count; ++i)
                {
                    AddValue(subject, property, valueArray[i], propertyIsArray, allowDuplicate, valueIsArray, prependValue);
                }
            }
            else if (subject.TryGetValue(property, out var propertyProp))
            {
                // check if subject already has value if duplicates not allowed
                var hasValue = !allowDuplicate && HasGraphValue(subject, property, value);

                // make property an array if value not present or always an array
                if (propertyProp.Type != JTokenType.Array && (!hasValue || propertyIsArray))
                {
                    var newArray = new JArray();
                    newArray.Add(propertyProp);
                    propertyProp = subject[property] = newArray;
                }

                // add new value
                if (!hasValue)
                {
                    if (prependValue)
                        ((JArray)propertyProp).AddFirst(value);
                    else
                        ((JArray)propertyProp).Add(value);
                }
            }
            else
            {
                // add new value as set or single value
                if (propertyIsArray)
                {
                    var newArray = new JArray();
                    newArray.Add(value);
                    subject[property] = newArray;
                }
                else
                    subject[property] = value;
            }
        }

        public static V GetFromDict<T, V>(Dictionary<T, V> dict, T key, V defVal = default) where V : class
        {
            if (dict.TryGetValue(key, out V val) && val is not null)
                return val;
            return defVal;
        }

        private static Regex IriKeywordRegex = new("^@[a-zA-Z]+$");
        private static Regex AbsoluteIriRegex = new(@"^([A-Za-z][A-Za-z0-9+-.]*|_):[^\s]*$");
        public static bool IsIriKeyword(string value)
        {
            return IriKeywordRegex.IsMatch(value);
        }
        public static bool IsIriAbsolute(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            return AbsoluteIriRegex.IsMatch(value);
        }
    }
}
