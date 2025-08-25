/**
 * Parts of the source code in this file has been translated/ported from rdf-canonize library by Digital Bazaar (BSD 3-Clause license)
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Specialized;

namespace JsonLd.Normalization
{
    internal class IdentifierIssuer
    {
        private readonly OrderedDictionary existing;
        public string Prefix { get; init; }
        public int Counter { get; private set; }


        /**
        * Creates a new IdentifierIssuer. A IdentifierIssuer issues unique
        * identifiers, keeping track of any previously issued identifiers.
        *
        * @param prefix the prefix to use ('<prefix><counter>').
        * @param existing an existing Map to use.
        * @param counter the counter to use.
        */
        public IdentifierIssuer(string prefix, OrderedDictionary existing = null, int counter = 0)
        {
            this.Prefix = prefix;
            this.existing = existing ?? new();
            this.Counter = counter;
        }

        /**
         * Copies this IdentifierIssuer.
         *
         * @return a copy of this IdentifierIssuer.
         */
        public IdentifierIssuer Clone()
        {
            var dictCopy = new OrderedDictionary();
            foreach (var key in existing.Keys)
                dictCopy.Add(key, existing[key]);
            return new IdentifierIssuer(Prefix, dictCopy, Counter);
        }

        /**
         * Gets the new identifier for the given old identifier, where if no old
         * identifier is given a new identifier will be generated.
         *
         * @param [old] the old identifier to get the new identifier for.
         *
         * @return the new identifier.
         */
        public string GetId(string old = null)
        {
            // return existing old identifier
            if (!String.IsNullOrEmpty(old))
            {
                var existingOld = existing[old];
                if (existingOld is not null)
                    return (string)existingOld;
            }

            // get next identifier
            var identifier = Prefix + Counter++;

            // save mapping
            if (!String.IsNullOrEmpty(old))
                existing[old] = identifier;

            return identifier;
        }

        /**
         * Returns true if the given old identifer has already been assigned a new
         * identifier.
         *
         * @param old the old identifier to check.
         *
         * @return true if the old identifier has been assigned a new identifier,
         *   false if not.
         */
        public bool HasId(string old)
        {
            return existing.Contains(old);
        }

        /**
         * Returns all of the IDs that have been issued new IDs in the order in
         * which they were issued new IDs.
         *
         * @return the list of old IDs that has been issued new IDs in order.
         */
        public IEnumerable<string> GetOldIds()
        {
            return existing.Keys.Cast<string>();
        }
    }
}
