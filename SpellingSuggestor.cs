using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;


namespace System.Collections.Specialized
{
    /// <summary>
    ///     Returns suggested spellings that match value within a given true Damereau-Levenshtein distance in O(1).
    /// </summary>
    public sealed class SpellingSuggestor {
        public const int DEFAULT_DELETE_DISTANCE = 2;

        // this class could be sped up by removing the Node binarysearch, which isnt used by Lookup(), and would speedup inserts too
        // it would however slow down Items, Contains(), Remove()

        public readonly int DeleteDistance;
        // consider dict<int stablehash, Node> for faster access and storage
        private readonly Dictionary<string, Node> m_dict = new Dictionary<string, Node>();

        #region constructors
        public SpellingSuggestor(int delete_distance = DEFAULT_DELETE_DISTANCE) {
            this.DeleteDistance = delete_distance;
        }
        #endregion

        #region Items
        /// <summary>
        ///     O(n) + number of permutations not matching any item.
        /// </summary>
        public IEnumerable<string> Items {
            get {
                foreach(var kvp in m_dict) {
                    var len = kvp.Key.Length;
                    foreach(var item in kvp.Value) {
                        if(item.Length != len)
                            break;
                        yield return item;
                    }
                }
            }
        }
        #endregion

        #region Add()
        /// <summary>
        ///     Average: O(1) search + O(log c) binary search    (c=permutations sharing value)
        /// </summary>
        public void Add(string value) {
            var hash = new HashSet<string>();
            this.Add(value, hash);
        }
        /// <summary>
        ///     Average: O(1) search + O(log c) binary search    (c=permutations sharing value)
        /// </summary>
        private void Add(string value, HashSet<string> hash) {
            CreateDeletePermutations(value, this.DeleteDistance, hash);
            foreach(var permutation in hash) {
                if(!m_dict.TryGetValue(permutation, out var node)) {
                    node = new Node();
                    m_dict.Add(permutation, node);
                }
                node.Add(value);
            }
        }
        #endregion
        #region AddRange()
        public void AddRange(IEnumerable<string> items) {
            var hash = new HashSet<string>();

            foreach(var item in items)
                this.Add(item, hash);
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     Average: O(1) search + O(log c) binary search    (c=permutations sharing value)
        /// </summary>
        public bool Remove(string value) {
            var hash = new HashSet<string>();
            return this.Remove(value, hash);
        }
        /// <summary>
        ///     Average: O(1) search + O(log c) binary search    (c=permutations sharing value)
        /// </summary>
        private bool Remove(string value, HashSet<string> hash) {
            // if at least one part was found, then its a valid delete as it had been added previously, 
            // even though some entries are missing if not all were found
            bool found = false;
            CreateDeletePermutations(value, this.DeleteDistance, hash);
            foreach(var permutation in hash) {
                if(m_dict.TryGetValue(permutation, out var node)) {
                    if(node.Remove(value))
                        found = true;
                }
            }
            return found;
        }
        #endregion
        #region RemoveRange()
        public void RemoveRange(IEnumerable<string> items) {
            var hash = new HashSet<string>();

            foreach(var item in items)
                this.Remove(item, hash);
        }
        #endregion
        #region Contains()
        /// <summary>
        ///     Average: O(1) search + O(log c) binary search    (c=permutations sharing value)
        /// </summary>
        public bool Contains(string value) {
            if(!m_dict.TryGetValue(value, out var node))
                return false;
                
            return node.Contains(value);
        }
        #endregion

        #region Lookup()
        public enum Verbosity {
            Top,
            /// <summary>
            ///     All results having the best delete distance.
            /// </summary>
            Closest,
            All
        }
        /// <summary>
        ///     Average: O(1) * number_of_permutation
        ///     Returns suggested spellings that match value within a given true Damereau-Levenshtein distance.
        /// </summary>
        /// <param name="delete_distance">Default: -1. -1 returns everything.</param>
        public List<Result> Lookup(string value, int delete_distance = -1, Verbosity verbosity = Verbosity.Top) {
            if(delete_distance < 0)
                delete_distance = this.DeleteDistance;
            else if(delete_distance > this.DeleteDistance)
                throw new ArgumentOutOfRangeException($"{nameof(delete_distance)} ({delete_distance}) cannot be > {nameof(this.DeleteDistance)} ({this.DeleteDistance}).", nameof(delete_distance));

            var permutation_hash = new HashSet<string>();
            var visited_results  = new HashSet<string>();
            var results          = new List<Result>();
            int best_distance    = int.MaxValue; // applies only when verbosity==Closest

            CreateDeletePermutations(value, delete_distance, permutation_hash);
            foreach(var permutation in permutation_hash) {
                if(!m_dict.TryGetValue(permutation, out var node))
                    continue;

                foreach(var originalString in node) {
                    if(!visited_results.Add(originalString))
                        continue;

                    int cost = FuzzyStringMatch.LevenshteinDistance(value, originalString, 1, 1, 1, 1, delete_distance + 1);
                    if(cost <= delete_distance) {
                        // if exact match, break loop
                        if(cost == 0 && verbosity == Verbosity.Top) {
                            results.Clear();
                            results.Add(new Result(originalString, cost));
                            return results;
                        }

                        // keep only results within the best delete distance found so far
                        if(verbosity == Verbosity.Closest) {
                            if(cost > best_distance)
                                continue;
                            else if(cost < best_distance) {
                                results.Clear();
                                best_distance = cost;
                            }
                        }

                        results.Add(new Result(originalString, cost));
                    }
                }
            }

            results.Sort();

            return results;
        }
        #endregion
        #region TrimExcess()
        /// <summary>
        ///     O(n)
        /// </summary>
        public void TrimExcess() {
            foreach(var item in m_dict.Values)
                item.TrimExcess();
        }
        #endregion

        #region private static CreateDeletePermutations()
        /// <summary>
        ///     Returns all the character delete permutations.
        /// </summary>
        /// <param name="delete_distance">The max number of characters to remove</param>
        private static void CreateDeletePermutations(string source, int delete_distance, HashSet<string> result) {
            result.Clear();

            foreach(var permutation in CreateDeletePermutations(source, delete_distance))
                result.Add(new string(permutation));
        }
        /// <summary>
        ///     Returns all the character delete permutations.
        ///     This will return the same instance for performance reasons, so copy it if need be.
        /// </summary>
        /// <param name="delete_distance">The max number of characters to remove</param>
        private static IEnumerable<char[]> CreateDeletePermutations(string source, int delete_distance) {
            if(delete_distance < 0 || string.IsNullOrEmpty(source))
                yield break;

            int len = source.Length;
            var s   = source.ToCharArray();

            // 0 permutation / exact match
            yield return s;
            if(delete_distance == 0 || len == 1)
                yield break;


            // 1 permutation
            var res_1perm = new char[len - 1];
            Array.Copy(s, 0, res_1perm, 0, len - 1);
            yield return res_1perm;
            for(int d = len - 2; d >= 0; d--) {
                Array.Copy(s, d + 1, res_1perm, d, len - d - 1);
                yield return res_1perm;
            }
            if(delete_distance == 1 || len == 2)
                yield break;
            

            // n permutations
            var skips = new int[delete_distance];

            for(int d = 2; d <= delete_distance; d++) {
                if(len - d <= 0)
                    yield break;

                for(int i = 0; i < d; i++)
                    skips[i] = len - (d - i);
                skips[d - 1]++; // put 1 too high on last entry to simplify code below

                var res = new char[len - d];
                Array.Copy(s, 0, res, 0, res.Length);

                while(skips[d - 1] >= d) {
                    for(int i = d - 1; i >= 0; i--) {
                        if(i == 0 || skips[i] > skips[i - 1] + 1) {
                            skips[i]--;
                            for(int j = i + 1; j < d; j++)
                                skips[j] = len - d + j;

                            int writeIndex = 0;
                            int readIndex  = 0;
                            for(int j = 0; j < d; j++) { // j=1 ?   j = i+1? for speedup
                                var size = skips[j] - readIndex;
                                if(size > 0)
                                    Array.Copy(s, readIndex, res, writeIndex, size);
                                writeIndex += size;
                                readIndex  += size + 1;
                            }
                            var size2 = len - readIndex;
                            if(size2 > 0)
                                Array.Copy(s, readIndex, res, writeIndex, size2);
                            yield return res;
                            break;
                        }
                    }
                }
            }
        }
        #endregion

        #region private class Node
        /// <summary>
        ///     Since this can be either really small or really big, and that we may have many in memory, 
        ///     this adapts to the current amount of item.
        /// </summary>
        private sealed class Node : IEnumerable<string> {
            private const int SWITCH_TO_BTREE_THRESHOLD = 256;

            private ArrayNode m_arrayNode = new ArrayNode();   // ordered by "Length(distance),OriginalString"
            private BTree<string> m_btree = null;              // ordered by "Length(distance),OriginalString"

            /// <summary>
            ///     O(log n)
            /// </summary>
            public void Add(string originalString) {
                if(m_arrayNode != null) {
                    if(m_arrayNode.Count < SWITCH_TO_BTREE_THRESHOLD) {
                        m_arrayNode.Add(originalString);
                        return;
                    } else {
                        var arrayNode = m_arrayNode;
                        m_arrayNode   = null;
                        m_btree       = new BTree<string>(new ItemComparer(), SWITCH_TO_BTREE_THRESHOLD);
                        
                        m_btree.AddRangeOrdered(arrayNode);
                    }
                }

                m_btree.Add(originalString);
            }
            /// <summary>
            ///     O(log n)
            /// </summary>
            public bool Remove(string originalString) {
                if(m_arrayNode != null)
                    return m_arrayNode.Remove(originalString);
                else
                    return m_btree.Remove(originalString);
            }
            /// <summary>
            ///     O(log n)
            /// </summary>
            public bool Contains(string originalString) {
                if(m_arrayNode != null)
                    return m_arrayNode.Contains(originalString);
                else
                    return m_btree.ContainsKey(originalString);
            }
            public void TrimExcess() {
                if(m_arrayNode != null)
                    m_arrayNode.TrimExcess();
                else
                    m_btree.Optimize();
            }
            
            public IEnumerator<string> GetEnumerator() {
                if(m_arrayNode != null)
                    return m_arrayNode.GetEnumerator();
                else
                    return m_btree.Keys.GetEnumerator();
            }
            IEnumerator IEnumerable.GetEnumerator() {
                return this.GetEnumerator();
            }
            public override string ToString() {
                return (m_arrayNode?.Count ?? m_btree.Count).ToString();
            }
            private sealed class ItemComparer : IComparer<string> {
                public int Compare(string x, string y) {
                    var diff = x.Length - y.Length;
                    if(diff == 0)
                        diff = string.CompareOrdinal(x, y);
                    return diff;
                }
            }
        }
        private sealed class ArrayNode : IEnumerable<string> {
            private string[] Permutations = new string[4]; // ordered by "Length(distance),OriginalString"
            public int Count;

            /// <summary>
            ///     O(n/2 + log n)
            ///     
            ///     ie:
            ///     O(log n) binary search 
            ///     O(n/2)   insert
            /// </summary>
            public void Add(string originalString) {
                if(this.Permutations.Length == this.Count)
                    Array.Resize(ref this.Permutations, this.Count * 2);
                var index = this.BinarySearch(originalString);
                if(index >= 0)
                    throw new ArgumentException($"Duplicate key '{originalString}'.", nameof(originalString));
                index = ~index;
                Array.Copy(this.Permutations, index, this.Permutations, index + 1, this.Count - index);
                this.Permutations[index] = originalString;
                this.Count++;
            }
            /// <summary>
            ///     O(n/2 + log n)
            ///     
            ///     ie:
            ///     O(log n) binary search 
            ///     O(n/2)   remove
            /// </summary>
            public bool Remove(string originalString) {
                var index = this.BinarySearch(originalString);
                if(index < 0)
                    return false;
                Array.Copy(this.Permutations, index + 1, this.Permutations, index, this.Count - index - 1);
                this.Count--;
                return true;
            }
            /// <summary>
            ///     O(log n)
            /// </summary>
            public bool Contains(string originalString) {
                return this.BinarySearch(originalString) >= 0;
            }
            public void TrimExcess() {
                // already checks for length==count
                Array.Resize(ref this.Permutations, this.Count);
            }
            private int BinarySearch(string originalString) {
                int min = 0;
                int max = this.Count - 1;

                while(min <= max) {
                    int median = (min + max) >> 1;
                    var diff   = this.Permutations[median].Length - originalString.Length;

                    if(diff == 0)
                        diff = string.CompareOrdinal(this.Permutations[median], originalString);

                    if(diff < 0)
                        min = median + 1;
                    else if(diff > 0)
                        max = median - 1;
                    else
                        return median;
                }

                return ~min;
            }
            
            public IEnumerator<string> GetEnumerator() {
                int max = this.Count;
                for(int i = 0; i < max; i++)
                    yield return this.Permutations[i];
            }
            IEnumerator IEnumerable.GetEnumerator() {
                return this.GetEnumerator();
            }
            public override string ToString() {
                return this.Count.ToString();
            }
        }
        #endregion
        #region public class Result
        public sealed class Result : IComparable<Result> {
            public string Value;
            /// <summary>
            ///     The true Damereau-Lavenshtein distance of this.Value vs input.
            /// </summary>
            public int LevenshteinDistance;

            public double Similarity;

            public Result(string value, int levenshtein_distance, double similarity = 0) {
                this.Value               = value;
                this.LevenshteinDistance = levenshtein_distance;
                this.Similarity          = similarity;
            }

            public int CompareTo(Result other) {
                if(other == null)
                    return 1;
                    
                var diff = this.Similarity - other.Similarity;
                if(diff != 0)
                    return diff < 0 ? 1 : -1; // inverse because sort by desc

                var diff2 = this.LevenshteinDistance - other.LevenshteinDistance;
                if(diff2 != 0)
                    return diff2;
                return string.CompareOrdinal(this.Value, other.Value);
            }
        }
        #endregion
    }
}
