using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Odin.Core.Util;

// Enable testing
[assembly: InternalsVisibleTo("Odin.Core.Tests")]

namespace Odin.Core.Trie
{
    public class Trie<T> // where T : class
    {
        // Maps ASCII character to Trie[] DNS node index, 128 means illegal
        // Lowercase and uppercase characters are mapped to the same index.
        public static readonly byte[] m_aTrieMap =
        {
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 000-009
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 010-019
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 020-029
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 030-039
            128, 128, 128, 128, 128,  26,  27, 128,  28,  29, // 040-049 '-' 45, '.' 46, '0' 48, '1' 49
             30,  31,  32,  33,  34,  35,  36,  37, 128, 128, // 050-059 ..'9' is 57
            128, 128, 128, 128, 128,   0,   1,   2,   3,   4, // 060-069 'A' is 65
              5,   6,   7,   8,   9,  10,  11,  12,  13,  14, // 070-079
             15,  16,  17,  18,  19,  20,  21,  22,  23,  24, // 080-089
             25, 128, 128, 128, 128, 128, 128,   0,   1,   2, // 090-099 'a' is 97
              3,   4,   5,   6,   7,   8,   9,  10,  11,  12, // 100-109
             13,  14,  15,  16,  17,  18,  19,  20,  21,  22, // 110-119
             23,  24,  25, 128, 128, 128, 128, 128, 128, 128, // 120-129 
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 130-139
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 140-149
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 150-159
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 160-169
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 170-179
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 180-189
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 190-199
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 200-209 
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 210-219
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 220-229
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 230-239
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 240-249
            128, 128, 128, 128, 128, 128                      // 250-255
        };

        private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();

        private struct NodeData
        {
            public NodeData[] NodeArray; // either null, or points to an array of 128 Nodes
            public T DataClass; // Either null if this is not a stop node, or a class that holds data
        }

        private NodeData m_NodeRoot = new(); // The Trie root

        private void CreateArray(ref NodeData n)
        {
            // Only valid chars are 0-9, a-z, -, .
            // That's 38 entries using the table above (0-37 '9')
            n.NodeArray = new NodeData[38];
        }

        public Trie()
        {
            // Initialize the root node

            // Are all values in C# 0 (and null) on creation?
            CreateArray(ref m_NodeRoot);
        }


        /// <summary>
        /// Checks if sName is a valid domain name and also checks if adding sName would violate
        /// an existing domain name already registered in the Trie. For example it is not valid
        /// to register valhalla.com if odin.valhalla.com is already in the trie. And vice versa.
        /// </summary>
        /// <param name="asciiName">domain name to check</param>
        /// <returns></returns>
        private bool InternalIsDomainUniqueInHierarchy(string asciiName)
        {
            AsciiDomainNameValidator.AssertValidDomain(asciiName); // Throws an exception if not OK

            ref var p = ref m_NodeRoot;

            int c;
            for (var i = asciiName.Length - 1; i >= 0; i--)
            {
                c = m_aTrieMap[asciiName[i]]; // Map (and ignore case)

                if (c == 128) // Illegal character
                    throw new Exception();

                // If there is no name here, then we're OK
                if (p.NodeArray == null)
                    return true;

                p = ref p.NodeArray[c];

                // i == 0 then we've now matched every char. So if p has an identity key, or
                // if there is a period, then we're not OK
                if (i == 0)
                {
                    if (!EqualityComparer<T>.Default.Equals(p.DataClass, default))
                        return false;

                    if (p.NodeArray == null) // If there are no mode nodes we are OK.
                        return true;

                    ref var q = ref p.NodeArray[m_aTrieMap['.']];
                    if (q.NodeArray != null) // A '.' in use, we're not OK
                        return false;

                    return true;
                }

                // If this is a registered name, then let's make sure it's not the full name (.)
                if (!EqualityComparer<T>.Default.Equals(p.DataClass, default))
                    if (asciiName[i - 1] == '.')
                        return false;
            }

            return true;
        }


        /// <summary>
        /// Searches for sName in the Trie. 
        /// TODO: Make a variant that returns the best match and prepended string.
        /// </summary>
        /// <param name="asciiName"></param>
        /// <returns>Returns default(T) if none or the data key if found.</returns>
        public T LookupExactName(string asciiName)
        {
            _rwLock.EnterReadLock();

            try
            {
                ref var p = ref m_NodeRoot;

                int c;
                for (var i = asciiName.Length - 1; i >= 0; i--)
                {
                    c = m_aTrieMap[asciiName[i]]; // Map (and ignore case)

                    if (c == 128) // Illegal character
                        throw new ArgumentException("Illegal character in " + asciiName + " " + c.ToString());

                    if (p.NodeArray == null)
                        return default;

                    p = ref p.NodeArray[c];

                    if (i == 0)
                        return p.DataClass;
                    // We could also return the remainder of sName (prefix)
                }

                return default; // Not found
            }
            finally
            {
                _rwLock.ExitReadLock(); 
            }
        }

        /// <summary>
        /// Searches for sName in the Trie. 
        /// TODO: Make a variant that returns the best match and prepended string.
        /// </summary>
        /// <param name="asciiName"></param>
        /// <returns>Returns default(T) if none or the data key if found.</returns>
        public (T, string) LookupName(string asciiName)
        {
            _rwLock.EnterReadLock();
            try
            {
                ref var p = ref m_NodeRoot;

                int c;
                for (var i = asciiName.Length - 1; i >= 0; i--)
                {
                    c = m_aTrieMap[asciiName[i]]; // Map (and ignore case)

                    if (c == 128) // Illegal character
                        throw new ArgumentException("Illegal character in " + asciiName + " " + c.ToString());

                    if (p.NodeArray == null)
                        return (default, "");

                    p = ref p.NodeArray[c]; // ref is more efficient

                    if (typeof(T) == typeof(Guid) ? (Guid)(object)p.DataClass != Guid.Empty : p.DataClass != null)
                    {
                        // We found a hit, now we either need to be at the end or the next character a period
                        if (i == 0)
                            return (p.DataClass, "");

                        // i is greater than zero
                        if (asciiName[i - 1] == '.')
                        {
                            // Make sure there are no illegal characters in the prefix
                            for (int j = 0; j < i - 1; j++)
                                if (m_aTrieMap[asciiName[i]] == 128)
                                    throw new Exception();

                            // Build the prefix substring without including the period
                            string prefix = asciiName.Substring(0, i - 1).ToLower();
                            return (p.DataClass, prefix);
                        }
                    }
                }

                return (default, ""); // Not found
            }
            finally
            { 
                _rwLock.ExitReadLock(); 
            }
        }


        // This should probably be private (but then I can't unit test)
        // Adds the string sName (reversed) to the Trie with the DB key nKey
        // Returns false if error, true if OK. Will not detect duplicate use of DB
        // key.    
        internal void AddName(string asciiName, T Key)
        {
            if (asciiName.Length < 1)
                throw new ArgumentException("Domain name too short", nameof(asciiName));

            if (EqualityComparer<T>.Default.Equals(Key, default))
                throw new ArgumentException("Empty guid key not allowed", nameof(Key));

            // Add the domain name to the Trie - backwards (important)
            //

            ref var p = ref m_NodeRoot;

            Debug.Assert(m_NodeRoot.NodeArray != null, "NodeRoot doesn't have array");
            Debug.Assert(p.NodeArray != null, "Reference p to NodeRoot doesn't have array");

            int c;
            for (var i = asciiName.Length - 1; i >= 0; i--)
            {
                c = m_aTrieMap[asciiName[i]]; // Map and ignore case

                if (c == 128) // Illegal character
                    throw new ArgumentException($"Domain name {asciiName} contains illegal character", nameof(asciiName));

                if (p.NodeArray == null)
                    CreateArray(ref p);

                p = ref p.NodeArray[c];

                if (i == 0)
                {
                    if (!EqualityComparer<T>.Default.Equals(p.DataClass, default))
                        throw new ArgumentException($"Duplicate name {asciiName} inserted", nameof(asciiName));

                    p.DataClass = Key;
                    // Finished
                }
            }
        }

        public bool IsDomainUniqueInHierarchy(string asciiName)
        {
            _rwLock.EnterWriteLock();
            try
            {
                return InternalIsDomainUniqueInHierarchy(asciiName);
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public void AddDomain(string asciiName, T Key)
        {
            _rwLock.EnterWriteLock();
            try
            {
                if (InternalIsDomainUniqueInHierarchy(asciiName) == false)
                {
                    throw new ArgumentException($"Domain hierarchy not unique for {asciiName}", nameof(asciiName));
                }
                AddName(asciiName, Key);
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        // First attempt at removing name from Trie
        private void RemoveName(string asciiName)
        {
            if (asciiName.Length < 1)
                throw new ArgumentException($"Domain name too short {asciiName}", nameof(asciiName));

            // Remove the domain name to the Trie - backwards (important)
            //

            ref var p = ref m_NodeRoot;

            Debug.Assert(m_NodeRoot.NodeArray != null, "NodeRoot doesn't have array");
            Debug.Assert(p.NodeArray != null, "Reference p to NodeRoot doesn't have array");

            int c;
            for (var i = asciiName.Length - 1; i >= 0; i--)
            {
                c = m_aTrieMap[asciiName[i]]; // Map (and ignore case)

                if (c == 128) // Illegal character
                {
                    Console.WriteLine("Illegal character in " + asciiName + " " + c.ToString());
                    continue;
                }

                if (p.NodeArray == null)
                    throw new Exception("No such name to remove in Trie");

                p = ref p.NodeArray[c];

                if (i == 0)
                {
                    if (EqualityComparer<T>.Default.Equals(default, p.DataClass))
                        throw new Exception("Key not matching for name to remove");

                    // Ok we are ready to remove this.
                    p.DataClass = default;
                    return;
                }
                // We could also return the remainder of sName (prefix)
            }

            throw new Exception("No such name to remove.");
        }

        public void RemoveDomain(string asciiName)
        {
            if (!TryRemoveDomain(asciiName))
            {
                throw new Exception("Trying to remove a domain which is not found in the Trie");
            }
        }

        public bool TryRemoveDomain(string asciiName)
        {
            _rwLock.EnterWriteLock();
            try
            {
                if (InternalIsDomainUniqueInHierarchy(asciiName))
                {
                    return false;
                }
                RemoveName(asciiName);
                return true;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

   }
}