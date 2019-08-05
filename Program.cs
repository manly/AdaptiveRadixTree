using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Specialized;

namespace AdaptiveRadixTree {
    class Program {
        public static IEnumerable<T[]> GenerateCombinations<T>(IEnumerable<T[]> source) {
            bool found;
            T[][] values = source.ToArray();
            int[] counts = new int[values.Length];
            T[] path = new T[values.Length];
            int[] pathIndex = new int[counts.Length];
            int max = values.Length - 1;

            var count = values.Length;
            for(int i = 0; i < count; i++) {
                var v = values[i];
                path[i] = v[0];
                counts[i] = v.Length - 1;
            }

            yield return path;

            do {
                found = false;
                int dimension = max;
                while(dimension >= 0) {
                    if(pathIndex[dimension] < counts[dimension]) {
                        var index = pathIndex[dimension] + 1;
                        pathIndex[dimension] = index;
                        path[dimension] = values[dimension][index];
                        while(dimension < max) {
                            pathIndex[++dimension] = 0;
                            path[dimension] = values[dimension][0];
                        }
                        yield return path;
                        found = true;
                        break;
                    } else
                        dimension--;
                }
            } while(found);
        }



        static void Main(string[] args) {
            var dist22 = new SpellingSuggestor(3);
            dist22.AddRange("manly,abcde,hamburger,apple".Split(','));
            var fufuufuff = dist22.Lookup("applr").ToList();

            var art2 = new AdaptiveRadixTree<string, string>();
            art2.Add("dfgdfg", "213123");

            var dist22333 = FuzzyStringMatch.CombinedWordEditDistance("H7N0K3", "H7N0K3");

            var art = new AdaptiveRadixTree<string, int>();
            var dict = new Dictionary<string, int>();
            int aa = 0;

            var items = AdaptiveRadixTreeTest.GenerateTestKeys(100000).ToArray();


            foreach(var item in items) {
                art.Add(in item, aa);
                dict.Add(item, aa);
                aa++;
            }
            System.Diagnostics.Debug.Write(art.CalculateMetrics());
            //// System.Diagnostics.Debug.Write(art.DebugDump(true));
            //
            var now = DateTime.UtcNow;
            int readResults = 0;
            // todo: read/write not using stream
            for(int i=0; i<items.Length; i++){
                var item = items[i];
                //readResults += art.PartialMatch(item.Substring(0, item.Length - 1) + ".", '.').Count();
                //readResults += art.PartialMatch("." + item.Substring(1), '.').Count();
                //readResults += art.RegExpMatch("[A-B-D]" + item.Substring(1)).Count();
                //readResults += art.StartsWithKeys(item.Substring(0, item.Length - 1)).Count();
                //readResults += art.RegExpNearNeighbors("[A-B-D]" + item.Substring(1), 0).Count();
            
                // remove()
                // fix path filter enumerator to call the filter method in-order
                
                // try with just one entry
            
                //var sdfsdf = art.RegExpNearNeighbors("BBBI", 2).ToList();
            
            
                if(i==99999)
                    "".ToLower();
                if(!art.Remove(item)) // fuck at i=163 with offbranch code
                    "".ToLower();
                //if(i > 145 && art.DebugDump(true).Contains("EXCEPTION"))
                //    "".ToString();
            }
            
            //var dict4 = new Dictionary<int, string>(100000);
            //int coll = 0;
            //for(int i = 0; i < 100000; i++) {
            //    //FuzzyStringMatch.GetStableHashCode(items[i % 100000]);
            //    //items[i % 100000].GetHashCode();
            //                    if((i %100000) == 0)
            //        dict4.Clear();
            //    try {
            //        dict4.Add(FuzzyStringMatch.GetStableHashCode(items[i % 100000]), items[i % 100000]);
            //    } catch { 
            //        coll++;
            //        }
            //}

            var diff = DateTime.UtcNow - now;
            System.Console.WriteLine(diff.ToString());
            //System.Console.WriteLine(readResults);
            System.Console.ReadLine();
        }
        private class stringcom : IEqualityComparer<string> {
            public bool Equals(string x, string y) {
                return x == y;
            }
            public int GetHashCode(string obj) {
                return FuzzyStringMatch.GetStableHashCode(obj);
            }
        }
    }
}
