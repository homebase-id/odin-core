/**
 * Parts of the source code in this file has been translated/ported from jsonld.js library by Digital Bazaar (BSD 3-Clause license)
 * and canonicalize library by Samuel Erdtman (Apache License 2.0)
*/

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JsonLd.Normalization
{
    internal static class RDF
    {
        public static List<Quad> ToRDF(this JToken input)
        {
            var dataset = new List<Quad>();

            var issuer = new IdentifierIssuer("_:b");
            var nodeMap = new JObject();
            nodeMap["@default"] = new JObject();

            CreateNodeMap(input, nodeMap, "@default", issuer);

            foreach (var props in nodeMap.Properties())
            {
                var graphTerm = new QuadItem();
                if (props.Name == "@default")
                {
                    graphTerm.TermType = TermType.DefaultGraph;
                    graphTerm.Value = "";
                }
                else if (Utils.IsIriAbsolute(props.Name))
                {
                    if (props.Name.StartsWith("_:"))
                        graphTerm.TermType = TermType.BlankNode;
                    else
                        graphTerm.TermType = TermType.NamedNode;
                    graphTerm.Value = props.Name;
                }
                else
                {
                    // skip relative IRIs (not valid RDF)
                    continue;
                }
                if (props.Value.Type == JTokenType.Object)
                    GraphToRDF(dataset, (JObject)props.Value, graphTerm, issuer);
            }

            return dataset;
        }

        private static void CreateNodeMap(JToken input, JObject graphs, string graph, IdentifierIssuer issuer,
                                          string name = null, JArray list = null)
        {
            // recurse through array
            if (input.Type == JTokenType.Array)
            {
                foreach (var node in input.ToArray())
                    CreateNodeMap(node, graphs, graph, issuer, null, list);
                return;
            }

            // add non-object to list
            if (input.Type != JTokenType.Object)
            {
                if (list is not null)
                    list.Add(input);
                return;
            }

            var inputObj = (JObject)input;
            inputObj.TryGetValue("@type", out var typeProp);

            // add values to list
            if (inputObj.ContainsKey("@value"))
            {
                if (typeProp is not null)
                {
                    // rename @type blank node
                    if (typeProp.Type == JTokenType.String && typeProp.Value<string>().IndexOf("_:") == 0)
                        inputObj["@type"] = typeProp = issuer.GetId(typeProp.Value<string>());
                }
                if (list is not null)
                    list.Add(input);
                return;
            }
            else if (list is not null && inputObj.TryGetValue("@list", out var listProp))
            {
                var _list = new JArray();
                CreateNodeMap(listProp, graphs, graph, issuer, name, _list);
                var newObj = new JObject();
                newObj["@list"] = _list;
                list.Add(newObj);
                return;
            }

            // Note: At this point, input must be a subject.

            // spec requires @type to be named first, so assign names early
            if (typeProp?.Type == JTokenType.Array)
            {
                foreach (var type in typeProp.ToArray())
                {
                    if (type.Type == JTokenType.String && type.Value<string>().IndexOf("_:") == 0)
                        issuer.GetId(type.Value<string>()); //for side effects
                }
            }

            // get name for subject
            if (name is null)
            {
                string inputId = null;
                if (inputObj.TryGetValue("@id", out var idProp) && idProp.Type == JTokenType.String)
                    inputId = idProp.Value<string>();
                name = Utils.IsBlankNode(input) ? issuer.GetId(inputId) : inputId;
            }

            // add subject reference to list
            if (list is not null)
            {
                var newObj = new JObject();
                newObj["@id"] = name;
                list.Add(newObj);
            }

            // create new subject or merge into existing one
            if (!graphs.TryGetValue(graph, out var subjectsProp) || subjectsProp.Type != JTokenType.Object)
            {
                subjectsProp = new JObject();
                graphs[graph] = subjectsProp;
            }
            var subjects = (JObject)subjectsProp;
            if (!subjects.TryGetValue(name, out var subjectProp) || subjectProp.Type != JTokenType.Object)
            {
                subjectProp = new JObject();
                if (name is not null)
                    subjects[name] = subjectProp;
            }
            var subject = (JObject)subjectProp;
            subject["@id"] = name;
            foreach (var prop in inputObj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var property = prop.Name;

                // skip @id
                if (property == "@id")
                    continue;

                // handle reverse properties
                if (property == "@reverse")
                {
                    var referencedNode = new JObject();
                    referencedNode["@id"] = name;
                    if (inputObj.TryGetValue("@reverse", out var reverseMapProp) && reverseMapProp.Type == JTokenType.Object)
                    {
                        foreach (var reverseProperty in ((JObject)reverseMapProp).Properties())
                        {
                            if (reverseProperty.Value.Type == JTokenType.Array)
                            {
                                foreach (var item in reverseProperty.Value.ToArray().OfType<JObject>())
                                {
                                    var itemName = item["@id"]?.ToString();
                                    if (Utils.IsBlankNode(item))
                                        itemName = issuer.GetId(itemName);

                                    CreateNodeMap(item, graphs, graph, issuer, itemName);
                                    if (!subjects.TryGetValue(itemName, out var subjectItemProp) ||
                                        subjectItemProp.Type != JTokenType.Object)
                                    {
                                        throw new JsonLdParseException($"Item \"{itemName}\" has to be an object during " +
                                                                       $"\"{graph}\" graph reversal");
                                    }
                                    Utils.AddValue((JObject)subjectItemProp, reverseProperty.Name, referencedNode,
                                                   propertyIsArray: true, allowDuplicate: false);
                                }
                            }
                        }
                    }
                    continue;
                }

                // recurse into graph
                if (property == "@graph")
                {
                    // add graph subjects map entry
                    if (name is not null && !graphs.ContainsKey(name))
                        graphs[name] = new JObject();

                    CreateNodeMap(prop.Value, graphs, name, issuer);
                    continue;
                }

                // recurse into included
                if (property == "@included")
                {
                    CreateNodeMap(prop.Value, graphs, graph, issuer);
                    continue;
                }

                // copy non-@type keywords
                if (property != "@type" && Utils.IsKeyword(property))
                {
                    if (property == "@index" && subject.TryGetValue(property, out var subjectProperty) &&
                        (prop.Value != subjectProperty || (prop.Value as JObject)?["@id"] != (subjectProperty as JObject)?["@id"]))
                    {
                        throw new JsonLdParseException("Invalid JSON-LD syntax; conflicting @index property detected.");
                    }

                    subject[property] = prop.Value;
                    continue;
                }

                // iterate over objects
                if (prop.Value.Type == JTokenType.Array)
                {
                    var objects = prop.Value.ToArray();

                    // if property is a bnode, assign it a new id
                    if (property.IndexOf("_:") == 0)
                    {
                        property = issuer.GetId(property);
                    }

                    // ensure property is added for empty arrays
                    if (objects.Length == 0)
                    {
                        Utils.AddValue(subject, property, new JArray(), propertyIsArray: true);
                        continue;
                    }
                    foreach (var obj in objects)
                    {
                        var o = obj;
                        if (property == "@type" && o.Type == JTokenType.String && o.Value<string>().IndexOf("_:") == 0)
                            o = issuer.GetId(o.Value<string>());// rename @type blank nodes

                        // handle embedded subject or subject reference
                        if (Utils.IsGraphSubject(o) || Utils.IsGraphSubjectReference(o))
                        {
                            var oObj = (JObject)o;
                            // skip null @id
                            if (oObj.TryGetValue("@id", out var oIdProp) &&
                                (Utils.IsEmptyObject(oIdProp) || (oIdProp.Type == JTokenType.String &&
                                                                  String.IsNullOrEmpty(oIdProp.Value<string>()))))
                            {
                                continue;
                            }

                            // relabel blank node @id
                            var id = oIdProp?.ToString();
                            if (id is null || Utils.IsBlankNode(o))
                                id = issuer.GetId(id);

                            // add reference and recurse
                            var newObj = new JObject();
                            newObj["@id"] = id;
                            Utils.AddValue(subject, property, newObj, propertyIsArray: true, allowDuplicate: false);
                            CreateNodeMap(o, graphs, graph, issuer, id);
                        }
                        else if (Utils.IsGraphValue(o))
                        {
                            Utils.AddValue(subject, property, o, propertyIsArray: true, allowDuplicate: false);
                        }
                        else if (Utils.IsGraphList(o))
                        {
                            // handle @list
                            var _list = new JArray();
                            CreateNodeMap(o["@list"], graphs, graph, issuer, name, _list);
                            var newObj = new JObject();
                            newObj["@list"] = _list;
                            Utils.AddValue(subject, property, newObj, propertyIsArray: true, allowDuplicate: false);
                        }
                        else
                        {
                            // handle @value
                            CreateNodeMap(o, graphs, graph, issuer, name);
                            Utils.AddValue(subject, property, o, propertyIsArray: true, allowDuplicate: false);
                        }
                    }
                }
            }
        }

        private static void GraphToRDF(List<Quad> dataset, JObject graph, QuadItem graphTerm, IdentifierIssuer issuer)
        {
            foreach (var prop in graph.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var id = prop.Name;
                var node = prop.Value;
                if (node.Type == JTokenType.Object)
                {
                    foreach (var nodeProp in ((JObject)node).Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        var property = nodeProp.Name;
                        var items = nodeProp.Value;
                        if (property == "@type")
                            property = QuadItem.RDF + "type";
                        else if (Utils.IsKeyword(property))
                            continue;

                        if (items.Type == JTokenType.Array)
                        {
                            foreach (var item in items.ToArray())
                            {
                                // RDF subject
                                var subject = new QuadItem
                                {
                                    TermType = id.StartsWith("_:") ? TermType.BlankNode : TermType.NamedNode,
                                    Value = id
                                };

                                // skip relative IRI subjects (not valid RDF)
                                if (!Utils.IsIriAbsolute(id))
                                {
                                    continue;
                                }

                                // RDF predicate
                                var predicate = new QuadItem
                                {
                                    TermType = property.StartsWith("_:") ? TermType.BlankNode : TermType.NamedNode,
                                    Value = property
                                };

                                // skip relative IRI predicates (not valid RDF)
                                if (!Utils.IsIriAbsolute(property))
                                {
                                    continue;
                                }

                                // skip blank node predicates unless producing generalized RDF
                                if (predicate.TermType == TermType.BlankNode) //&& !options.produceGeneralizedRdf)
                                {
                                    continue;
                                }

                                // convert list, value or node object to triple
                                var quadObject = ObjectToRDF(item, issuer, dataset, graphTerm);//, options.rdfDirection);
                                // skip null objects (they are relative IRIs)
                                if (quadObject is not null)
                                {
                                    dataset.Add(new Quad
                                    {
                                        Subject = subject,
                                        Predicate = predicate,
                                        Object = quadObject,
                                        Graph = graphTerm
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        private static ObjectQuadItem ObjectToRDF(JToken item, IdentifierIssuer issuer, List<Quad> dataset,
                                                  QuadItem graphTerm, string rdfDirection = null)
        {
            var result = new ObjectQuadItem();

            // convert value object to RDF
            if (Utils.IsGraphValue(item))
            {
                result.TermType = TermType.Literal;
                result.Value = null;
                result.DataType = new QuadItem
                {
                    TermType = TermType.NamedNode
                };
                var value = item["@value"];
                string datatype = null;
                if (item.Type == JTokenType.Object && ((JObject)item).TryGetValue("@type", out var typeProp))
                {
                    if (typeProp.Type == JTokenType.Array)
                        datatype = String.Join(",", typeProp.ToArray().Select(t => t.ToString())); //this is handling for peculiar `^^<${o.datatype.value}>` result in jsonld.js NQuads.serializeQuad
                    else
                        datatype = typeProp.ToString();
                }

                // convert to XSD/JSON datatypes as appropriate
                if (datatype == "@json")
                {
                    result.Value = JsonCanonicalize(value);
                    result.DataType.Value = QuadItem.RDF + "JSON";
                }
                else if (value.Type == JTokenType.Boolean)
                {
                    result.Value = value.ToString(Formatting.None);
                    result.DataType.Value = datatype ?? QuadItem.XSD + "boolean";
                }
                else if (value.Type == JTokenType.Float || datatype == (QuadItem.XSD + "double"))
                {
                    if (datatype != (QuadItem.XSD + "double") && Int32.TryParse(value.ToString(), out var floatIntVal))
                    {
                        result.Value = floatIntVal.ToString();
                        result.DataType.Value = datatype ?? QuadItem.XSD + "integer";
                    }
                    else
                    {
                        if (value.Type != JTokenType.Float)
                            value = Double.Parse(value.ToString(), CultureInfo.InvariantCulture);

                        // canonical double representation
                        result.Value = value.Value<double>().ToString("0.0##############E-0"); //toExponential(15).replace(/(\d)0*e\+?/, '$1E')
                        result.DataType.Value = datatype ?? QuadItem.XSD + "double";
                    }
                }
                else if (value.Type == JTokenType.Integer)
                {
                    result.Value = value.Value<long>().ToString();
                    result.DataType.Value = datatype ?? QuadItem.XSD + "integer";
                }
                else if (rdfDirection == "i18n-datatype" && ((JObject)item).TryGetValue("@direction", out var directionProp))
                {
                    datatype = "https://www.w3.org/ns/i18n#" + (item["@language"] ?? "") + $"_{directionProp.ToString()}";
                    result.DataType.Value = datatype;
                    result.Value = value.ToString();
                }
                else if (((JObject)item).TryGetValue("@language", out var landProp))
                {
                    result.Value = value.ToString();
                    result.DataType.Value = datatype ?? QuadItem.RDF + "langString";
                    result.Language = landProp.ToString();
                }
                else
                {
                    result.Value = value.ToString();
                    result.DataType.Value = datatype ?? QuadItem.XSD + "string";
                }
            }
            else if (Utils.IsGraphList(item))
            {
                var _list = ListToRDF(item["@list"].ToArray().ToList(), issuer, dataset, graphTerm, rdfDirection);
                result.TermType = _list.TermType;
                result.Value = _list.Value;
            }
            else
            {
                // convert string/node object to RDF
                var id = (item.Type == JTokenType.Object ? item["@id"] : item)?.ToString();
                result.TermType = id is not null && id.StartsWith("_:") ? TermType.BlankNode : TermType.NamedNode;
                result.Value = id;
            }

            // skip relative IRIs, not valid RDF
            if (result.TermType == TermType.NamedNode && !Utils.IsIriAbsolute(result.Value))
                return null;

            return result;
        }

        private static string JsonCanonicalize(JToken value)
        {
            if (value?.Type == JTokenType.Array)
            {
                return "[" + value.ToArray().Aggregate("", (acc, val) => {
                    var comma = acc.Length == 0 ? "" : ",";
                    if (val?.Type == JTokenType.Undefined)// || typeof cv === 'symbol'
                        val = null;
                    return acc + comma + JsonCanonicalize(val);
                }) + "]";
            }

            if (value?.Type == JTokenType.Object)
            {
                return "{" + ((JObject)value).Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Aggregate("", (acc, prop) => {
                    if (prop.Value?.Type == JTokenType.Undefined)// || typeof value[cv] === 'symbol')
                        return acc;
                    var comma = acc.Length == 0 ? "" : ",";
                    return acc + comma + JsonCanonicalize(prop.Name) + ':' + JsonCanonicalize(prop.Value);
                }) + "}";
            }

            var noFormattingTypes = new JTokenType?[] { JTokenType.Boolean, JTokenType.String, JTokenType.Null };
            if (noFormattingTypes.Contains(value?.Type))
                return value.ToString(Formatting.None);
            if (value?.Type == JTokenType.Float)
                return value.ToString().ToLower();

            return value?.ToString() ?? "";
        }

        private static QuadItem ListToRDF(List<JToken> list, IdentifierIssuer issuer, List<Quad> dataset,
                                          QuadItem graphTerm, string rdfDirection)
        {
            var first = new QuadItem { TermType = TermType.NamedNode, Value = QuadItem.RDF + "first" };
            var rest = new QuadItem { TermType = TermType.NamedNode, Value = QuadItem.RDF + "rest" };
            var nil = new QuadItem { TermType = TermType.NamedNode, Value = QuadItem.RDF + "nil" };

            var last = list.LastOrDefault();
            if (last is not null)
                list.RemoveAt(list.Count - 1);

            // Result is the head of the list
            var result = last is not null ? new QuadItem { TermType = TermType.BlankNode, Value = issuer.GetId() } : nil;
            var subject = result;

            foreach (var item in list)
            {
                var obj = ObjectToRDF(item, issuer, dataset, graphTerm, rdfDirection);
                var next = new QuadItem { TermType = TermType.BlankNode, Value = issuer.GetId() };
                dataset.Add(new Quad
                {
                    Subject = subject,
                    Predicate = first,
                    Object = obj,
                    Graph = graphTerm
                });
                dataset.Add(new Quad
                {
                    Subject = subject,
                    Predicate = rest,
                    Object = new ObjectQuadItem { TermType = next.TermType, Value = next.Value },
                    Graph = graphTerm
                });
                subject = next;
            }

            // Tail of list
            if (last != null)
            {
                var obj = ObjectToRDF(last, issuer, dataset, graphTerm, rdfDirection);
                dataset.Add(new Quad
                {
                    Subject = subject,
                    Predicate = first,
                    Object = obj,
                    Graph = graphTerm
                });
                dataset.Add(new Quad
                {
                    Subject = subject,
                    Predicate = rest,
                    Object = new ObjectQuadItem { TermType = nil.TermType, Value = nil.Value },
                    Graph = graphTerm
                });
            }

            return result;
        }
    }
}
