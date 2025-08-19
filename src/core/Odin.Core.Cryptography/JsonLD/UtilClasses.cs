/**
 * Parts of the source code in this file has been translated/ported from rdf-canonize library by Digital Bazaar (BSD 3-Clause license)
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace JsonLd.Normalization
{
    internal record IriRelativeTo
    {
        public static readonly IriRelativeTo BaseSet = new IriRelativeTo { Base = true };
        public static readonly IriRelativeTo VocabSet = new IriRelativeTo { Vocab = true };
        public static readonly IriRelativeTo BothSet = new IriRelativeTo { Base = true, Vocab = true };

        public bool Base = false;
        public bool Vocab = false;
    }

    internal class BlankNodeInfo
    {
        public HashSet<Quad> Quads { get; set; }
        public string Hash { get; set; }
    }

    internal class MessageDigest
    {
        StringBuilder sb = new StringBuilder();

        public void Update(string data)
        {
            sb.Append(data);
        }

        public string Digest()
        {
            using var sha256Hash = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            sb.Clear();
            var resultBytes = sha256Hash.ComputeHash(bytes);
            return Convert.ToHexString(resultBytes).ToLower();
        }
    }

    internal class Permuter
    {
        private List<string> current;
        private bool done;
        private Dictionary<string, bool> dir;

        public Permuter(List<string> list)
        {
            // original array
            current = list.OrderBy(s => s).ToList();
            // indicates whether there are more permutations
            done = false;
            // directional info for permutation algorithm
            dir = new();
            for (var i = 0; i < list.Count; ++i)
            {
                dir[list[i]] = true;
            }
        }

        public bool HasNext() => !done;

        public List<string> Next()
        {
            // copy current permutation to return it
            var rval = current.ToList();

            /* Calculate the next permutation using the Steinhaus-Johnson-Trotter
             permutation algorithm. */

            // get largest mobile element k
            // (mobile: element is greater than the one it is looking at)
            string k = null;
            var pos = 0;
            var length = current.Count;
            for (var i = 0; i < length; ++i)
            {
                var element = current[i];
                var left = dir[element];
                if ((k == null || string.Compare(element, k) > 0) &&
                    (left && i > 0 && string.Compare(element, current[i - 1]) > 0 ||
                    !left && i < length - 1 && string.Compare(element, current[i + 1]) > 0))
                {
                    k = element;
                    pos = i;
                }
            }

            // no more permutations
            if (k == null)
            {
                done = true;
            }
            else
            {
                // swap k and the element it is looking at
                var swap = dir[k] ? pos - 1 : pos + 1;
                current[pos] = current[swap];
                current[swap] = k;

                // reverse the direction of all elements larger than k
                foreach (var element in current)
                {
                    if (string.Compare(element, k) > 0)
                    {
                        dir[element] = !dir[element];
                    }
                }
            }

            return rval;
        }
    }
}