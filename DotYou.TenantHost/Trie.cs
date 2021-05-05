using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DotYou.TenantHost
{
    public class Trie<T> // where T : class
    {
        // Maps ASCII character to Trie[] DNS node index, 128 is illegal
        private byte[] m_aTrieMap =
        {128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 0-9
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 10-19
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 20-29
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 30-39
    128, 128, 128, 128, 128,  26,  27, 128,  28,  29,  // 40-49 '-' 45, '.' 46, '0' 48, '1' 49
     30,  31,  32,  33,  34,  35,  36,  37, 128, 128,  // 50-59 ..'9' is 57
    128, 128, 128, 128, 128,   0,   1,   2,   3,   4,  // 60-69 'A' is 65
      5,   6,   7,   8,   9,  10,  11,  12,  13,  14,  // 70-79
     15,  16,  17,  18,  19,  20,  21,  22,  23,  24,  // 80-89
     25, 128, 128, 128, 128, 128, 128,   0,   1,   2,  // 90-99 'a' is 97
      3,   4,   5,   6,   7,   8,   9,  10,  11,  12,  // 100-109
     13,  14,  15,  16,  17,  18,  19,  20,  21,  22,  // 110-119
     23,  24,  25, 128, 128, 128, 128, 128, 128, 128,  // 120-129 
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 130-139
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 140-149
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 150-159
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 160-169
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 170-179
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 180-189
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 190-199
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 200-209 
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 210-219
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 220-229
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 230-239
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 240-249
    128, 128, 128, 128, 128, 128};                     // 250-255

        private Mutex TrieMutex = new Mutex();
        private struct NodeData
        {
            public NodeData[] NodeArray;  // either null, or points to an array of 128 Nodes
            public T DataClass;           // Either null if this is not a stop node, or a class that holds data
        }

        private NodeData m_NodeRoot = new NodeData();   // The Trie root

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
        /// <param name="sName">domain name to check</param>
        /// <returns></returns>
        // This should probably be private (but then I can't unit test)
        public bool IsDomainUniqueInHierarchy(string sName)
        {
            DomainName.ValidateDomain(sName); // Throws an exception if not OK

            ref NodeData p = ref m_NodeRoot;

            int c;
            for (int i = sName.Length - 1; i >= 0; i--)
            {
                c = m_aTrieMap[sName[i] & 127]; // Map (and ignore case)

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
                    if (!EqualityComparer<T>.Default.Equals(p.DataClass, default(T)))
                        return false;

                    ref NodeData q = ref p.NodeArray[this.m_aTrieMap['.' & 127]];
                    if (q.NodeArray != null) // A '.' in use
                        return false;

                    return true;
                }

                // If this is a registered name, then let's make sure it's not the full name (.)
                if (!EqualityComparer<T>.Default.Equals(p.DataClass, default(T)))
                {
                    if (sName[i - 1] == '.')
                        return false;
                }
            }

            return true;
        }



        /// <summary>
        /// Searches for sName in the Trie. 
        /// TODO: Make a variant that returns the best match and prepended string.
        /// </summary>
        /// <param name="sName"></param>
        /// <returns>Returns default(T) if none or the data key if found.</returns>
        public T LookupName(string sName)
        {
            ref NodeData p = ref m_NodeRoot;

            int c;
            for (int i = sName.Length - 1; i >= 0; i--)
            {
                c = m_aTrieMap[sName[i] & 127]; // Map (and ignore case)

                if (c == 128) // Illegal character
                {
                    Console.WriteLine("Illegal character in " + sName + " " + c.ToString());
                    continue;
                }

                if (p.NodeArray == null)
                    return default(T);

                p = ref p.NodeArray[c];

                if (i == 0)
                    return p.DataClass;
                // We could also return the remainder of sName (prefix)
            }

            return default(T); // Not found
        }


        // This should probably be private (but then I can't unit test)
        // Adds the string sName (reversed) to the Trie with the DB key nKey
        // Returns false if error, true if OK. Will not detect duplicate use of DB
        // key.    
        private void AddName(string sName, T Key)
        {
            if (sName.Length < 1)
                throw new DomainTooShort();

            if (EqualityComparer<T>.Default.Equals(Key, default(T)))
                throw new EmptyKeyNotAllowed();

            // Add the domain name to the Trie - backwards (important)
            //

            ref NodeData p = ref m_NodeRoot;

            Debug.Assert(m_NodeRoot.NodeArray != null, "NodeRoot doesn't have array");
            Debug.Assert(p.NodeArray != null, "Reference p to NodeRoot doesn't have array");

            int c;
            for (int i = sName.Length - 1; i >= 0; i--)
            {
                c = m_aTrieMap[sName[i] & 127]; // Map and ignore case

                if (c == 128) // Illegal character
                    throw new DomainIllegalCharacter();

                if (p.NodeArray == null)
                    CreateArray(ref p);

                p = ref p.NodeArray[c];

                if (i == 0)
                {
                    if (!EqualityComparer<T>.Default.Equals(p.DataClass, default(T)))
                        throw new DuplicateDomainNameInsertedException();

                    p.DataClass = Key;
                    // Finished
                }

            }
        }


        public void AddDomain(string sName, T Key)
        {
            TrieMutex.WaitOne();

            if (IsDomainUniqueInHierarchy(sName) == false)
            {
                TrieMutex.ReleaseMutex();
                throw new DomainHierarchyNotUnique();
            }

            try
            {
                AddName(sName, Key);
            }
            catch (Exception e)
            {
                throw (e);
            }
            finally
            {
                TrieMutex.ReleaseMutex();
            }
        }


        // Tests
        public void _PerformanceTest()
        {
            var t = new Trie<Guid>();
            Guid g;
            Random randObj = new Random();
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            string s;

            for (int i = 0; i < 1000000; i++)
            {
                s = "";

                // Generate random string
                for (int j = 0; j < 8; j++)
                {
                    // Generate floating point numbers
                    double myFloat = randObj.NextDouble();
                    var myChar = Convert.ToChar(Convert.ToInt32(Math.Floor(25 * myFloat) + 65));
                    s += myChar;
                }
                s += ".com";
                t.AddName(s, Guid.NewGuid());
            }
            stopWatch.Stop();
            TimeSpan ts = stopWatch.Elapsed;

            // Format and display the TimeSpan value.
            string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
               ts.Hours, ts.Minutes, ts.Seconds,
               ts.Milliseconds / 10);
            Console.WriteLine("Time to boot 1,000,000 DB " + elapsedTime);

            stopWatch.Start();

            UInt32 k = 0;
            for (int i = 0; i < 100000000; i++)
            {
                if (t.LookupName("abcdefgh.com") != Guid.Empty)
                    k++;
                if (t.LookupName("michael.corleone.com") != Guid.Empty)
                    k++;
                if (t.LookupName("ymer.com") != Guid.Empty)
                    k++;
            }
            stopWatch.Stop();
            ts = stopWatch.Elapsed;

            // Format and display the TimeSpan value.
            elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
               ts.Hours, ts.Minutes, ts.Seconds,
               ts.Milliseconds / 10);
            Console.WriteLine("Time to lookup 15,000,000 trie entries " + elapsedTime);

            int cnt = ((3 * 100000000) / (ts.Seconds * 1000 + ts.Milliseconds)) * 1000;
            Console.WriteLine("Lookups per second " + cnt.ToString());
        }
    }
}