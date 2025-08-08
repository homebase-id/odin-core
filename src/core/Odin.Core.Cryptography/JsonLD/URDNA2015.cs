/**
 * Parts of the source code in this file has been translated/ported from rdf-canonize library by Digital Bazaar (BSD 3-Clause license)
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace JsonLd.Normalization
{
    internal static class URDNA2015
    {
        /// <summary>
        /// Implements RDF Dataset Normalization Algorithm, code is translated from rdf-canonize javascript package version 3.0.0
        /// </summary>
        /// <param name="dataset">a list of Quad objects to be normalized</param>
        /// <returns>normalized dataset as a string</returns>
        public static string Normalize(List<Quad> dataset)
        {
            var blankNodeInfo = new Dictionary<string, BlankNodeInfo>();
            var canonicalIssuer = new IdentifierIssuer("_:c14n");

            var quads = dataset;

            // 1) Create the normalization state.
            // 2) For every quad in input dataset:
            foreach (var quad in dataset)
            {
                // 2.1) For each blank node that occurs in the quad, add a reference
                // to the quad using the blank node identifier in the blank node to
                // quads map, creating a new entry if necessary.
                AddBlankNodeQuadInfo(quad, quad.Subject, blankNodeInfo);
                AddBlankNodeQuadInfo(quad, quad.Object, blankNodeInfo);
                AddBlankNodeQuadInfo(quad, quad.Graph, blankNodeInfo);
            }

            // 3) Create a list of non-normalized blank node identifiers
            // non-normalized identifiers and populate it using the keys from the
            // blank node to quads map.
            // Note: We use a map here and it was generated during step 2.

            // 4) `simple` flag is skipped -- loop is optimized away. This optimization
            // is permitted because there was a typo in the hash first degree quads
            // algorithm in the URDNA2015 spec that was implemented widely making it
            // such that it could not be fixed; the result was that the loop only
            // needs to be run once and the first degree quad hashes will never change.
            // 5.1-5.2 are skipped; first degree quad hashes are generated just once
            // for all non-normalized blank nodes.

            // 5.3) For each blank node identifier identifier in non-normalized
            // identifiers:
            var hashToBlankNodes = new Dictionary<string, HashSet<string>>();
            var nonNormalized = blankNodeInfo.Keys.ToArray();
            foreach (var id in nonNormalized)
            {
                // steps 5.3.1 and 5.3.2:
                HashAndTrackBlankNode(id, hashToBlankNodes, blankNodeInfo);
            }

            // 5.4) For each hash to identifier list mapping in hash to blank
            // nodes map, lexicographically-sorted by hash:
            var hashes = hashToBlankNodes.Keys.OrderBy(k => k).ToArray();
            // optimize away second sort, gather non-unique hashes in order as we go
            var nonUnique = new List<HashSet<string>>();
            foreach (var hash in hashes)
            {
                // 5.4.1) If the length of identifier list is greater than 1,
                // continue to the next mapping.
                var idList = hashToBlankNodes[hash];
                if (idList.Count > 1)
                {
                    nonUnique.Add(idList);
                    continue;
                }

                // 5.4.2) Use the Issue Identifier algorithm, passing canonical
                // issuer and the single blank node identifier in identifier
                // list, identifier, to issue a canonical replacement identifier
                // for identifier.
                var id = idList.First();
                canonicalIssuer.GetId(id);

                // Note: These steps are skipped, optimized away since the loop
                // only needs to be run once.
                // 5.4.3) Remove identifier from non-normalized identifiers.
                // 5.4.4) Remove hash from the hash to blank nodes map.
                // 5.4.5) Set simple to true.
            }

            // 6) For each hash to identifier list mapping in hash to blank nodes map,
            // lexicographically-sorted by hash:
            // Note: sort optimized away, use `nonUnique`.
            foreach (var idList in nonUnique)
            {
                // 6.1) Create hash path list where each item will be a result of
                // running the Hash N-Degree Quads algorithm.
                var hashPathList = new List<(string, IdentifierIssuer)>();

                // 6.2) For each blank node identifier identifier in identifier list:
                foreach (var id in idList)
                {
                    // 6.2.1) If a canonical identifier has already been issued for
                    // identifier, continue to the next identifier.
                    if (canonicalIssuer.HasId(id))
                        continue;

                    // 6.2.2) Create temporary issuer, an identifier issuer
                    // initialized with the prefix _:b.
                    var issuer = new IdentifierIssuer("_:b");

                    // 6.2.3) Use the Issue Identifier algorithm, passing temporary
                    // issuer and identifier, to issue a new temporary blank node
                    // identifier for identifier.
                    issuer.GetId(id);

                    // 6.2.4) Run the Hash N-Degree Quads algorithm, passing
                    // temporary issuer, and append the result to the hash path list.
                    var result = HashNDegreeQuads(id, issuer, blankNodeInfo, canonicalIssuer);
                    hashPathList.Add(result);
                }

                // 6.3) For each result in the hash path list,
                // lexicographically-sorted by the hash in result:
                hashPathList.Sort((a, b) => String.Compare(a.Item1, b.Item1));
                foreach (var (_, resIssuer) in hashPathList)
                {
                    // 6.3.1) For each blank node identifier, existing identifier,
                    // that was issued a temporary identifier by identifier issuer
                    // in result, issue a canonical identifier, in the same order,
                    // using the Issue Identifier algorithm, passing canonical
                    // issuer and existing identifier.
                    var oldIds = resIssuer.GetOldIds();
                    foreach (var id in oldIds)
                        canonicalIssuer.GetId(id);
                }
            }

            /* Note: At this point all blank nodes in the set of RDF quads have been
            assigned canonical identifiers, which have been stored in the canonical
            issuer. Here each quad is updated by assigning each of its blank nodes
            its new identifier. */

            // 7) For each quad, quad, in input dataset:
            var normalized = new List<string>();
            foreach (var quad in quads)
            {
                // 7.1) Create a copy, quad copy, of quad and replace any existing
                // blank node identifiers using the canonical identifiers
                // previously issued by canonical issuer.
                // Note: We optimize with shallow copies here.
                var q = new Quad
                {
                    Subject = UseCanonicalId(quad.Subject, canonicalIssuer),
                    Object = UseCanonicalId(quad.Object, canonicalIssuer),
                    Graph = UseCanonicalId(quad.Graph, canonicalIssuer),
                    Predicate = quad.Predicate
                };
                // 7.2) Add quad copy to the normalized dataset.
                normalized.Add(SerializeQuad(q));
            }

            // sort normalized output
            normalized.Sort(StringComparer.Ordinal);

            // 8) Return the normalized dataset.
            return String.Join("", normalized);
        }

        private static void AddBlankNodeQuadInfo(Quad quad, QuadItem component, Dictionary<string, BlankNodeInfo> blankNodeInfo)
        {
            if (component.TermType != TermType.BlankNode)
                return;

            var id = component.Value;
            if (blankNodeInfo.TryGetValue(id, out var info))
                info.Quads.Add(quad);
            else
                blankNodeInfo[id] = new BlankNodeInfo { Quads = new HashSet<Quad> { quad }, Hash = null };
        }

        private static void HashAndTrackBlankNode(string id, Dictionary<string, HashSet<string>> hashToBlankNodes,
                                                  Dictionary<string, BlankNodeInfo> blankNodeInfo)
        {
            // 5.3.1) Create a hash, hash, according to the Hash First Degree
            // Quads algorithm.
            var hash = HashFirstDegreeQuads(id, blankNodeInfo);

            // 5.3.2) Add hash and identifier to hash to blank nodes map,
            // creating a new entry if necessary.
            if (!hashToBlankNodes.TryGetValue(hash, out var idList))
                hashToBlankNodes[hash] = new HashSet<string> { id };
            else
                idList.Add(id);
        }

        private static string HashFirstDegreeQuads(string id, Dictionary<string, BlankNodeInfo> blankNodeInfo)
        {
            // 1) Initialize nquads to an empty list. It will be used to store quads in
            // N-Quads format.
            var nquads = new List<string>();

            // 2) Get the list of quads `quads` associated with the reference blank node
            // identifier in the blank node to quads map.
            var info = blankNodeInfo[id];
            var quads = info.Quads;

            // 3) For each quad `quad` in `quads`:
            foreach (var quad in quads)
            {
                // 3.1) Serialize the quad in N-Quads format with the following special
                // rule:

                // 3.1.1) If any component in quad is an blank node, then serialize it
                // using a special identifier as follows:
                var copy = new Quad { Subject = null, Predicate = quad.Predicate, Object = null, Graph = null };
                // 3.1.2) If the blank node's existing blank node identifier matches
                // the reference blank node identifier then use the blank node
                // identifier _:a, otherwise, use the blank node identifier _:z.
                copy.Subject = ModifyFirstDegreeComponent(id, quad.Subject);
                copy.Object = ModifyFirstDegreeComponent(id, quad.Object);
                copy.Graph = ModifyFirstDegreeComponent(id, quad.Graph);
                nquads.Add(SerializeQuad(copy));
            }

            // 4) Sort nquads in lexicographical order.
            nquads.Sort(StringComparer.Ordinal);

            // 5) Return the hash that results from passing the sorted, joined nquads
            // through the hash algorithm.
            var md = new MessageDigest();
            foreach (var nquad in nquads)
                md.Update(nquad);

            info.Hash = md.Digest();
            return info.Hash;
        }

        private static T ModifyFirstDegreeComponent<T>(string id, T component) where T : QuadItem, new()
        {
            if (component.TermType != TermType.BlankNode)
                return component;

            /* Note: A mistake in the URDNA2015 spec that made its way into
            implementations (and therefore must stay to avoid interop breakage)
            resulted in an assigned canonical ID, if available for
            `component.value`, not being used in place of `_:a`/`_:z`, so
            we don't use it here. */
            return new T
            {
                TermType = TermType.BlankNode,
                Value = component.Value == id ? "_:a" : "_:z"
            };
        }

        private static string SerializeQuad(Quad quad)
        {
            var s = quad.Subject;
            var p = quad.Predicate;
            var o = quad.Object;
            var g = quad.Graph;

            var nquad = new StringBuilder();

            // subject can only be NamedNode or BlankNode
            if (s.TermType == TermType.NamedNode)
                nquad.Append($"<{s.Value}>");
            else
                nquad.Append($"{s.Value}");

            // predicate can only be NamedNode
            nquad.Append($" <{p.Value}> ");

            // object is NamedNode, BlankNode, or Literal
            if (o.TermType == TermType.NamedNode)
            {
                nquad.Append($"<{o.Value}>");
            }
            else if (o.TermType == TermType.BlankNode)
            {
                nquad.Append(o.Value);
            }
            else
            {
                nquad.Append($"\"{EscapeQuadValue(o.Value)}\"");
                if (o.DataType.Value == QuadItem.RDF + "langString")
                {
                    if (!String.IsNullOrEmpty(o.Language))
                    {
                        nquad.Append($"@{o.Language}");
                    }
                }
                else if (o.DataType.Value != QuadItem.XSD + "string")
                {
                    nquad.Append($"^^<{o.DataType.Value}>");
                }
            }

            // graph can only be NamedNode or BlankNode (or DefaultGraph, but that
            // does not add to `nquad`)
            if (g.TermType == TermType.NamedNode)
            {
                nquad.Append($" <{g.Value}>");
            }
            else if (g.TermType == TermType.BlankNode)
            {
                nquad.Append($" {g.Value}");
            }

            nquad.Append(" .\n");
            return nquad.ToString();
        }

        private static string EscapeQuadValue(string value)
        {
            return Regex.Replace(value, @"[""\\\n\r]", m =>
                m.Value switch
                {
                    "\"" => "\\\"",
                    "\\" => "\\\\",
                    "\n" => "\\n",
                    "\r" => "\\r",
                    _ => m.Value
                });
        }

        private static (string, IdentifierIssuer) HashNDegreeQuads(string id, IdentifierIssuer issuer,
                                                                   Dictionary<string, BlankNodeInfo> blankNodeInfo,
                                                                   IdentifierIssuer canonicalIssuer)
        {
            // 1) Create a hash to related blank nodes map for storing hashes that
            // identify related blank nodes.
            // Note: 2) and 3) handled within `createHashToRelated`
            var md = new MessageDigest();
            var hashToRelated = CreateHashToRelated(id, issuer, blankNodeInfo, canonicalIssuer);

            // 4) Create an empty string, data to hash.
            // Note: We created a hash object `md` above instead.

            // 5) For each related hash to blank node list mapping in hash to related
            // blank nodes map, sorted lexicographically by related hash:
            var hashes = hashToRelated.Keys.ToList();
            hashes.Sort();
            foreach (var hash in hashes)
            {
                // 5.1) Append the related hash to the data to hash.
                md.Update(hash);

                // 5.2) Create a string chosen path.
                var chosenPath = "";

                // 5.3) Create an unset chosen issuer variable.
                IdentifierIssuer chosenIssuer = null;

                // 5.4) For each permutation of blank node list:
                var permuter = new Permuter(hashToRelated[hash]);
                while (permuter.HasNext())
                {
                    var permutation = permuter.Next();

                    // 5.4.1) Create a copy of issuer, issuer copy.
                    var issuerCopy = issuer.Clone();

                    // 5.4.2) Create a string path.
                    var pathBuilder = new StringBuilder();

                    // 5.4.3) Create a recursion list, to store blank node identifiers
                    // that must be recursively processed by this algorithm.
                    var recursionList = new List<string>();

                    // 5.4.4) For each related in permutation:
                    var nextPermutation = false;
                    foreach (var related in permutation)
                    {
                        // 5.4.4.1) If a canonical identifier has been issued for
                        // related, append it to path.
                        if (canonicalIssuer.HasId(related))
                        {
                            pathBuilder.Append(canonicalIssuer.GetId(related));
                        }
                        else
                        {
                            // 5.4.4.2) Otherwise:
                            // 5.4.4.2.1) If issuer copy has not issued an identifier for
                            // related, append related to recursion list.
                            if (!issuerCopy.HasId(related))
                            {
                                recursionList.Add(related);
                            }
                            // 5.4.4.2.2) Use the Issue Identifier algorithm, passing
                            // issuer copy and related and append the result to path.
                            pathBuilder.Append(issuerCopy.GetId(related));
                        }

                        // 5.4.4.3) If chosen path is not empty and the length of path
                        // is greater than or equal to the length of chosen path and
                        // path is lexicographically greater than chosen path, then
                        // skip to the next permutation.
                        // Note: Comparing path length to chosen path length can be optimized
                        // away; only compare lexicographically.
                        if (chosenPath.Length != 0 && String.Compare(pathBuilder.ToString(), chosenPath) > 0)
                        {
                            nextPermutation = true;
                            break;
                        }
                    }

                    if (nextPermutation)
                        continue;

                    // 5.4.5) For each related in recursion list:
                    foreach (var related in recursionList)
                    {
                        // 5.4.5.1) Set result to the result of recursively executing
                        // the Hash N-Degree Quads algorithm, passing related for
                        // identifier and issuer copy for path identifier issuer.
                        var (resHash, resIssuer) = HashNDegreeQuads(related, issuerCopy, blankNodeInfo, canonicalIssuer);

                        // 5.4.5.2) Use the Issue Identifier algorithm, passing issuer
                        // copy and related and append the result to path.
                        pathBuilder.Append(issuerCopy.GetId(related));

                        // 5.4.5.3) Append <, the hash in result, and > to path.
                        pathBuilder.Append($"<{resHash}>");

                        // 5.4.5.4) Set issuer copy to the identifier issuer in
                        // result.
                        issuerCopy = resIssuer;

                        // 5.4.5.5) If chosen path is not empty and the length of path
                        // is greater than or equal to the length of chosen path and
                        // path is lexicographically greater than chosen path, then
                        // skip to the next permutation.
                        // Note: Comparing path length to chosen path length can be optimized
                        // away; only compare lexicographically.
                        if (chosenPath.Length != 0 && String.Compare(pathBuilder.ToString(), chosenPath) > 0)
                        {
                            nextPermutation = true;
                            break;
                        }
                    }

                    if (nextPermutation)
                        continue;

                    // 5.4.6) If chosen path is empty or path is lexicographically
                    // less than chosen path, set chosen path to path and chosen
                    // issuer to issuer copy.
                    if (chosenPath.Length == 0 || String.Compare(pathBuilder.ToString(), chosenPath) < 0)
                    {
                        chosenPath = pathBuilder.ToString();
                        chosenIssuer = issuerCopy;
                    }
                }

                // 5.5) Append chosen path to data to hash.
                md.Update(chosenPath);

                // 5.6) Replace issuer, by reference, with chosen issuer.
                issuer = chosenIssuer;
            }

            // 6) Return issuer and the hash that results from passing data to hash
            // through the hash algorithm.
            return (md.Digest(), issuer);
        }

        private static Dictionary<string, List<string>> CreateHashToRelated(string id, IdentifierIssuer issuer,
                                                                            Dictionary<string, BlankNodeInfo> blankNodeInfo,
                                                                            IdentifierIssuer canonicalIssuer)
        {
            // 1) Create a hash to related blank nodes map for storing hashes that
            // identify related blank nodes.
            var hashToRelated = new Dictionary<string, List<string>>();

            // 2) Get a reference, quads, to the list of quads in the blank node to
            // quads map for the key identifier.
            var quads = blankNodeInfo[id].Quads;

            // 3) For each quad in quads:
            foreach (var quad in quads)
            {
                // 3.1) For each component in quad, if component is the subject, object,
                // or graph name and it is a blank node that is not identified by
                // identifier:
                // steps 3.1.1 and 3.1.2 occur in helpers:
                AddRelatedBlankNodeHash(quad, quad.Subject, 's', id, issuer, hashToRelated, blankNodeInfo, canonicalIssuer);
                AddRelatedBlankNodeHash(quad, quad.Object, 'o', id, issuer, hashToRelated, blankNodeInfo, canonicalIssuer);
                AddRelatedBlankNodeHash(quad, quad.Graph, 'g', id, issuer, hashToRelated, blankNodeInfo, canonicalIssuer);
            }

            return hashToRelated;
        }

        private static void AddRelatedBlankNodeHash(Quad quad, QuadItem component, char position, string id,
                                                    IdentifierIssuer issuer, Dictionary<string, List<string>> hashToRelated,
                                                    Dictionary<string, BlankNodeInfo> blankNodeInfo,
                                                    IdentifierIssuer canonicalIssuer)
        {
            if (!(component.TermType == TermType.BlankNode && component.Value != id))
                return;

            // 3.1.1) Set hash to the result of the Hash Related Blank Node
            // algorithm, passing the blank node identifier for component as
            // related, quad, path identifier issuer as issuer, and position as
            // either s, o, or g based on whether component is a subject, object,
            // graph name, respectively.
            var related = component.Value;
            var hash = HashRelatedBlankNode(related, quad, issuer, position, blankNodeInfo, canonicalIssuer);

            // 3.1.2) Add a mapping of hash to the blank node identifier for
            // component to hash to related blank nodes map, adding an entry as
            // necessary.
            if (hashToRelated.TryGetValue(hash, out var entries))
                entries.Add(related);
            else
                hashToRelated[hash] = new List<string> { related };
        }

        private static string HashRelatedBlankNode(string related, Quad quad, IdentifierIssuer issuer, char position,
                                                   Dictionary<string, BlankNodeInfo> blankNodeInfo,
                                                   IdentifierIssuer canonicalIssuer)
        {
            // 1) Set the identifier to use for related, preferring first the canonical
            // identifier for related if issued, second the identifier issued by issuer
            // if issued, and last, if necessary, the result of the Hash First Degree
            // Quads algorithm, passing related.
            string id;
            if (canonicalIssuer.HasId(related))
                id = canonicalIssuer.GetId(related);
            else if (issuer.HasId(related))
                id = issuer.GetId(related);
            else
                id = blankNodeInfo[related].Hash;

            // 2) Initialize a string input to the value of position.
            // Note: We use a hash object instead.
            var md = new MessageDigest();
            md.Update(new String(position, 1));

            // 3) If position is not g, append <, the value of the predicate in quad,
            // and > to input.
            if (position != 'g')
                md.Update($"<{quad.Predicate.Value}>"); //getRelatedPredicate

            // 4) Append identifier to input.
            md.Update(id);

            // 5) Return the hash that results from passing input through the hash
            // algorithm.
            return md.Digest();
        }

        private static T UseCanonicalId<T>(T component, IdentifierIssuer canonicalIssuer) where T : QuadItem, new()
        {
            if (component.TermType == TermType.BlankNode &&
                !component.Value.StartsWith(canonicalIssuer.Prefix))
            {
                return new T
                {
                    TermType = TermType.BlankNode,
                    Value = canonicalIssuer.GetId(component.Value)
                };
            }
            return component;
        }
    }
}