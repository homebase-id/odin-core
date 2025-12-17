/**
 * Parts of the source code in this file has been translated/ported from jsonld.js library by Digital Bazaar (BSD 3-Clause license)
*/

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JsonLd.Normalization
{
    internal class Expansion
    {
        public static async Task<JToken> Expand(ExpandContext activeCtx,
                                                JToken element,
                                                string activeProperty = null,
                                                ExpandOptions options = null,
                                                bool insideList = false,
                                                bool insideIndex = false,
                                                ExpandContext typeScopedContext = null,
                                                Func<object, JToken> expansionMap = null)
        {
            if (Utils.IsEmptyObject(element))
                return null;

            if (element.Type != JTokenType.Array && element.Type != JTokenType.Object)
            {
                // drop free-floating scalars that are not in lists unless custom mapped
                if (!insideList && (activeProperty == null ||
                                    ExpandIri(activeCtx, activeProperty, IriRelativeTo.VocabSet, null, null, options) == "@graph"))
                {
                    return expansionMap?.Invoke(new
                    {
                        unmappedValue = element,
                        activeCtx,
                        activeProperty,
                        options,
                        insideList
                    });
                }

                // expand element according to value expansion rules
                return ExpandValue(activeCtx, element, activeProperty, options);
            }

            // recursively expand array
            if (element.Type == JTokenType.Array)
            {
                var rvalArr = new JArray();
                var containerToken = GetContextValue(activeCtx, activeProperty, "@container");
                var container = containerToken?.Type == JTokenType.Array ? containerToken.ToArray() : new JToken[0];
                insideList = insideList || container.Any(c => c.Type == JTokenType.String && c.Value<string>() == "@list");
                var elementArray = element.ToArray();
                for (int i = 0; i < elementArray.Length; ++i)
                {
                    // expand element
                    var e = await Expand(activeCtx, elementArray[i], activeProperty, options,
                                           false, insideIndex, typeScopedContext, expansionMap);
                    if (insideList && e?.Type == JTokenType.Array)
                    {
                        var eObj = new JObject();
                        eObj["@list"] = e;
                        e = eObj;
                    }

                    if (e == null)
                    {
                        e = expansionMap?.Invoke(new
                        {
                            unmappedValue = elementArray[i],
                            activeCtx,
                            activeProperty,
                            parent = element,
                            index = i,
                            options,
                            expandedParent = rvalArr,
                            insideList
                        });
                        if (e == null)
                            continue;
                    }

                    if (e.Type == JTokenType.Array)
                    {
                        foreach (var eElem in e.ToArray())
                            rvalArr.Add(eElem);
                    }
                    else
                    {
                        rvalArr.Add(e);
                    }
                }
                return rvalArr;
            }

            // recursively expand object:
            var elemObj = (JObject)element;

            // first, expand the active property
            var expandedActiveProperty = ExpandIri(activeCtx, activeProperty, IriRelativeTo.VocabSet, null, null, options);

            // Get any property-scoped context for activeProperty
            var propertyScopedCtx = GetContextValue(activeCtx, activeProperty, "@context");

            // second, determine if any type-scoped context should be reverted; it
            // should only be reverted when the following are all true:
            // 1. `element` is not a value or subject reference
            // 2. `insideIndex` is false
            typeScopedContext ??= activeCtx.PreviousContext is not null ? activeCtx : null;
            var properties = elemObj.Properties().OrderBy(p => p.Name).ToList();
            var mustRevert = !insideIndex;
            if (mustRevert && typeScopedContext != null && properties.Count <= 2 && !properties.Any(p => p.Name == "@context"))
            {
                foreach (var prop in properties)
                {
                    var expandedProperty = ExpandIri(typeScopedContext, prop.Name, IriRelativeTo.VocabSet, null, null, options);
                    if (expandedProperty == "@value")
                    {
                        // value found, ensure type-scoped context is used to expand it
                        mustRevert = false;
                        activeCtx = typeScopedContext;
                        break;
                    }
                    if (expandedProperty == "@id" && properties.Count == 1)
                    {
                        // subject reference found, do not revert
                        mustRevert = false;
                        break;
                    }
                }
            }

            if (mustRevert)
                activeCtx = activeCtx.RevertToPreviousContext();// revert type scoped context

            // apply property-scoped context after reverting term-scoped context
            if (propertyScopedCtx is not null)
                activeCtx = await ProcessContext(activeCtx, propertyScopedCtx, true, true, options);

            // if element has a context, process it
            if (elemObj.TryGetValue("@context", out var elemContext))
                activeCtx = await ProcessContext(activeCtx, elemContext, true, false, options);

            // set the type-scoped context to the context on input, for use later
            typeScopedContext = activeCtx;

            // Remember the first key found expanding to @type
            string typeKey = null;

            // look for scoped contexts on `@type`
            foreach (var prop in properties)
            {
                var expandedProperty = ExpandIri(activeCtx, prop.Name, IriRelativeTo.VocabSet, null, null, options);
                if (expandedProperty == "@type")
                {
                    // set scoped contexts from @type
                    // avoid sorting if possible
                    typeKey = typeKey ?? prop.Name;
                    var value = elemObj[prop.Name];
                    JArray types;
                    if (value.Type == JTokenType.Array)
                    {
                        var valueArr = value.ToArray();
                        if (valueArr.Length > 1)
                            types = new JArray(valueArr.OrderBy(e => e.ToString()).ToArray());
                        else
                            types = (JArray)value;
                    }
                    else
                        types = new JArray(value);
                    foreach (var type in types)
                    {
                        var ctx = GetContextValue(typeScopedContext, type.ToString(), "@context");
                        if (ctx is not null)
                            activeCtx = await ProcessContext(activeCtx, ctx, false, false, options);
                    }
                }
            }

            // process each key and value in element, ignoring @nest content
            var rval = new JObject();
            await ExpandObject(activeCtx, activeProperty, expandedActiveProperty, elemObj, rval,
                               options, insideList, typeKey, typeScopedContext, expansionMap);

            // get property count on expanded output
            var count = rval.Properties().Count();

            if (rval.TryGetValue("@value", out var valueProp))
            {
                // @value must only have @language or @type
                if (rval.ContainsKey("@type") && (rval.ContainsKey("@language") || rval.ContainsKey("@direction")))
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; an element containing \"@value\" may not " +
                                                   "contain both \"@type\" and either \"@language\" or \"@direction\".");
                }
                var validCount = count - 1;
                if (rval.ContainsKey("@type"))
                    validCount -= 1;
                if (rval.ContainsKey("@index"))
                    validCount -= 1;
                if (rval.ContainsKey("@language"))
                    validCount -= 1;
                if (rval.ContainsKey("@direction"))
                    validCount -= 1;
                if (validCount != 0)
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; an element containing \"@value\" may only " +
                                                   "have an \"@index\" property and either \"@type\" " +
                                                   "or either or both \"@language\" or \"@direction\".");
                }
                var values = new List<JToken>();
                if (valueProp?.Type == JTokenType.Array)
                    values.AddRange(valueProp.ToArray());
                else if (!Utils.IsEmptyObject(valueProp))
                    values.Add(valueProp);

                rval.TryGetValue("@type", out var typeProp);
                var types = new List<JToken>();
                if (typeProp?.Type == JTokenType.Array)
                    types.AddRange(typeProp.ToArray());
                else if (!Utils.IsEmptyObject(typeProp))
                    types.Add(typeProp);

                // drop null @values unless custom mapped
                if (types.Contains("@json") && types.Count == 1) //_processingMode(activeCtx, 1.1) && 
                {
                    // Any value of @value is okay if @type: @json
                }
                else if (values.Count == 0)
                {
                    var mapped = expansionMap?.Invoke(new
                    {
                        unmappedValue = rval,
                        activeCtx,
                        activeProperty,
                        element,
                        options,
                        insideList
                    });
                    rval = mapped as JObject;
                }
                else if (!values.All(v => v.Type == JTokenType.String || Utils.IsEmptyObject(v)) &&
                         rval.ContainsKey("@language"))
                {
                    // if @language is present, @value must be a string
                    throw new JsonLdParseException("Invalid JSON-LD syntax; only strings may be language-tagged.");
                }
                else if (!types.All(t => Utils.IsEmptyObject(t) ||
                                           (t.Type == JTokenType.String && (Utils.IsIriAbsolute(t.Value<string>()) ||
                                                                            t.Value<string>().IndexOf("_:") != 0))))
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; an element containing \"@value\" and \"@type\" " +
                                                   "must have an absolute IRI for the value of \"@type\".");
                }
            }
            else if (rval.TryGetValue("@type", out var typeProp) && typeProp.Type != JTokenType.Array)
            {
                // convert @type to an array
                rval["@type"] = new JArray(typeProp);
            }
            else if (rval.ContainsKey("@set") || rval.ContainsKey("@list"))
            {
                // handle @set and @list
                if (count > 1 && !(count == 2 && rval.ContainsKey("@index")))
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; if an element has the property \"@set\" or " +
                                                   "\"@list\", then it can have at most one other property that is \"@index\".");
                }
                // optimize away @set
                if (rval.TryGetValue("@set", out var setProp))
                {
                    if (setProp.Type != JTokenType.Object)
                        return setProp;

                    rval = (JObject)setProp;
                    count = rval.Properties().Count();
                }
            }
            else if (count == 1 && rval.ContainsKey("@language"))
            {
                // drop objects with only @language unless custom mapped
                var mapped = expansionMap?.Invoke(//rval,
                    new
                    {
                        unmappedValue = rval,
                        activeCtx,
                        activeProperty,
                        element,
                        options,
                        insideList
                    });
                rval = mapped as JObject;
            }

            // drop certain top-level objects that do not occur in lists, unless custom
            // mapped
            if (rval is not null && !(options?.KeepFreeFloatingNodes ?? false) && !insideList &&
                (activeProperty == null || expandedActiveProperty == "@graph"))
            {
                // drop empty object, top-level @value/@list, or object with only @id
                if (count == 0 || rval.ContainsKey("@value") || rval.ContainsKey("@list") ||
                    (count == 1 && rval.ContainsKey("@id")))
                {
                    var mapped = expansionMap?.Invoke(new
                    {
                        unmappedValue = rval,
                        activeCtx,
                        activeProperty,
                        element,
                        options,
                        insideList
                    });
                    rval = mapped as JObject;
                }
            }

            return rval;
        }

        private static async Task<ExpandContext> ProcessContext(ExpandContext activeCtx, JToken localCtx, bool propagate,
                                                                bool overrideProtected, ExpandOptions options,
                                                                HashSet<object> cycles = null)
        {
            cycles ??= new();

            var ctxs = Utils.NormalizeContext(localCtx);

            // no contexts in array, return current active context w/o changes
            if (!ctxs.Any())
                return activeCtx;

            var baseUrl = options.Base;
            var contextResolver = options.ContextResolver;

            // resolve contexts
            var resolved = await contextResolver.Resolve(activeCtx, localCtx, baseUrl);

            // override propagate if first resolved context has `@propagate`
            if (resolved.FirstOrDefault()?.Document?.Type == JTokenType.Object &&
                ((JObject)resolved[0].Document).TryGetValue("@propagate", out var propagateToken) &&
                propagateToken.Type == JTokenType.Boolean)
            {   // retrieve early, error checking done later
                propagate = propagateToken.Value<bool>();
            }

            // process each context in order, update active context
            // on each iteration to ensure proper caching
            var rval = activeCtx;

            // track the previous context
            // if not propagating, make sure rval has a previous context
            if (!propagate && rval.PreviousContext is null)
            {   // clone `rval` context before updating
                rval = rval.CloneActiveContext();
                rval.PreviousContext = activeCtx;
            }

            foreach (var resolvedContext in resolved)
            {
                var ctx = resolvedContext.Document;

                // update active context to one computed from last iteration
                activeCtx = rval;

                // reset to initial context
                if (ctx == null)
                {
                    // We can't nullify if there are protected terms and we're
                    // not allowing overrides (e.g. processing a property term scoped context)
                    if (!overrideProtected && activeCtx.Protected.Any())
                    {
                        var protectedMode = options.ProtectedMode ?? "error";
                        if (protectedMode == "error")
                            throw new JsonLdParseException("Tried to nullify a context with protected terms outside of a term definition.");
                        else if (protectedMode == "warn")
                        {
                            // get processed context from cache if available
                            var processedInWarnProtectedMode = resolvedContext.GetProcessed(activeCtx);
                            if (processedInWarnProtectedMode is not null)
                            {
                                rval = activeCtx = processedInWarnProtectedMode;
                                continue;
                            }

                            var oldActiveCtx = activeCtx;
                            // copy all protected term definitions to fresh initial context
                            rval = activeCtx = new();
                            foreach (var (term, _protected) in oldActiveCtx.Protected)
                            {
                                if (_protected)
                                {
                                    if (oldActiveCtx.Mappings.TryGetValue(term, out var protectedVal))
                                        activeCtx.Mappings[term] = (JObject)protectedVal.DeepClone();
                                    activeCtx.Protected[term] = _protected;
                                }
                            }

                            // cache processed result
                            resolvedContext.SetProcessed(oldActiveCtx, rval);
                            continue;
                        }
                        throw new JsonLdParseException("Invalid protectedMode.");
                    }
                    rval = activeCtx = new();
                    continue;
                }

                // get processed context from cache if available
                var processed = resolvedContext.GetProcessed(activeCtx);
                if (processed is not null)
                {
                    rval = activeCtx = processed;
                    continue;
                }

                // dereference @context key if present
                if (ctx.Type == JTokenType.Object && ((JObject)ctx).TryGetValue("@context", out var ctxContext))
                    ctx = ctxContext;

                // context must be an object by now, all URLs retrieved before this call
                if (ctx.Type != JTokenType.Object)
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @context must be an object.");

                // TODO: there is likely a `previousContext` cloning optimization that
                // could be applied here (no need to copy it under certain conditions)

                // clone context before updating it
                rval = rval.CloneActiveContext();

                // define context mappings for keys in local context
                var defined = new Dictionary<string, bool>();

                var ctxObj = (JObject)ctx;
                // handle @version
                if (ctxObj.TryGetValue("@version", out var versionProp))
                {
                    if ((versionProp.ToString() != "1.1") && (versionProp.ToString() != "1,1")) // Regional
                        throw new JsonLdParseException($"Unsupported JSON-LD version: {versionProp}");

                    //if (activeCtx.processingMode && activeCtx.processingMode === 'json-ld-1.0')
                    //{
                    //    throw new JsonLdError(
                    //      '@version: ' + ctx['@version'] + ' not compatible with ' +
                    //      activeCtx.processingMode,
                    //      'jsonld.ProcessingModeConflict',
                    //      {code: 'processing mode conflict', context: ctx});
                    //}
                    //rval.processingMode = 'json-ld-1.1';
                    rval.Fields["@version"] = versionProp;
                    defined["@version"] = true;
                }

                // if not set explicitly, set processingMode to "json-ld-1.1"
                //rval.processingMode = rval.processingMode || activeCtx.processingMode;

                // handle @base
                if (ctxObj.TryGetValue("@base", out var baseProp))
                {
                    if (!Utils.IsEmptyObject(baseProp))
                    {
                        if (baseProp.Type != JTokenType.String)
                        {
                            throw new JsonLdParseException("Invalid JSON-LD syntax; the value of \"@base\" in a " +
                                                           "@context must be an absolute IRI, a relative IRI, or null.");
                        }
                        var baseVal = baseProp.Value<string>();
                        if (!Utils.IsIriAbsolute(baseVal))
                        {
                            var rvalBaseVal = "";
                            if (rval.Fields.TryGetValue("@base", out var rvalBase) && rvalBase.Type == JTokenType.String)
                                rvalBaseVal = rvalBase.Value<string>();
                            baseProp = Utils.PrependBase(rvalBaseVal, baseVal);
                        }
                    }

                    rval.Fields["@base"] = baseProp;
                    defined["@base"] = true;
                }

                // handle @vocab
                if (ctxObj.TryGetValue("@vocab", out var vocabProp))
                {
                    if (Utils.IsEmptyObject(vocabProp))
                        rval.Fields.Remove("@vocab");
                    else if (vocabProp.Type != JTokenType.String)
                    {
                        throw new JsonLdParseException("Invalid JSON-LD syntax; the value of \"@vocab\" in a " +
                                                       "@context must be a string or null.");
                    }
                    else
                    {
                        var vocabVal = vocabProp.Value<string>();
                        //if (!Utils.IsIriAbsolute(vocabVal) && api.processingMode(rval, 1.0))
                        //{
                        //    throw new JsonLdParseException("Invalid JSON-LD syntax; the value of \"@vocab\" in a " +
                        //                                   "@context must be an absolute IRI.");
                        //}
                        rval.Fields["@vocab"] = ExpandIri(rval, vocabVal, IriRelativeTo.BothSet, null, null, options);
                    }
                    defined["@vocab"] = true;
                }

                // handle @language
                if (ctxObj.TryGetValue("@language", out var languageProp))
                {
                    if (Utils.IsEmptyObject(languageProp))
                        rval.Fields.Remove("@language");
                    else if (languageProp.Type != JTokenType.String)
                    {
                        throw new JsonLdParseException("Invalid JSON-LD syntax; the value of \"@language\" in a " +
                                                       "@context must be a string or null.");
                    }
                    else
                        rval.Fields["@language"] = languageProp.Value<string>().ToLower();
                    defined["@language"] = true;
                }

                // handle @direction
                if (ctxObj.TryGetValue("@direction", out var directionProp))
                {
                    //if (activeCtx.processingMode === 'json-ld-1.0')
                    //{
                    //    throw new JsonLdError(
                    //      'Invalid JSON-LD syntax; @direction not compatible with ' +
                    //      activeCtx.processingMode,
                    //      'jsonld.SyntaxError',
                    //      {code: 'invalid context member', context: ctx});
                    //}
                    if (Utils.IsEmptyObject(directionProp))
                        rval.Fields.Remove("@direction");
                    else
                    {
                        var dirVal = directionProp.ToString();
                        if (dirVal != "ltr" && dirVal != "rtl")
                        {
                            throw new JsonLdParseException("Invalid JSON-LD syntax; the value of \"@direction\" in a " +
                                                           "@context must be null, \"ltr\", or \"rtl\".");
                        }
                        else
                        {
                            rval.Fields["@direction"] = dirVal;
                        }
                    }
                    defined["@direction"] = true;
                }

                // handle @propagate
                // note: we've already extracted it, here we just do error checking
                if (ctxObj.TryGetValue("@propagate", out var propagateProp))
                {
                    //if (activeCtx.processingMode === 'json-ld-1.0')
                    //{
                    //    throw new JsonLdError(
                    //      'Invalid JSON-LD syntax; @propagate not compatible with ' +
                    //      activeCtx.processingMode,
                    //      'jsonld.SyntaxError',
                    //      {code: 'invalid context entry', context: ctx});
                    //}
                    if (propagateProp.Type != JTokenType.Boolean)
                        throw new JsonLdParseException("Invalid JSON-LD syntax; @propagate value must be a boolean.");
                    defined["@propagate"] = true;
                }

                // handle @import
                if (ctxObj.TryGetValue("@import", out var importProp))
                {
                    //if (activeCtx.processingMode === 'json-ld-1.0')
                    //{
                    //    throw new JsonLdError(
                    //      'Invalid JSON-LD syntax; @import not compatible with ' +
                    //      activeCtx.processingMode,
                    //      'jsonld.SyntaxError',
                    //      {code: 'invalid context entry', context: ctx});
                    //}
                    if (importProp.Type != JTokenType.String)
                        throw new JsonLdParseException("Invalid JSON-LD syntax; @import must be a string.");

                    // resolve contexts
                    var resolvedImport = await contextResolver.Resolve(activeCtx, importProp.Value<string>(), baseUrl);
                    if (resolvedImport.Count != 1)
                        throw new JsonLdParseException("Invalid JSON-LD syntax; @import must reference a single context.");

                    var processedImport = resolvedImport[0].GetProcessed(activeCtx);
                    if (processedImport is not null)
                    {
                        // Note: if the same context were used in this active context
                        // as a reference context, then processed_input might not
                        // be a dict.
                        var newCtx = new JObject(); //?
                        foreach (var field in processedImport.Fields)
                            newCtx[field.Key] = field.Value;
                        ctxObj = newCtx;
                    }
                    else
                    {
                        var importCtx = resolvedImport[0].Document;
                        if (importCtx.Type == JTokenType.Object)
                        {
                            var importCtxObj = (JObject)importCtx;
                            if (importCtxObj.ContainsKey("@import"))
                                throw new JsonLdParseException("Invalid JSON-LD syntax: imported context must not include @import.");

                            processedImport = activeCtx.CloneActiveContext(); //?
                            // merge ctx into importCtx and replace rval with the result
                            foreach (var prop in importCtxObj.Properties())
                            {
                                if (!ctxObj.ContainsKey(prop.Name))
                                {
                                    ctxObj[prop.Name] = prop.Value;
                                    processedImport.Fields[prop.Name] = prop.Value;
                                }
                            }
                        }

                        // Note: this could potenially conflict if the import
                        // were used in the same active context as a referenced
                        // context and an import. In this case, we
                        // could override the cached result, but seems unlikely.
                        resolvedImport[0].SetProcessed(activeCtx, processedImport);
                    }

                    defined["@import"] = true;
                }

                // handle @protected; determine whether this sub-context is declaring
                // all its terms to be "protected" (exceptions can be made on a
                // per-definition basis)
                defined["@protected"] = ctxObj.TryGetValue("@protected", out var protectedProp) &&
                                        protectedProp.Type == JTokenType.Boolean && protectedProp.Value<bool>();

                // process all other keys
                foreach (var prop in ctxObj.Properties())
                {
                    CreateTermDefinition(rval, ctxObj, prop.Name, defined, options, overrideProtected);

                    if (prop.Value.Type == JTokenType.Object && ((JObject)prop.Value).TryGetValue("@context", out var propCtx))
                    {
                        var process = true;
                        if (propCtx.Type == JTokenType.String)
                        {
                            var url = Utils.PrependBase(baseUrl, propCtx.Value<string>());
                            // track processed contexts to avoid scoped context recursion
                            if (cycles.Contains(url))
                                process = false;
                            else
                                cycles.Add(url);
                        }
                        // parse context to validate
                        if (process)
                        {
                            try
                            {
                                await ProcessContext(rval.CloneActiveContext(), propCtx, true, true, options, cycles);
                            }
                            catch (Exception e)
                            {
                                throw new JsonLdParseException("Invalid JSON-LD syntax; invalid scoped context.", e);
                            }
                        }
                    }
                }

                // cache processed result
                resolvedContext.SetProcessed(activeCtx, rval);
            }

            return rval;
        }

        private static void CreateTermDefinition(ExpandContext activeCtx, JObject localCtx, string term,
                                                 Dictionary<string, bool> defined, ExpandOptions options,
                                                 bool overrideProtected = false)
        {
            if (String.IsNullOrEmpty(term))
                throw new JsonLdParseException("Invalid JSON-LD syntax; a term cannot be an empty string.");

            if (defined.TryGetValue(term, out var isDefined))
            {
                if (isDefined)
                    return; // term already defined

                // cycle detected
                throw new JsonLdParseException("Cyclical context definition detected.");
            }

            // now defining term
            defined[term] = false;

            // get context term value
            localCtx.TryGetValue(term, out var value);

            if (term == "@type" && value?.Type == JTokenType.Object &&
                (!((JObject)value).TryGetValue("@container", out var valueContainer) || valueContainer.ToString() == "@set"))// && api.processingMode(activeCtx, 1.1))
            {
                var validKeywords = new string[] { "@container", "@id", "@protected" };
                var props = ((JObject)value).Properties();
                if (!props.Any() || props.Any(p => !validKeywords.Contains(p.Name)))
                    throw new JsonLdParseException("Invalid JSON-LD syntax; keywords cannot be overridden.");
            }
            else if (Utils.IsKeyword(term))
            {
                throw new JsonLdParseException("Invalid JSON-LD syntax; keywords cannot be overridden.");
            }
            else if (Utils.IsIriKeyword(term))
            {
                // FIXME: remove logging and use a handler                
                return; //terms beginning with "@" are reserved for future use and ignored
            }

            // remove old mapping
            if (activeCtx.Mappings.TryGetValue(term, out var previousMapping))// keep reference to previous mapping for potential `@protected` check
                activeCtx.Mappings.Remove(term);

            // convert short-hand value to object w/@id
            var simpleTerm = false;
            if (value == null || Utils.IsEmptyObject(value) || value.Type == JTokenType.String)
            {
                simpleTerm = true;
                value = new JObject(new JProperty("@id", value));
            }

            if (value.Type != JTokenType.Object)
                throw new JsonLdParseException("Invalid JSON-LD syntax; @context term values must be strings or objects.");

            var valueObj = (JObject)value;

            // create new mapping
            var mapping = new JObject();
            activeCtx.Mappings[term] = mapping;
            mapping["reverse"] = false;

            // make sure term definition only has expected keywords
            var validKeys = new List<string> { "@container", "@id", "@language", "@reverse", "@type" };

            //// JSON-LD 1.1 support
            //if (api.processingMode(activeCtx, 1.1))
            //{
            validKeys.AddRange(new string[] { "@context", "@direction", "@index", "@nest", "@prefix", "@protected" });
            //}

            foreach (var prop in valueObj.Properties())
            {
                if (!validKeys.Contains(prop.Name))
                    throw new JsonLdParseException("Invalid JSON-LD syntax; a term definition must not contain " + prop.Name);
            }

            // always compute whether term has a colon as an optimization for
            // _compactIri
            var colon = term.IndexOf(':');
            mapping["_termHasColon"] = colon > 0;

            if (valueObj.TryGetValue("@reverse", out var reverseProp))
            {
                if (valueObj.ContainsKey("@id"))
                    throw new JsonLdParseException("Invalid JSON-LD syntax; a @reverse term definition must not contain @id.");
                if (valueObj.ContainsKey("@nest"))
                    throw new JsonLdParseException("Invalid JSON-LD syntax; a @reverse term definition must not contain @nest.");
                if (reverseProp.Type != JTokenType.String)
                    throw new JsonLdParseException("Invalid JSON-LD syntax; a @context @reverse value must be a string.");

                var reverse = reverseProp.Value<string>();
                if (!Utils.IsKeyword(reverse) && Utils.IsIriKeyword(reverse))
                {
                    // FIXME: remove logging and use a handler
                    //values beginning with "@" are reserved for future use and ignored
                    if (previousMapping != null)
                        activeCtx.Mappings[term] = previousMapping;
                    else
                        activeCtx.Mappings.Remove(term);
                    return;
                }

                // expand and add @id mapping
                var id = ExpandIri(activeCtx, reverse, IriRelativeTo.VocabSet, localCtx, defined, options);
                if (!Utils.IsIriAbsolute(id))
                    throw new JsonLdParseException("Invalid JSON-LD syntax; a @context @reverse value must be an absolute IRI or a blank node identifier.");

                mapping["@id"] = id;
                mapping["reverse"] = true;
            }
            else if (valueObj.TryGetValue("@id", out var idProp))
            {
                if (Utils.IsEmptyObject(idProp))
                {
                    // reserve a null term, which may be protected
                    mapping["@id"] = null;
                }
                else if (idProp.Type != JTokenType.String)
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; a @context @id value must be an array of strings or a string.");
                }
                else
                {
                    var id = idProp.Value<string>();
                    if (!Utils.IsKeyword(id) && Utils.IsIriKeyword(id))
                    {
                        // FIXME: remove logging and use a handler
                        //values beginning with "@" are reserved for future use and ignored
                        if (previousMapping != null)
                            activeCtx.Mappings[term] = previousMapping;
                        else
                            activeCtx.Mappings.Remove(term);
                        return;
                    }
                    else if (id != term)
                    {
                        // expand and add @id mapping
                        id = ExpandIri(activeCtx, id, IriRelativeTo.VocabSet, localCtx, defined, options);
                        if (!Utils.IsIriAbsolute(id) && !Utils.IsKeyword(id))
                        {
                            throw new JsonLdParseException("Invalid JSON-LD syntax; a @context @id value must be an absolute IRI, " +
                                                           "a blank node identifier, or a keyword.");
                        }

                        // if term has the form of an IRI it must map the same
                        if (Regex.IsMatch(term, @"(?::[^:])|\/"))
                        {
                            var termDefined = defined.ToDictionary(kvp => kvp.Key, kvp => kvp.Value); //clone
                            termDefined[term] = true;
                            var termIri = ExpandIri(activeCtx, term, IriRelativeTo.VocabSet, localCtx, termDefined, options);
                            if (termIri != id)
                                throw new JsonLdParseException("Invalid JSON-LD syntax; term in form of IRI must expand to definition.");
                        }

                        mapping["@id"] = id;
                        // indicate if this term may be used as a compact IRI prefix
                        mapping["_prefix"] = simpleTerm &&
                            !(mapping.TryGetValue("_termHasColon", out var _termHasColon) &&
                              _termHasColon.Type == JTokenType.Boolean && _termHasColon.Value<bool>()) &&
                            Regex.IsMatch(id, @"[:\/\?#\[\]@]$");
                    }
                }
            }

            if (!mapping.ContainsKey("@id"))
            {
                // see if the term has a prefix
                if (mapping.TryGetValue("_termHasColon", out var mappingHasColon) && mappingHasColon.Value<bool>())
                {
                    var prefix = term.Substring(0, colon);
                    if (localCtx.ContainsKey(prefix))
                    {
                        // define parent prefix
                        CreateTermDefinition(activeCtx, localCtx, prefix, defined, options);
                    }

                    if (activeCtx.Mappings.TryGetValue(prefix, out var prefixVal) && prefixVal.Type == JTokenType.Object)
                    {
                        // set @id based on prefix parent
                        var suffix = term.Substring(colon + 1);
                        var prefixId = "";
                        if (prefixVal.TryGetValue("@id", out var prefixIdToken) && prefixIdToken.Type == JTokenType.String)
                            prefixId = prefixIdToken.Value<string>();
                        mapping["@id"] = prefixId + suffix;
                    }
                    else
                    {
                        // term is an absolute IRI
                        mapping["@id"] = term;
                    }
                }
                else if (term == "@type")
                {
                    // Special case, were we've previously determined that container is @set
                    mapping["@id"] = term;
                }
                else
                {
                    // non-IRIs *must* define @ids if @vocab is not available
                    if (!activeCtx.Fields.TryGetValue("@vocab", out var vocabToken) || vocabToken.Type != JTokenType.String)
                        throw new JsonLdParseException("Invalid JSON-LD syntax; @context terms must define an @id.");
                    // prepend vocab to term
                    mapping["@id"] = vocabToken.Value<string>() + term;
                }
            }
            mapping.TryGetValue("@id", out var mappingIdProp); //for future use

            // Handle term protection
            var valueHasProtected = valueObj.TryGetValue("@protected", out var valueProtectedProp);
            if ((valueHasProtected && valueProtectedProp.Type == JTokenType.Boolean && valueProtectedProp.Value<bool>() == true) ||
                (!valueHasProtected && defined.TryGetValue("@protected", out var definedProtected) && definedProtected))
            {
                activeCtx.Protected[term] = true;
                mapping["protected"] = true;
            }

            // IRI mapping now defined
            defined[term] = true;

            if (valueObj.TryGetValue("@type", out var typeProp))
            {
                if (typeProp.Type != JTokenType.String)
                    throw new JsonLdParseException("Invalid JSON-LD syntax; an @context @type value must be a string.");

                var type = typeProp.Value<string>();
                if (type == "@json" || type == "@none")
                {
                    //if (api.processingMode(activeCtx, 1.0))
                    //{
                    //    throw new JsonLdError(
                    //      'Invalid JSON-LD syntax; an @context @type value must not be ' +
                    //      `"${ type}" in JSON - LD 1.0 mode.`,
                    //      'jsonld.SyntaxError',
                    //      {code: 'invalid type mapping', context: localCtx});
                    //}
                }
                else if (type != "@id" && type != "@vocab")
                {
                    // expand @type to full IRI
                    type = ExpandIri(activeCtx, type, IriRelativeTo.VocabSet, localCtx, defined, options);
                    if (!Utils.IsIriAbsolute(type))
                        throw new JsonLdParseException("Invalid JSON-LD syntax; an @context @type value must be an absolute IRI.");

                    if (type.IndexOf("_:") == 0)
                    {
                        throw new JsonLdParseException("Invalid JSON-LD syntax; an @context @type value must be an IRI, " +
                                                       "not a blank node identifier.");
                    }
                }

                // add @type to mapping
                mapping["@type"] = type;
            }

            if (valueObj.TryGetValue("@container", out var containerProp))
            {
                // normalize container to an array form
                var container = new List<string>();
                if (containerProp.Type == JTokenType.String)
                    container.Add(containerProp.Value<string>());
                else if (containerProp.Type == JTokenType.Array)
                {
                    container.AddRange(containerProp.ToArray().Where(c => c.Type == JTokenType.String)
                                                              .Select(c => c.Value<string>()));
                }

                var validContainers = new List<string> { "@list", "@set", "@index", "@language" };
                var isValid = true;
                var hasSet = container.Contains("@set");

                //// JSON-LD 1.1 support
                //if (api.processingMode(activeCtx, 1.1))
                //{
                validContainers.AddRange(new string[] { "@graph", "@id", "@type" });

                // check container length
                if (container.Contains("@list"))
                {
                    if (container.Count != 1)
                    {
                        throw new JsonLdParseException("Invalid JSON-LD syntax; @context @container with @list must " +
                                                       "have no other values");
                    }
                }
                else if (container.Contains("@graph"))
                {
                    if (container.Any(key => key != "@graph" && key != "@id" && key != "@index" && key != "@set"))
                    {
                        throw new JsonLdParseException("Invalid JSON-LD syntax; @context @container with @graph must " +
                                                       "have no other values other than @id, @index, and @set");
                    }
                }
                else
                {
                    // otherwise, container may also include @set
                    isValid &= container.Count <= (hasSet ? 2 : 1);
                }

                if (container.Contains("@type"))
                {
                    // If mapping does not have an @type,
                    // set it to @id
                    if (!mapping.TryGetValue("@type", out var mappingType) || String.IsNullOrEmpty(mappingType?.ToString()))
                        mapping["@type"] = "@id";

                    // type mapping must be either @id or @vocab
                    if (!(new string[] { "@id", "@vocab" }).Contains(mapping["@type"].ToString()))
                        throw new JsonLdParseException("Invalid JSON-LD syntax; container: @type requires @type to be @id or @vocab.");
                }
                //}
                //else
                //{
                //    // in JSON-LD 1.0, container must not be an array (it must be a string,
                //    // which is one of the validContainers)
                //    isValid &= !_isArray(value['@container']);

                //    // check container length
                //    isValid &= container.length <= 1;
                //}

                // check against valid containers
                isValid &= container.All(c => validContainers.Contains(c));

                // @set not allowed with @list
                isValid &= !(hasSet && container.Contains("@list"));

                if (!isValid)
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @context @container value must be " +
                                                   "one of the following: " + String.Join(", ", validContainers));
                }

                if (mapping.TryGetValue("reverse", out var mappingReverseProp) &&
                    mappingReverseProp.Type == JTokenType.Boolean && mappingReverseProp.Value<bool>() &&
                    !container.All(c => (new string[] { "@index", "@set" }).Contains(c)))
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @context @container value for a @reverse " +
                                                   "type definition must be @index or @set.");
                }

                // add @container to mapping
                mapping["@container"] = new JArray(container.ToArray());
            }

            // property indexing
            if (valueObj.TryGetValue("@index", out var indexProp))
            {
                if (indexProp.Type != JTokenType.String || indexProp.Value<string>().IndexOf('@') == 0)
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @index must expand to an IRI: " +
                                                   $"\"{indexProp.Value<string>()}\" on term \"{term}\".");
                }
                if (!valueObj.ContainsKey("@container") ||
                    !(mapping.TryGetValue("@container", out var mappingContainerProp) &&
                      mappingContainerProp.Type == JTokenType.Array && mappingContainerProp.ToArray().Any(t => JToken.EqualityComparer.Equals(t, "@index"))))
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @index without @index in @container: " +
                                                   $"\"{indexProp.Value<string>()}\" on term \"{term}\".");
                }
                mapping["@index"] = indexProp.Value<string>();
            }

            // scoped contexts
            if (valueObj.TryGetValue("@context", out var contextProp))
                mapping["@context"] = contextProp;

            if (valueObj.TryGetValue("@language", out var languageProp) && !valueObj.ContainsKey("@type"))
            {
                if (!Utils.IsEmptyObject(languageProp) && languageProp.Type != JTokenType.String)
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @context @language value must be a string or null.");

                // add @language to mapping
                if (languageProp.Type == JTokenType.String)
                    languageProp = languageProp.Value<string>().ToLower();
                mapping["@language"] = languageProp;
            }

            // term may be used as a prefix
            if (valueObj.TryGetValue("@prefix", out var prefixProp))
            {
                if (Regex.IsMatch(term, @":|\/"))
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @context @prefix used on a compact IRI term");
                if (mappingIdProp?.Type == JTokenType.String && Utils.IsKeyword(mappingIdProp.Value<string>()))
                    throw new JsonLdParseException("Invalid JSON-LD syntax; keywords may not be used as prefixes");

                if (prefixProp.Type == JTokenType.Boolean)
                    mapping["_prefix"] = prefixProp.Value<bool>();
                else
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @context value for @prefix must be boolean");
            }

            if (valueObj.TryGetValue("@direction", out var directionProp))
            {
                if (!(Utils.IsEmptyObject(directionProp) ||
                      (directionProp.Type == JTokenType.String &&
                       (directionProp.Value<string>() == "ltr" || directionProp.Value<string>() == "rtl"))))
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @direction value must be null, \"ltr\", or \"rtl\".");
                }
                mapping["@direction"] = directionProp;
            }

            if (valueObj.TryGetValue("@nest", out var nestProp))
            {
                if (nestProp.Type != JTokenType.String ||
                    (nestProp.Value<string>() != "@nest" && nestProp.Value<string>().IndexOf('@') == 0))
                {
                    throw new JsonLdParseException("Invalid JSON-LD syntax; @context @nest value must be " +
                                                   "a string which is not a keyword other than @nest.");
                }
                mapping["@nest"] = nestProp;
            }

            // disallow aliasing @context and @preserve
            if (mappingIdProp?.Type == JTokenType.String &&
                (mappingIdProp.Value<string>() == "@context" || mappingIdProp.Value<string>() == "@preserve"))
            {
                throw new JsonLdParseException("Invalid JSON-LD syntax; @context and @preserve cannot be aliased.");
            }

            // Check for overriding protected terms
            if (previousMapping is not null &&
                previousMapping.TryGetValue("protected", out var previousProtected) &&
                previousProtected.Type == JTokenType.Boolean && previousProtected.Value<bool>() &&
                !overrideProtected)
            {
                // force new term to continue to be protected and see if the mappings would
                // be equal
                activeCtx.Protected[term] = true;
                mapping["protected"] = true;
                if (!JToken.DeepEquals(previousMapping, mapping))
                {
                    var protectedMode = options.ProtectedMode ?? "error";
                    if (protectedMode == "error")
                    {
                        throw new JsonLdParseException($"Invalid JSON - LD syntax; tried to redefine \"{term}\" " +
                                                       "which is a protected term.");
                    }
                    else if (protectedMode == "warn")
                    {
                        // FIXME: remove logging and use a handler
                        //console.warn('WARNING: protected term redefinition', { term});
                        return;
                    }
                    throw new JsonLdParseException("Invalid protectedMode.");
                }
            }
        }

        private static async Task ExpandObject(ExpandContext activeCtx, string activeProperty, string expandedActiveProperty,
                                               JObject element, JObject expandedParent, ExpandOptions options,
                                               bool insideList, string typeKey, ExpandContext typeScopedContext,
                                               Func<object, JToken> expansionMap)
        {
            var props = element.Properties().OrderBy(p => p.Name);
            var nests = new List<string>();
            JToken unexpandedValue = null;

            bool isFrame = options.IsFrame;

            var elementTypeKeyProp = typeKey is not null ? element[typeKey] : null;
            var elementTypeKey = elementTypeKeyProp?.Type == JTokenType.Array ? ((JArray)elementTypeKeyProp)[0] : elementTypeKeyProp;
            // Figure out if this is the type for a JSON literal
            var isJsonType = elementTypeKeyProp is not null &&
                             ExpandIri(activeCtx, elementTypeKey.ToString(), IriRelativeTo.VocabSet, options: options)
                                == "@json";

            foreach (var prop in props)
            {
                var value = prop.Value;
                JToken expandedValue = null;

                // skip @context
                if (prop.Name == "@context")
                    continue;

                // expand property
                var expandedProperty = ExpandIri(activeCtx, prop.Name, IriRelativeTo.VocabSet, options: options);

                // drop non-absolute IRI keys that aren't keywords unless custom mapped
                if (expandedProperty == null || !(Utils.IsIriAbsolute(expandedProperty) || Utils.IsKeyword(expandedProperty)))
                {
                    // TODO: use `await` to support async
                    expandedProperty = expansionMap?.Invoke(new
                    {
                        unmappedProperty = prop.Name,
                        activeCtx,
                        activeProperty,
                        parent = element,
                        options,
                        insideList,
                        value,
                        expandedParent
                    })?.ToString();
                    if (expandedProperty == null)
                    {
                        continue;
                    }
                }

                if (Utils.IsKeyword(expandedProperty))
                {
                    if (expandedActiveProperty == "@reverse")
                        throw new JsonLdParseException("Invalid JSON-LD syntax; a keyword cannot be used as a @reverse property.");

                    if (expandedParent.ContainsKey(expandedProperty) &&
                        expandedProperty != "@included" &&
                        expandedProperty != "@type")
                    {
                        throw new JsonLdParseException("Invalid JSON-LD syntax; colliding keywords detected.");
                    }
                }

                // syntax error if @id is not a string
                if (expandedProperty == "@id")
                {
                    if (value.Type != JTokenType.String)
                    {
                        if (!isFrame)
                            throw new JsonLdParseException("Invalid JSON-LD syntax; \"@id\" value must a string.");

                        if (value.Type == JTokenType.Object)
                        {
                            // empty object is a wildcard
                            if (!Utils.IsEmptyObject(value))
                            {
                                throw new JsonLdParseException("Invalid JSON-LD syntax; \"@id\" value an empty object or array " +
                                                               "of strings, if framing");
                            }
                        }
                        else if (value.Type == JTokenType.Array)
                        {
                            if (!value.ToArray().All(v => v.Type == JTokenType.String))
                            {
                                throw new JsonLdParseException("Invalid JSON-LD syntax; \"@id\" value an empty object or array " +
                                                               "of strings, if framing");
                            }
                        }
                        else
                        {
                            throw new JsonLdParseException("Invalid JSON-LD syntax; \"@id\" value an empty object or array " +
                                                           "of strings, if framing");
                        }
                    }

                    var valueArray = Utils.AsArray(value);
                    for (var i = 0; i < valueArray.Count; ++i)
                    {
                        if (valueArray[i].Type == JTokenType.String)
                        {
                            valueArray[i] = ExpandIri(activeCtx, valueArray[i].Value<string>(),
                                                      IriRelativeTo.BaseSet, options: options);
                        }
                    }
                    Utils.AddValue(expandedParent, "@id", valueArray, propertyIsArray: isFrame);
                    continue;
                }

                if (expandedProperty == "@type")
                {
                    // if framing, can be a default object, but need to expand
                    // key to determine that
                    if (value.Type == JTokenType.Object)
                    {
                        var newObj = new JObject();
                        foreach (var valueProp in ((JObject)value).Properties())
                        {
                            var newName = ExpandIri(typeScopedContext, valueProp.Name, IriRelativeTo.VocabSet, options: options);
                            var newVal = new JArray(Utils.AsArray(valueProp.Value)
                                                         .Select(item => ExpandIri(typeScopedContext, item.ToString(),
                                                                                   IriRelativeTo.BothSet, options: options)));
                            newObj[newName] = newVal;
                        }
                        value = newObj;
                    }
                    ValidateTypeValue(value, isFrame);
                    Utils.AddValue(expandedParent, "@type",
                      new JArray(Utils.AsArray(value).Select(v =>
                      {
                          if (v.Type == JTokenType.String)
                              return ExpandIri(typeScopedContext, v.Value<string>(), IriRelativeTo.BothSet, options: options);
                          return v;
                      })),
                      propertyIsArray: isFrame);
                    continue;
                }

                // Included blocks are treated as an array of separate object nodes sharing
                // the same referencing active_property.
                // For 1.0, it is skipped as are other unknown keywords
                if (expandedProperty == "@included")// && _processingMode(activeCtx, 1.1))
                {
                    var includedResult = Utils.AsArray(await Expand(activeCtx, value, activeProperty,
                                                                      options, expansionMap: expansionMap));

                    // Expanded values must be node objects
                    if (!includedResult.All(v => Utils.IsGraphSubject(v)))
                        throw new JsonLdParseException("Invalid JSON-LD syntax; values of @included must expand to node objects.");

                    Utils.AddValue(expandedParent, "@included", includedResult, propertyIsArray: true);
                    continue;
                }

                // @graph must be an array or an object
                if (expandedProperty == "@graph" && !(value.Type == JTokenType.Object || value.Type == JTokenType.Array))
                    throw new JsonLdParseException("Invalid JSON-LD syntax; \"@graph\" value must not be an object or an array.");

                if (expandedProperty == "@value")
                {
                    // capture value for later
                    // "colliding keywords" check prevents this from being set twice
                    unexpandedValue = value;
                    if (isJsonType)// && _processingMode(activeCtx, 1.1))
                    {
                        // no coercion to array, and retain all values
                        expandedParent["@value"] = value;
                    }
                    else
                    {
                        Utils.AddValue(expandedParent, "@value", value, propertyIsArray: isFrame);
                    }
                    continue;
                }

                // @language must be a string
                // it should match BCP47
                if (expandedProperty == "@language")
                {
                    if (value == null)
                    {
                        // drop null @language values, they expand as if they didn't exist
                        continue;
                    }
                    if (value.Type != JTokenType.String && !isFrame)
                        throw new JsonLdParseException("Invalid JSON-LD syntax; \"@language\" value must be a string.");

                    // ensure language value is lowercase
                    value = new JArray(Utils.AsArray(value).Select(v => v.Type == JTokenType.String ?
                                                                            v.Value<string>().ToLower() : v));

                    //var bcp47Regex = new Regex(@"^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$");
                    //// ensure language tag matches BCP47
                    //foreach (var lang in value.ToArray())
                    //{
                    //    if (lang.Type == JTokenType.String && !bcp47Regex.IsMatch(lang.Value<string>()))
                    //    {
                    //        console.warn($"@language must be valid BCP47: {lang}");
                    //    }
                    //}

                    Utils.AddValue(expandedParent, "@language", value, propertyIsArray: isFrame);
                    continue;
                }

                // @direction must be "ltr" or "rtl"
                if (expandedProperty == "@direction")
                {
                    if (value.Type != JTokenType.String && !isFrame)
                    {
                        throw new JsonLdParseException("Invalid JSON-LD syntax; \"@direction\" value must be a string.");
                    }

                    value = Utils.AsArray(value);

                    // ensure direction is "ltr" or "rtl"
                    foreach (var dir in value)
                    {
                        if (dir.Type == JTokenType.String && dir.Value<string>() != "ltr" && dir.Value<string>() != "rtl")
                            throw new JsonLdParseException("Invalid JSON-LD syntax; \"@direction\" must be \"ltr\" or \"rtl\".");
                    }

                    Utils.AddValue(expandedParent, "@direction", value, propertyIsArray: isFrame);
                    continue;
                }

                // @index must be a string
                if (expandedProperty == "@index")
                {
                    if (value.Type != JTokenType.String)
                        throw new JsonLdParseException("Invalid JSON-LD syntax; \"@index\" value must be a string.");

                    Utils.AddValue(expandedParent, "@index", value);
                    continue;
                }

                // @reverse must be an object
                if (expandedProperty == "@reverse")
                {
                    if (value.Type != JTokenType.Object)
                        throw new JsonLdParseException("Invalid JSON-LD syntax; \"@reverse\" value must be an object.");

                    expandedValue = await Expand(activeCtx, value, "@reverse", options, expansionMap: expansionMap);
                    // properties double-reversed
                    if (expandedValue.Type == JTokenType.Object &&
                        ((JObject)expandedValue).TryGetValue("@reverse", out var reverseProp) &&
                        reverseProp.Type == JTokenType.Object)
                    {
                        foreach (var revProp in ((JObject)reverseProp).Properties())
                            Utils.AddValue(expandedParent, revProp.Name, revProp.Value, propertyIsArray: true);
                    }

                    // FIXME: can this be merged with code below to simplify?
                    // merge in all reversed properties
                    var reverseMap = expandedParent["@reverse"] as JObject;
                    if (expandedValue.Type == JTokenType.Object)
                    {
                        foreach (var expProp in ((JObject)expandedValue).Properties())
                        {
                            if (expProp.Name == "@reverse")
                                continue;

                            if (reverseMap == null)
                            {
                                reverseMap = new JObject();
                                expandedParent["@reverse"] = reverseMap;
                            }
                            Utils.AddValue(reverseMap, expProp.Name, new JArray(), propertyIsArray: true);
                            var items = expProp.Value as JArray;
                            for (var ii = 0; ii < (items?.Count ?? 0); ++ii)
                            {
                                var item = items[ii];
                                if (Utils.IsGraphValue(item) || Utils.IsGraphList(item))
                                {
                                    throw new JsonLdParseException("Invalid JSON-LD syntax; \"@reverse\" value must not be a " +
                                                                   "@value or an @list.");
                                }
                                Utils.AddValue(reverseMap, expProp.Name, item, propertyIsArray: true);
                            }
                        }
                    }

                    continue;
                }

                // nested keys
                if (expandedProperty == "@nest")
                {
                    nests.Add(prop.Name);
                    continue;
                }

                // use potential scoped context for key
                var termCtx = activeCtx;
                var ctx = GetContextValue(activeCtx, prop.Name, "@context");
                if (ctx is not null)
                    termCtx = await ProcessContext(activeCtx, ctx, true, true, options);

                var container = (GetContextValue(termCtx, prop.Name, "@container") as JArray ?? new JArray())
                                    .Select(t => t.ToString()).ToList();

                if (container.Contains("@language") && value.Type == JTokenType.Object)
                {
                    var direction = GetContextValue(termCtx, prop.Name, "@direction");
                    // handle language map container (skip if value is not an object)
                    expandedValue = ExpandLanguageMap(termCtx, value, direction, options);
                }
                else if (container.Contains("@index") && value.Type == JTokenType.Object)
                {
                    // handle index container (skip if value is not an object)
                    var asGraph = container.Contains("@graph");
                    var indexKey = GetContextValue(termCtx, prop.Name, "@index")?.ToString() ?? "@index";
                    var propertyIndex = indexKey != "@index" ?
                        ExpandIri(activeCtx, indexKey, IriRelativeTo.VocabSet, options: options) : null;

                    expandedValue = await ExpandIndexMap(termCtx, prop.Name, value, expansionMap,
                                                         asGraph, indexKey, propertyIndex, options);
                }
                else if (container.Contains("@id") && value.Type == JTokenType.Object)
                {
                    // handle id container (skip if value is not an object)
                    var asGraph = container.Contains("@graph");
                    expandedValue = await ExpandIndexMap(termCtx, prop.Name, value, expansionMap, asGraph, "@id", null, options);
                }
                else if (container.Contains("@type") && value.Type == JTokenType.Object)
                {
                    // handle type container (skip if value is not an object)
                    // since container is `@type`, revert type scoped context when expanding
                    expandedValue = await ExpandIndexMap(termCtx.RevertToPreviousContext(), prop.Name, value, expansionMap,
                                                         false, "@type", null, options);
                }
                else
                {
                    // recurse into @list or @set
                    var isList = expandedProperty == "@list";
                    if (isList || expandedProperty == "@set")
                    {
                        var nextActiveProperty = activeProperty;
                        if (isList && expandedActiveProperty == "@graph")
                            nextActiveProperty = null;

                        expandedValue = await Expand(termCtx, value, nextActiveProperty, options, isList,
                                                       expansionMap: expansionMap);
                    }
                    else if (JToken.EqualityComparer.Equals(GetContextValue(activeCtx, prop.Name, "@type"), "@json"))
                    {
                        expandedValue = new JObject();
                        expandedValue["@type"] = "@json";
                        expandedValue["@value"] = value;
                    }
                    else
                    {
                        // recursively expand value with key as new active property
                        expandedValue = await Expand(termCtx, value, prop.Name, options, false, expansionMap: expansionMap);
                    }
                }

                // drop null values if property is not @value
                if (expandedValue == null && expandedProperty != "@value")
                {
                    // TODO: use `await` to support async
                    expandedValue = expansionMap?.Invoke(new
                    {
                        unmappedValue = value,
                        expandedProperty,
                        activeCtx = termCtx,
                        activeProperty,
                        parent = element,
                        options,
                        insideList,
                        key = prop.Name,
                        expandedParent
                    });
                    if (expandedValue == null)
                    {
                        continue;
                    }
                }

                // convert expanded value to @list if container specifies it
                if (expandedProperty != "@list" && !Utils.IsGraphList(expandedValue) && container.Contains("@list"))
                {
                    // ensure expanded value in @list is an array
                    var newObj = new JObject();
                    newObj["@list"] = Utils.AsArray(expandedValue);
                    expandedValue = newObj;
                }

                // convert expanded value to @graph if container specifies it
                // and value is not, itself, a graph
                // index cases handled above
                if (container.Contains("@graph") && !container.Any(key => key == "@id" || key == "@index"))
                {
                    // ensure expanded values are arrays
                    expandedValue = new JArray(Utils.AsArray(expandedValue).Select(v =>
                    {
                        var obj = new JObject();
                        obj["@graph"] = Utils.AsArray(v);
                        return obj;
                    }));
                }

                // FIXME: can this be merged with code above to simplify?
                // merge in reverse properties
                if (termCtx.Mappings.TryGetValue(prop.Name, out var mappedProp) &&
                    mappedProp.TryGetValue("reverse", out var mappedReverseProp) &&
                    mappedReverseProp.Type == JTokenType.Boolean && mappedReverseProp.Value<bool>())
                {
                    if (!expandedParent.TryGetValue("@reverse", out var reverseMap) || reverseMap.Type != JTokenType.Object)
                    {
                        reverseMap = new JObject();
                        expandedParent["@reverse"] = reverseMap;
                    }
                    expandedValue = Utils.AsArray(expandedValue);
                    for (var ii = 0; ii < ((JArray)expandedValue).Count; ++ii)
                    {
                        var item = expandedValue[ii];
                        if (Utils.IsGraphValue(item) || Utils.IsGraphList(item))
                        {
                            throw new JsonLdParseException("Invalid JSON-LD syntax; \"@reverse\" value must not be a " +
                                                           "@value or an @list.");
                        }
                        Utils.AddValue((JObject)reverseMap, expandedProperty, item, propertyIsArray: true);
                    }
                    continue;
                }

                // add value for property
                // special keywords handled above
                Utils.AddValue(expandedParent, expandedProperty, expandedValue, propertyIsArray: true);
            }

            // @value must not be an object or an array (unless framing) or if @type is
            // @json
            if (expandedParent.ContainsKey("@value"))
            {
                if (expandedParent["@type"]?.ToString() == "@json")// && _processingMode(activeCtx, 1.1))
                {
                    // allow any value, to be verified when the object is fully expanded and
                    // the @type is @json.
                }
                else if ((unexpandedValue.Type == JTokenType.Object || unexpandedValue.Type == JTokenType.Array) && !isFrame)
                    throw new JsonLdParseException("Invalid JSON-LD syntax; \"@value\" value must not be an object or an array.");
            }

            // expand each nested key
            foreach (var key in nests)
            {
                var nestedValues = Utils.AsArray(element[key]);
                foreach (var nv in nestedValues)
                {
                    if (nv.Type != JTokenType.Object ||
                        ((JObject)nv).Properties().Any(prop =>
                            ExpandIri(activeCtx, prop.Name, IriRelativeTo.VocabSet, options: options) == "@value"))
                    {
                        throw new JsonLdParseException("Invalid JSON-LD syntax; nested value must be a node object.");
                    }
                    await ExpandObject(activeCtx, activeProperty, expandedActiveProperty, (JObject)nv,
                                       expandedParent, options, insideList, typeKey, typeScopedContext, expansionMap);
                }
            }
        }

        private static void ValidateTypeValue(JToken v, bool isFrame)
        {
            if (v.Type == JTokenType.String)
                return;

            if (v.Type == JTokenType.Array && ((JArray)v).All(vv => vv.Type == JTokenType.String))
                return;

            if (isFrame && v.Type == JTokenType.Object)
            {
                var obj = (JObject)v;
                switch (obj.Properties().Count())
                {
                    case 0:
                        // empty object is wildcard
                        return;
                    case 1:
                        // default entry is all strings
                        if (obj.TryGetValue("@default", out var defaultProp) &&
                            Utils.AsArray(defaultProp).All(vv => vv.Type == JTokenType.String))
                        {
                            return;
                        }
                        break;
                }
            }

            throw new JsonLdParseException("Invalid JSON-LD syntax; \"@type\" value must a string, an array of " +
                                           "strings, an empty object, or a default object.");
        }

        private static JArray ExpandLanguageMap(ExpandContext activeCtx, JToken languageMap, JToken direction,
                                                ExpandOptions options)
        {
            var rval = new JArray();
            if (languageMap.Type == JTokenType.Object)
            {
                foreach (var prop in ((JObject)languageMap).Properties().OrderBy(p => p.Name))
                {
                    var expandedKey = ExpandIri(activeCtx, prop.Name, IriRelativeTo.VocabSet, options: options);
                    var val = Utils.AsArray(prop.Value);
                    foreach (var item in val)
                    {
                        if (item == null)
                        {
                            // null values are allowed (8.5) but ignored (3.1)
                            continue;
                        }
                        if (item.Type != JTokenType.String)
                        {
                            throw new JsonLdParseException("Invalid JSON-LD syntax; language map values must be strings.");
                        }
                        var newObj = new JObject();
                        newObj["@value"] = item;
                        if (expandedKey != "@none")
                        {
                            newObj["@language"] = prop.Name.ToLower();
                        }
                        if (!Utils.IsEmptyObject(direction))
                        {
                            newObj["@direction"] = direction;
                        }
                        rval.Add(newObj);
                    }
                }
            }
            return rval;
        }

        private static async Task<JArray> ExpandIndexMap(ExpandContext activeCtx, string activeProperty, JToken value,
                                                         Func<object, JToken> expansionMap, bool asGraph,
                                                         string indexKey, string propertyIndex, ExpandOptions options)
        {
            var rval = new JArray();
            if (value.Type == JTokenType.Object)
            {
                var isTypeIndex = indexKey == "@type";
                foreach (var prop in ((JObject)value).Properties().OrderBy(p => p.Name))
                {
                    var key = prop.Name;
                    // if indexKey is @type, there may be a context defined for it
                    if (isTypeIndex)
                    {
                        var ctx = GetContextValue(activeCtx, key, "@context");
                        if (ctx is not null)
                            activeCtx = await ProcessContext(activeCtx, ctx, false, false, options);
                    }

                    var val = await Expand(activeCtx, Utils.AsArray(prop.Value), activeProperty, options,
                                             false, true, expansionMap: expansionMap);

                    // expand for @type, but also for @none
                    JToken expandedKey = null;
                    if (propertyIndex is not null)
                    {
                        if (key == "@none")
                        {
                            expandedKey = "@none";
                        }
                        else
                        {
                            expandedKey = ExpandValue(activeCtx, key, indexKey, options);
                        }
                    }
                    else
                    {
                        expandedKey = ExpandIri(activeCtx, key, IriRelativeTo.VocabSet, options: options);
                    }

                    if (indexKey == "@id")
                    {
                        // expand document relative
                        key = ExpandIri(activeCtx, key, IriRelativeTo.BaseSet, options: options);
                    }
                    else if (isTypeIndex)
                    {
                        key = expandedKey.ToString();
                    }

                    var items = new JToken[0];
                    if (val.Type == JTokenType.Object)
                        items = ((JObject)val).Properties().Select(p => p.Value).ToArray();
                    if (val.Type == JTokenType.Array)
                        items = val.ToArray();
                    foreach (var itemElem in items)
                    {
                        var item = itemElem;
                        // If this is also a @graph container, turn items into graphs
                        if (asGraph && !Utils.IsGraph(item))
                        {
                            item = new JObject();
                            item["@graph"] = new JArray(itemElem);
                        }
                        if (indexKey == "@type")
                        {
                            if (expandedKey.Type == JTokenType.String && expandedKey.Value<string>() == "@none")
                            {
                                // ignore @none
                            }
                            else if (item.Type == JTokenType.Object)
                            {
                                var itemObj = (JObject)item;
                                if (itemObj.TryGetValue("@type", out var typeProp))
                                {
                                    var arr = new JArray(key);
                                    arr.Add(typeProp);
                                    item["@type"] = arr;
                                }
                                else
                                {
                                    item["@type"] = new JArray(key);
                                }
                            }
                        }
                        else if (Utils.IsGraphValue(item) && !new string[] { "@language", "@type", "@index" }.Contains(indexKey))
                        {
                            throw new JsonLdParseException("Invalid JSON-LD syntax; Attempt to add illegal key to value " +
                                                           $"object: \"{indexKey}\".");
                        }
                        else if (expandedKey.Type != JTokenType.String || expandedKey.Value<string>() != "@none")
                        {
                            if (!String.IsNullOrEmpty(propertyIndex))
                            {
                                // index is a property to be expanded, and values interpreted for that
                                // property
                                if (item.Type == JTokenType.Object)
                                {
                                    // expand key as a value
                                    Utils.AddValue((JObject)item, propertyIndex, expandedKey,
                                                   propertyIsArray: true, prependValue: true);
                                }
                            }
                            else if (item.Type == JTokenType.Object && !((JObject)item).ContainsKey(indexKey))
                            {
                                item[indexKey] = key;
                            }
                        }
                        rval.Add(item);
                    }
                }
            }
            return rval;
        }

        private static string ExpandIri(ExpandContext activeCtx, string value, IriRelativeTo relativeTo = null,
                                        JObject localCtx = null, Dictionary<string, bool> defined = null,
                                        ExpandOptions options = null)
        {
            // already expanded
            if (value == null || Utils.IsKeyword(value))
                return value;

            // ignore non-keyword things that look like a keyword
            if (Utils.IsIriKeyword(value))
                return null;

            // define term dependency if not defined
            if (localCtx != null && localCtx.ContainsKey(value) &&
                !(defined.TryGetValue(value, out var isDefined) && isDefined))
            {
                CreateTermDefinition(activeCtx, localCtx, value, defined, options);
            }

            if (relativeTo?.Vocab ?? false)
            {
                if (activeCtx.Mappings.TryGetValue(value, out var mapping))
                {
                    if (mapping is null) // value is explicitly ignored with a null mapping
                        return null;

                    if (mapping.TryGetValue("@id", out var idProp))
                        return idProp.ToString();
                }
            }

            // split value into prefix:suffix
            var colonIdx = value.IndexOf(':');
            if (colonIdx > 0)
            {
                var prefix = value.Substring(0, colonIdx);
                var suffix = value.Substring(colonIdx + 1);

                // do not expand blank nodes (prefix of '_') or already-absolute
                // IRIs (suffix of '//')
                if (prefix == "_" || suffix.IndexOf("//") == 0)
                    return value;

                // prefix dependency not defined, define it
                if (localCtx?.ContainsKey(prefix) ?? false)
                    CreateTermDefinition(activeCtx, localCtx, prefix, defined, options);

                // use mapping if prefix is defined
                if (activeCtx.Mappings.TryGetValue(prefix, out var mapping) &&
                    mapping.TryGetValue("_prefix", out var prefixMapping) && prefixMapping.Value<bool>())
                {
                    return (mapping.TryGetValue("@id", out var id) ? (id?.ToString() ?? "") : "") + suffix;
                }

                // already absolute IRI
                if (Utils.IsIriAbsolute(value))
                    return value;
            }

            // prepend vocab
            if (relativeTo.Vocab && activeCtx.Fields.TryGetValue("@vocab", out var vocabVal))
                return (vocabVal.Type == JTokenType.String ? vocabVal.Value<string>() : "") + value;

            // prepend base
            if (relativeTo.Base && activeCtx.Fields.TryGetValue("@base", out var baseValProp) &&
                (Utils.IsEmptyObject(baseValProp) || baseValProp?.Type == JTokenType.String))
            {
                var baseVal = baseValProp?.ToString();
                if (!String.IsNullOrEmpty(baseVal)) // The null case preserves value as potentially relative                    
                    return Utils.PrependBase(Utils.PrependBase(options.Base, baseVal), value);
            }
            else if (relativeTo.Base)
            {
                return Utils.PrependBase(options.Base, value);
            }

            return value;
        }

        private static JToken ExpandValue(ExpandContext activeCtx, JToken value, string activeProperty = null,
                                          ExpandOptions options = null)
        {
            // nothing to expand
            if (Utils.IsEmptyObject(value))
                return null;

            // special-case expand @id and @type (skips '@id' expansion)
            var expandedProperty = ExpandIri(activeCtx, activeProperty, IriRelativeTo.VocabSet, null, null, options);
            if (expandedProperty == "@id" || expandedProperty == "@type")
            {
                if (value.Type == JTokenType.String)
                {
                    var relativeTo = new IriRelativeTo { Base = true, Vocab = expandedProperty == "@type" };
                    return ExpandIri(activeCtx, value.Value<string>(), relativeTo, null, null, options);
                }
                return value;
            }

            // get type definition from context
            var typeToken = GetContextValue(activeCtx, activeProperty, "@type");
            var type = typeToken?.Type == JTokenType.String ? typeToken.Value<string>() : null;

            // do @id expansion (automatic for @graph)
            if ((type == "@id" || expandedProperty == "@graph") && value.Type == JTokenType.String)
            {
                var res = new JObject();
                res["@id"] = ExpandIri(activeCtx, value.Value<string>(), IriRelativeTo.BaseSet, null, null, options);
                return res;
            }
            // do @id expansion w/vocab
            if (type == "@vocab" && value.Type == JTokenType.String)
            {
                var res = new JObject();
                res["@id"] = ExpandIri(activeCtx, value.Value<string>(), IriRelativeTo.BothSet, null, null, options);
                return res;
            }

            // do not expand keyword values
            if (Utils.IsKeyword(expandedProperty))
                return value;

            var rval = new JObject();

            if (!String.IsNullOrEmpty(type) && !new string[] { "@id", "@vocab", "@none" }.Contains(type))
            {
                // other type
                rval["@type"] = type;
            }
            else if (value.Type == JTokenType.String)
            {
                // check for language tagging for strings
                var language = GetContextValue(activeCtx, activeProperty, "@language");
                if (language != null)
                    rval["@language"] = language;

                var direction = GetContextValue(activeCtx, activeProperty, "@direction");
                if (direction != null)
                    rval["@direction"] = direction;
            }

            if (value.Type == JTokenType.Boolean || value.Type == JTokenType.Float ||
                value.Type == JTokenType.Integer || value.Type == JTokenType.String)
            {
                rval["@value"] = value;
            }
            else // do conversion of values that aren't basic JSON types to strings
            {
                var stringVal = value.ToString(Formatting.None);
                if (stringVal.StartsWith('\"') && stringVal.EndsWith('\"'))
                    stringVal = stringVal[1..^1];
                rval["@value"] = stringVal;
            }

            return rval;
        }

        private static JToken GetContextValue(ExpandContext ctx, string key, string type)
        {
            // invalid key
            if (key is null || type is null)
                return null;

            // get specific entry information
            if (ctx.Mappings.TryGetValue(key, out var entry))
            {
                if (entry.TryGetValue(type, out var entryTypeVal))
                    return entryTypeVal;// return entry value for type
            }

            // get default language or direction
            if ((type == "@language" || type == "@direction") && ctx.Fields.TryGetValue(type, out var ctxTypeVal))
                return ctxTypeVal;

            return null;
        }
    }
}
