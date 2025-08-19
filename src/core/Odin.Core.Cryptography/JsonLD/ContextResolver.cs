/**
 * Parts of the source code in this file has been translated/ported from jsonld.js library by Digital Bazaar (BSD 3-Clause license)
*/

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JsonLd.Normalization
{
    public interface IContextResolver
    {
        Task<List<ResolvedContext>> Resolve(ExpandContext activeCtx, JToken context, string baseUrl);
    }

    public class ResolvedContext
    {
        public JToken Document { get; init; }
        protected Dictionary<ExpandContext, ExpandContext> Cache { get; init; } = new();

        public ResolvedContext(JToken document)
        {
            Document = document;
            // TODO: enable customization of processed context cache
            // TODO: limit based on size of processed contexts vs. number of them
            //this.cache = new LRU({ max: MAX_ACTIVE_CONTEXTS});
        }

        public ExpandContext GetProcessed(ExpandContext activeCtx)
        {
            Cache.TryGetValue(activeCtx, out var result);
            return result;
        }

        public void SetProcessed(ExpandContext activeCtx, ExpandContext processedCtx)
        {
            Cache[activeCtx] = processedCtx;
        }
    };

    public class ContextResolver : IContextResolver
    {
        protected static readonly ConcurrentDictionary<string, (string, string, DateTime)> documentCache = new();
        protected static readonly TimeSpan CACHE_TIMEOUT = TimeSpan.FromMinutes(5);

        protected const int MAX_CONTEXT_URLS = 10;
        protected const int MAX_REDIRECTS = 10;

        public virtual async Task<List<ResolvedContext>> Resolve(ExpandContext activeCtx, JToken context, string baseUrl)
        {
            return await Resolve(activeCtx, context, baseUrl, null);
        }
        public virtual async Task<List<ResolvedContext>> Resolve(ExpandContext activeCtx, JToken context, 
                                                                 string baseUrl, HashSet<object> cycles)
        {
            cycles ??= new();

            var ctxs = Utils.NormalizeContext(context);

            // resolve each context in the array
            var allResolved = new List<ResolvedContext>();
            foreach (var ctx in ctxs)
            {
                if (ctx.Type == JTokenType.String)
                {
                    var ctxVal = ctx.Value<string>();
                    // see if `ctx` has been resolved before...
                    var resolvedCtx = Get(ctxVal);
                    if (resolvedCtx is not null)
                        allResolved.Add(resolvedCtx);
                    else // not resolved yet, resolve
                        allResolved.AddRange(await ResolveRemoteContext(activeCtx, ctxVal, baseUrl, cycles));

                    continue; // added to output - continue
                }
                if (ctx == null || Utils.IsEmptyObject(ctx))
                {
                    // handle `null` context, nothing to cache
                    allResolved.Add(new ResolvedContext(null));
                    continue;
                }
                if (ctx.Type != JTokenType.Object)
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @context must be an object.");

                // context is an object, get/create `ResolvedContext` for it
                var key = ctx.ToString();
                var resolved = Get(key);
                if (resolved is null)
                {
                    // create a new static `ResolvedContext` and cache it
                    resolved = new ResolvedContext(ctx);
                    CacheResolvedContext(key, resolved, "static");
                }
                allResolved.Add(resolved);
            }

            return allResolved;
        }

        protected readonly Dictionary<string, ResolvedContext> OperationCache = new();
        protected readonly Dictionary<string, Dictionary<string, ResolvedContext>> SharedCache = new();

        protected ResolvedContext Get(string key)
        {
            // get key from per operation cache; no `tag` is used with this cache so
            // any retrieved context will always be the same during a single operation
            if (!OperationCache.TryGetValue(key, out var resolved))
            {
                // see if the shared cache has a `static` entry for this URL
                if (SharedCache.TryGetValue(key, out var tagMap))
                {
                    if (tagMap.TryGetValue("static", out resolved))
                        OperationCache[key] = resolved;
                }
            }
            return resolved;
        }

        protected virtual async Task<List<ResolvedContext>> ResolveRemoteContext(ExpandContext activeCtx, string url,
                                                                                 string baseUrl, HashSet<object> cycles)
        {
            url = Utils.PrependBase(baseUrl, url);
            var (context, docUrl) = await FetchContext(activeCtx, url, cycles);

            // update base according to remote document and resolve any relative URLs
            baseUrl = !string.IsNullOrEmpty(docUrl) ? docUrl : url;
            ResolveContextUrls(context, baseUrl);

            // resolve, cache, and return context
            var resolved = await Resolve(activeCtx, context, baseUrl, cycles);
            foreach (var ctx in resolved)
                CacheResolvedContext(url, ctx);
            return resolved;
        }

        protected virtual async Task<(JObject, string)> FetchContext(ExpandContext activeCtx, string url, HashSet<object> cycles)
        {
            // check for max context URLs fetched during a resolve operation
            if (cycles.Count > MAX_CONTEXT_URLS)
                throw new JsonLdParseException("Maximum number of @context URLs exceeded.");

            // check for context URL cycle
            // shortcut to avoid extra work that would eventually hit the max above
            if (cycles.Contains(url))
                throw new JsonLdParseException("Cyclical @context URLs detected.");

            // track cycles
            cycles.Add(url);

            string docBody, docUrl;
            JToken context;
            try
            {
                var fromCache = documentCache.TryGetValue(url, out var cached) &&
                                DateTime.UtcNow - cached.Item3 < CACHE_TIMEOUT;
                if (fromCache)
                    (docBody, docUrl) = (cached.Item1, cached.Item2);
                else
                    (docBody, docUrl) = await LoadDocument(url);

                // parse string context as JSON
                context = JToken.Parse(docBody);
                if (!fromCache)
                    documentCache[url] = (docBody, docUrl, DateTime.UtcNow);
            }
            catch (Exception e)
            {
                throw new JsonLdParseException(
                  "Dereferencing a URL did not result in a valid JSON-LD object. " +
                  "Possible causes are an inaccessible URL perhaps due to " +
                  "a same-origin policy (ensure the server uses CORS if you are " +
                  "using client-side JavaScript), too many redirects, a " +
                  "non-JSON response, or more than one HTTP Link Header was " +
                  "provided for a remote context.",
                  e);
            }

            // ensure ctx is an object
            if (context.Type != JTokenType.Object)
            {
                throw new JsonLdParseException("Dereferencing a URL did not result in a JSON object. " +
                                               "The response was valid JSON, but it was not a JSON object.");
            }

            var result = new JObject();
            // use empty context if no @context key is present
            if (((JObject)context).TryGetValue("@context", out var contextProp))
                result["@context"] = contextProp;
            else
                result["@context"] = new JObject();

            //// append @context URL to context if given
            //if (remoteDoc.contextUrl)
            //{
            //    if (!_isArray(result['@context']))
            //    {
            //        result['@context'] = [result['@context']];
            //    }
            //    result['@context'].push(remoteDoc.contextUrl);
            //}

            return (result, docUrl);
        }

        protected static readonly Regex JsonContentTypeRegex = new(@"^application\/(\w*\+)?json$");

        protected virtual async Task<(string, string)> LoadDocument(string url, List<string> redirects = null)
        {
            redirects ??= new();
            JToken alternate = null;
            var client = new HttpClient();
            var res = await client.GetAsync(url);

            // handle error
            if ((int)res.StatusCode >= 400)
                throw new JsonLdParseException($"URL '{url}' could not be dereferenced: {res.StatusCode}");

            var link = res.Headers.TryGetValues("link", out var links) ? links.FirstOrDefault() : null;
            var location = res.Headers.TryGetValues("location", out var locations) ? locations.FirstOrDefault() : null;
            var contentType = res.Headers.TryGetValues("content-type", out var contentTypes) ? contentTypes.FirstOrDefault() : null;

            // handle Link Header
            if (link is not null && contentType != "application/ld+json")
            {
                // only 1 related link header permitted
                var linkHeaders = ParseLinkHeader(link);
                //if (linkHeaders.TryGetValue(LINK_HEADER_CONTEXT, out var linkedContext))
                //{
                //    if (linkedContext.Type == JTokenType.Array)
                //        throw new JsonLdParseException("URL could not be dereferenced, it has more than one associated HTTP Link Header.");
                //    doc.contextUrl = linkedContext.target;
                //}

                // "alternate" link header is a redirect
                if (linkHeaders.TryGetValue("alternate", out alternate) &&
                    alternate.Type == JTokenType.Object)
                {
                    var alternateObj = (JObject)alternate;
                    if (alternateObj.TryGetValue("type", out var typeToken) &&
                        typeToken.Value<string>() == "application/ld+json" &&
                        !JsonContentTypeRegex.IsMatch(contentType ?? "") &&
                        alternateObj.TryGetValue("target", out var targetToken))
                    {
                        location = Utils.PrependBase(url, targetToken.Value<string>());
                    }
                }
            }

            // handle redirect
            if ((alternate is not null || (int)res.StatusCode >= 300 && (int)res.StatusCode < 400) &&
                location is not null)
            {
                if (redirects.Count >= MAX_REDIRECTS)
                    throw new JsonLdParseException("URL could not be dereferenced; there were too many redirects.");
                if (redirects.Contains(url))
                    throw new JsonLdParseException("URL could not be dereferenced; infinite redirection was detected.");

                redirects.Add(url);
                // location can be relative, turn into full url
                var nextUrl = new Uri(new Uri(url), location).AbsoluteUri;
                return await LoadDocument(nextUrl, redirects);
            }

            redirects.Add(url);
            return (await res.Content.ReadAsStringAsync(), url);
        }

        protected static readonly Regex LinkHeadersRegex = new(@"(?:<[^>] *?>| ""[^""]*?""|[^,])+");
        protected static readonly Regex LinkHeaderRegex = new(@"\s*<([^>]*?)>\s* (?:;\s* (.*))?");
        protected static readonly Regex LinkHeaderParamsRegex = new(@"(.*?)=(?:(?:""([^""]*?)"")| ([^""]*?))\s*(?:(?:;\s*)|$)");

        protected JObject ParseLinkHeader(string header)
        {
            JObject rval = new();
            // split on unbracketed/unquoted commas
            var entries = LinkHeadersRegex.Matches(header);
            for (int i = 0; i < entries.Count; ++i)
            {
                var match = LinkHeaderRegex.Match(entries[i].Value);
                if (!match.Success)
                    continue;

                var linkHeader = new JObject();
                linkHeader["target"] = match.Groups[1].Value;
                var headerParams = match.Groups[2].Value;
                while ((match = LinkHeaderParamsRegex.Match(headerParams)).Success)
                    linkHeader[match.Groups[1].Value] = (match.Groups[2].Success ? match.Groups[2] : match.Groups[3]).Value;

                if (linkHeader.TryGetValue("rel", out var relToken))
                {
                    var rel = relToken.Value<string>();
                    if (rval.TryGetValue(rel, out var rvalRel))
                    {
                        if (rvalRel.Type == JTokenType.Array)
                            ((JArray)rvalRel).Add(linkHeader);
                        else
                            rval[rel] = new JArray(rvalRel, linkHeader);
                    }
                    else
                        rval[rel] = linkHeader;
                }
            }
            return rval;
        }

        protected void ResolveContextUrls(JToken context, string baseUrl)
        {
            if (context?.Type != JTokenType.Object)
                return;

            var ctx = context["@context"];

            if (ctx.Type == JTokenType.String)
            {
                context["@context"] = Utils.PrependBase(baseUrl, ctx.Value<string>());
                return;
            }

            if (ctx.Type == JTokenType.Array)
            {
                var ctxArray = (JArray)ctx;
                for (int i = 0; i < ctxArray.Count; ++i)
                {
                    var element = ctxArray[i];
                    if (element.Type == JTokenType.String)
                    {
                        ctxArray[i] = Utils.PrependBase(baseUrl, element.Value<string>());
                        continue;
                    }
                    if (element.Type == JTokenType.Object)
                    {
                        var newObject = new JObject();
                        newObject["@context"] = element;
                        ResolveContextUrls(newObject, baseUrl);
                    }
                }
                return;
            }

            if (ctx.Type != JTokenType.Object)
            {
                // no @context URLs can be found in non-object
                return;
            }

            // ctx is an object, resolve any context URLs in terms
            foreach (var prop in ((JObject)ctx).Properties())
                ResolveContextUrls(prop, baseUrl);
        }

        protected ResolvedContext CacheResolvedContext(string key, ResolvedContext resolved, string tag = null)
        {
            OperationCache[key] = resolved;
            if (tag is not null)
            {
                if (!SharedCache.TryGetValue(key, out var tagMap))
                {
                    tagMap = new();
                    SharedCache[key] = tagMap;
                }
                tagMap[tag] = resolved;
            }
            return resolved;
        }
    }
}
