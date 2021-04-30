using System;
using System.Diagnostics;

public class Trie
{
    private byte[] m_aTrieMap =
    {128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 0-9
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 10-19
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 20-29
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 30-39
    128, 128, 128, 128, 128,  26, 27, 128, 128, 128,  // 40-49 hyphen 45, period 26
    128, 128, 128, 128, 128, 128, 128, 128, 128, 128,  // 50-59
    128, 128, 128, 128, 128,   0,   1,   2,   3,   4,  // 60-69 A is 65
      5,   6,   7,   8,   9,  10,  11,  12,  13,  14,  // 70-79
     15,  16,  17,  18,  19,  20,  21,  22,  23,  24,  // 80-89
     25, 128, 128, 128, 128, 128, 128,   0,   1,   2,  // 90-99 a is 97
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


    struct NodeData
    {
        public NodeData[] m_aNodeArray;  // either null, or points to an array of 128 Nodes
        public Guid m_gIdentityKey;      // Guid.Empty means no key, guid value otherwise
    }

    NodeData m_NodeRoot = new NodeData();   // The Trie root

    private void CreateArray(ref NodeData n)
    {
        // Only valid chars are 0-9, a-z, -, .
        // That's 28 entries using the table above
        n.m_aNodeArray = new NodeData[28];
    }

    public Trie()
    {
        // Initialize the root node

        // Are all values in C# 0 (and null) on creation?
        CreateArray(ref m_NodeRoot);
    }

    // Searches for sName in the Trie. Returns 0 if none
    // or the DB key if found
    public Guid lookupName(string sName)
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

            if (p.m_aNodeArray == null)
                return Guid.Empty;

            p = ref p.m_aNodeArray[c];

            if (i == 0)
                return p.m_gIdentityKey;
            // We could also return the remainder of sName (prefix)
        }

        return Guid.Empty; // Not found
    }

    // Adds the string sName (reversed) to the Trie with the DB key nKey
    // Returns false if error, true if OK. Will not detect duplicate use of DB
    // key.
    // We'll need thread safe mutex { check if name exists, then insert new name } end mutex
    public bool addName(string sName, Guid gKey)
    {
        if (sName.Length < 1)
        {
            Console.WriteLine("Cannot add empty name");
            return false;
        }

        if (sName.Length > 255)
        {
            Console.WriteLine("sName too long to add to Trie: " + sName);
            return false;
        }

        Debug.Assert(gKey != Guid.Empty, "gKey cannot be empty");


        // Add the domain name to the Trie - backwards (important)
        //
        //Console.WriteLine("Adding to Trie: " + sName);

        ref NodeData p = ref m_NodeRoot;

        Debug.Assert(m_NodeRoot.m_aNodeArray != null, "NodeRoot doesn't have array");
        Debug.Assert(p.m_aNodeArray != null, "Reference p to NodeRoot doesn't have array");

        int c;
        for (int i = sName.Length - 1; i >= 0; i--)
        {
            c = m_aTrieMap[sName[i] & 127]; // Map and ignore case

            if (c == 128) // Illegal character
            {
                Console.WriteLine("Illegal character in " + sName + " " + c.ToString());
                continue;
            }

            //Console.WriteLine("Adding letter " + c);

            if (p.m_aNodeArray == null)
            {
                //Console.WriteLine("Creating Array  = " + i.ToString());
                CreateArray(ref p);
            }

            p = ref p.m_aNodeArray[c];

            if (i == 0)
            {
                if (p.m_gIdentityKey != Guid.Empty)
                {
                    Console.WriteLine("Duplicate name inserted:" + sName);
                    return false; // Failed
                }

                p.m_gIdentityKey = gKey;
            }

        }

        return true; // OK
    }

    // Tests
    public void _Test()
    {
        bool b;

        Console.WriteLine("Running Trie Test");

        b = this.addName("", Guid.NewGuid());
        Debug.Assert(b == false, "Cannot add empty name");

        Guid g1 = Guid.NewGuid();
        Guid g2 = Guid.NewGuid();
        Guid g3 = Guid.NewGuid();
        Guid g4 = Guid.NewGuid();
        Guid g5 = Guid.NewGuid();

        b = this.addName("a", g1);
        Debug.Assert(b == true, "Adding a");
        b = this.addName("aa", g2);
        Debug.Assert(b == true, "Adding aa");
        b = this.addName("ab", g3);
        Debug.Assert(b == true, "Adding ab");
        b = this.addName("aaa", g4);
        Debug.Assert(b == true, "Adding aaa");
        b = this.addName("b", g5);
        Debug.Assert(b == true, "Adding b");

        Guid g;

        g = this.lookupName("a");
        Debug.Assert(g == g1, "Looking up a");
        g = this.lookupName("aa");
        Debug.Assert(g == g2, "Looking up aa");
        g = this.lookupName("ab");
        Debug.Assert(g == g3, "Looking up ab");
        g = this.lookupName("aaa");
        Debug.Assert(g == g4, "Looking up aaa");
        g = this.lookupName("b");
        Debug.Assert(g == g5, "Looking up b");

        g = this.lookupName("q");
        Debug.Assert(g == Guid.Empty, "Looking up q");

        g = this.lookupName("ymer");
        Debug.Assert(g == Guid.Empty, "Looking up ymer");

        Console.WriteLine("Finished Trie Test");

        // ======================

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
            this.addName(s, Guid.NewGuid());
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
            if (this.lookupName("abcdefgh.com") != Guid.Empty)
                k++;
            if (this.lookupName("michael.corleone.com") != Guid.Empty)
                k++;
            if (this.lookupName("ymer.com") != Guid.Empty)
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
