using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
 
 
namespace System.Collections.Specialized
{
    /// <summary>
    ///     Multi n-gram indexer, used typically for O(1) sub-string searches.
    ///     Provides indexed wildcard searching functionality.
    ///     ie: [abcd] => {abcd, abc, bcd, ab, bc, cd, a, b, c, d}
    /// </summary>
    /// <remarks>
    ///     About min/max n-gram length selection
    ///     -------------------------------------
    ///     Most crucial is min-ngram-length value. 
    ///     This value should be &lt;= the shortest consecutive stretch of specific characters.
    ///     For example, if you expect most searches to be 'ABC?123' or 'ABC*123', then you'd want min-ngram-length &lt;= 3.
    ///     If min-ngram-length is 4 in this example, then no index can be used.
    ///     
    ///     Max-ngram-length has no specific rule.
    ///     If you want pure lookup performance, then use int.MaxValue.
    ///     There is no gain to be had to use a value &gt; the longest consecutive stretch of specific characters to be searched for.
    ///     But typically the only gain (from a higher value) is the specificity of how rare a longer n-gram is, thus leading to faster filtering 
    ///     when searching for that specific value.
    /// </remarks>
    public sealed class MultiNGramIndex {
        private const char DEFAULT_WILDCARD_UNKNOWN  = '?';
        private const char DEFAULT_WILDCARD_ANYTHING = '*';
 
        private readonly Dictionary<string, List<NGram>> m_dict  = new Dictionary<string, List<NGram>>();
 
        // need the list of all items, since search may contain no consecutive sequence >= minngramlength, in which case we still need to perform a search
        // also need a list of all items without any index
        private readonly HashSet<string> m_items           = new HashSet<string>(); // all items >= minngramlength
        private readonly HashSet<string> m_itemsNotIndexed = new HashSet<string>(); // all items < minngramlength
 
        /// <summary>
        ///     Avoid too low values, as that will slow down matching.
        /// </summary>
        public readonly int MinNGramLength; // inclusive
        public readonly int MaxNGramLength; // inclusive
        /// <summary>?</summary>
        private readonly char WildcardUnknown;
        /// <summary>*</summary>
        private readonly char WildcardAnything;
        public int Count { get; private set; }
 
        #region constructors
        /// <param name="minNGramLength">Inclusive. Avoid too low values, as that will slow down matching.</param>
        /// <param name="maxNGramLength">Inclusive.</param>
        public MultiNGramIndex(int minNGramLength, int maxNGramLength, char wildcard_unknown_character = DEFAULT_WILDCARD_UNKNOWN, char wildcard_anything_character = DEFAULT_WILDCARD_ANYTHING) {
            if(minNGramLength < 2) // while 1 is valid, it would be a massive slowdown
                throw new ArgumentOutOfRangeException(nameof(minNGramLength));
            if(maxNGramLength < minNGramLength)
                throw new ArgumentOutOfRangeException(nameof(maxNGramLength));
 
            this.MinNGramLength   = minNGramLength;
            this.MaxNGramLength   = maxNGramLength;
            this.WildcardUnknown  = wildcard_unknown_character;
            this.WildcardAnything = wildcard_anything_character;
        }
        #endregion
 
        #region Items
        /// <summary>
        ///     O(n)
        ///     Returns all items, but not in insertion order.
        /// </summary>
        public IEnumerable<string> Items {
            get {
                foreach(var item in m_itemsNotIndexed)
                    yield return item;
                foreach(var item in m_items)
                    yield return item;
            }
        }
        #endregion
 
        #region Add()
        /// <summary>
        ///     Average: O(k)    k = value.Length
        ///     Returns true if success.
        ///     Returns false if value already exists.
        ///     
        ///     Throws ArgumentNullException on null value.
        ///     Throws ArgumentException on empty value.
        /// </summary>
        public bool Add(string value) {
            if(value == null)
                throw new ArgumentNullException(nameof(value));
            if(value.Length == 0)
                throw new ArgumentException(nameof(value));

            if(value.Length >= this.MinNGramLength) {
                if(!m_items.Add(value))
                    //throw new ArgumentException($"The value ({value}) already exists.", nameof(value));
                    return false;
 
                foreach(var ngram in this.GenerateNGram(value.Length)) {
                    var ngram_string = value.Substring(ngram.Index, ngram.Length);
 
                    if(!m_dict.TryGetValue(ngram_string, out var list)) {
                        list = new List<NGram>();
                        m_dict.Add(ngram_string, list);
                    }
 
                    list.Add(new NGram(value, ngram.Index));
                }
            } else {
                if(!m_itemsNotIndexed.Add(value))
                    //throw new ArgumentException($"The value ({value}) already exists.", nameof(value));
                    return false;
            }

            this.Count++;
            return true;
        }
        #endregion
        #region AddRange()
        public void AddRange(IEnumerable<string> values) {
            foreach(var item in values)
                this.Add(item);
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     Throws ArgumentNullException on null value.
        ///     Throws ArgumentException on empty value.
        /// </summary>
        public bool Remove(string value) {
            if(value == null)
                throw new ArgumentNullException(nameof(value));
            if(value.Length == 0)
                throw new ArgumentException(nameof(value));
 
            if(value.Length >= this.MinNGramLength) {
                if(!m_items.Remove(value))
                    return false;

                // avoid expensive string comparison
                string reference = null;

                using(var enumerator = this.GenerateNGram(value.Length).GetEnumerator()) {
                    if(!enumerator.MoveNext())
                        return false;
 
                    // try find the first item so as to get the actual reference to the original string
                    // this allows significant speedup in string comparison for all future compares
                    var ngram        = enumerator.Current;
                    var ngram_string = value.Substring(ngram.Index, ngram.Length);
 
                    if(!m_dict.TryGetValue(ngram_string, out var list))
                        return false;
 
                    int index  = 0;
                    bool found = false;
                    while(index < list.Count) {
                        var item = list[index];
                        if(item.Value != value)
                            index++;
                        else {
                            found = true;
                            reference = item.Value;
                            if(list.Count != 1)
                                list.RemoveAt(index);
                            else
                                m_dict.Remove(ngram_string);
                            break;
                        }
                    }
                    if(!found)
                        return false;
 
                    while(enumerator.MoveNext()) {
                        ngram        = enumerator.Current;
                        ngram_string = value.Substring(ngram.Index, ngram.Length);
 
                        if(!m_dict.TryGetValue(ngram_string, out list))
                            return false;
 
                        index = 0;
                        while(index < list.Count) {
                            var item = list[index];
                            // now that we have the original reference, all string comparisons can be avoided
                            // this is where most of the performance was lost
                            if(object.ReferenceEquals(item.Value, reference)) //if(item.Value != value)
                                index++;
                            else {
                                if(list.Count != 1)
                                    list.RemoveAt(index);
                                else
                                    m_dict.Remove(ngram_string);
                                break;
                            }
                        }
                    }
                }
            } else {
                if(!m_itemsNotIndexed.Remove(value))
                    return false;
            }
             
            this.Count--;
            return true;
        }
        #endregion
        #region RemoveRange()
        public void RemoveRange(IEnumerable<string> values) {
            foreach(var item in values)
                this.Remove(item);
        }
        #endregion
        #region Clear()
        /// <summary>
        ///     O(1)
        /// </summary>
        public void Clear() {
            m_dict.Clear();
            m_items.Clear();
            m_itemsNotIndexed.Clear();
            this.Count = 0;
        }
        #endregion
        #region Contains()
        /// <summary>
        ///     Returns true if value was previously added.
        ///     
        ///     Throws ArgumentNullException on null value.
        ///     Throws ArgumentException on empty value.
        /// </summary>
        public bool Contains(string value) {
            if(value == null)
                throw new ArgumentNullException(nameof(value));
            if(value.Length == 0)
                throw new ArgumentException(nameof(value));
 
            if(value.Length >= this.MinNGramLength)
                return m_items.Contains(value);
            else
                return m_itemsNotIndexed.Contains(value);
        }
        #endregion
        #region TrimExcess()
        /// <summary>
        ///     Removes the excess storage used by lists.
        /// </summary>
        public void TrimExcess() {
            foreach(var item in m_dict.Values)
                item.TrimExcess();
        }
        #endregion
 
        #region Search()
        /// <summary>
        ///     Searches for the given format which may include wildcard characters.
        /// </summary>
        /// <param name="format_including_wildcards">The user search format, which may include wildcard characters.</param>
        public IEnumerable<string> Search(string format_including_wildcards, SearchOption match = SearchOption.ExactMatch) {
            // algorithm explanation:
            //
            // tldr; look at SearchSection(), repeat the same concept but across sections() matches
            //
            // format='*123***456?78?9*'     sections = { "123", "456?78?9" }
            // 
            // basically we search for sections {"123", "456?78?9"} and then do an intersection of results, 
            // since all of them need to be found
            //
            // Those searches do not focus on positioning being in-order, so we also need to manually verify at the end after intersecting
            // otherwise '45667889  123' would match all 2 sections, but wouldnt make sense with the search format
            //
            // since lookup() is O(1) on any of those, we prioritize least amount of results for intersection
            // ie: '123'=200 results, '456?78?9'=4000 results
            // so we process in that order
            // 
            // now there is no hard requirement to do intersection amongst multiple section searches, since we can always check preceding/following chars
            // as such, we balance the filtering by using the 2 approaches
            // keep in mind that the check for preceding/following chars must always happen whether you use intersection filtering or not
             
            var parsed = this.ParseSearchFormat(format_including_wildcards, match);

            // if searching without any wildcards
            if(parsed.ExactMatchSearch) {
                var hashtable = parsed.TotalCharacters >= this.MinNGramLength ? m_items : m_itemsNotIndexed;
                if(hashtable.Contains(format_including_wildcards))
                    yield return format_including_wildcards;
                
                yield break;
            }

            var regex = this.BuildRegex(format_including_wildcards, match);

            if(parsed.TotalCharacters < this.MinNGramLength) {
                // non-indexed search; bruteforce all potential matches
                foreach(var item in m_itemsNotIndexed) {
                    if(regex.IsMatch(item))
                        yield return item;
                }

                yield break;
            } else if(parsed.MaxSearchableNGramLength < this.MinNGramLength) {
                // if no search can be done on indexed data, then we need to manually check all items
                // non-indexed search; bruteforce all potential matches
                foreach(var item in m_items) {
                    if(regex.IsMatch(item))
                        yield return item;
                }

                yield break;
            }
 
            var orderedSectionsIndexes = parsed.Sections
                .Select((value, index) => (value, index))
                .OrderByDescending(o => o.value.SearchLength)
                .ThenBy(o => o.value.ResultMustMatchAtStart ? 0 : 1)
                .ThenBy(o => o.value.ResultMustMatchAtEnd ? 0 : 1)
                .Select(o => o.index);
 
            int epoch               = 0;
            long intersection_count = int.MaxValue;
            var results             = new Dictionary<string, EpochContainer>();
            foreach(var sectionIndex in orderedSectionsIndexes) {
                var sectionResults = this.SearchSection(
                    parsed, 
                    sectionIndex, 
                    original_string => epoch == 0 || (results.TryGetValue(original_string, out var epochContainer) && epochContainer.Epoch == epoch - 1))
                    .ToList();
 
                // ie: we either keep filtering by doing intersections, or we just try and compare the whole section
                // the rule of thumb here being that you can do roughly 4x dict compares in the time you verify the match (which will have to be done anyway)
                // keep in mind the further searches we go through, the more results we will get, thus filtering little
                // keep in mind we only care about the time savings, so this needs to filter a good portion of results
                // you could take into account the section.SearchLength and how many characters comparisons are avoided potentially if you want to try a smarter rule
                if(sectionResults.Count > intersection_count * 4)
                    break;
 
                // fast intersection
                 
                if(epoch > 0) {
                    intersection_count = 0;
                    foreach(var sectionResult in sectionResults) {
                        if(results.TryGetValue(sectionResult, out var x) && x.Epoch == epoch - 1) {
                            x.Epoch = epoch;
                            intersection_count++;
                        }
                    }
                } else {
                    intersection_count = sectionResults.Count;
                    foreach(var sectionResult in sectionResults)
                        results.Add(sectionResult, new EpochContainer() { Epoch = 0 });
                }
                 
                epoch++;
            }
 
            var intersection = results
                .Where(o => o.Value.Epoch == epoch - 1)
                .Select(o => o.Key);
 
            foreach(var intersect in intersection) {
                if(regex.IsMatch(intersect))
                    yield return intersect;
            }
        }
        private sealed class EpochContainer {
            public int Epoch;
        }
        #endregion
 
        #region DebugDump()
        public string DebugDump(bool include_ngrams = false) {
            var sb = new StringBuilder();
 
            var ngram_breakdown = new long[this.MaxNGramLength + 1];
            var ngram_unique_breakdown = new long[this.MaxNGramLength + 1];
            var excess_unique_breakdown = new long[this.MaxNGramLength + 1]; // excess capacity
            foreach(var item in m_dict) {
                ngram_breakdown[item.Key.Length] += item.Value.Count;
                ngram_unique_breakdown[item.Key.Length]++;
                excess_unique_breakdown[item.Key.Length] += item.Value.Capacity - item.Value.Count;
            }
            var ngram_count = ngram_breakdown.Sum();
            var ngram_unique_count = m_dict.Count; //ngram_unique_breakdown.Sum();
            var excess_count = excess_unique_breakdown.Sum();
 
            var value_breakdown = new Dictionary<int, long>();
            foreach(var item in this.Items) {
                if(value_breakdown.TryGetValue(item.Length, out var count))
                    count++;
                value_breakdown[item.Length] = count;
            }
 
            var total_value_chars = value_breakdown.Sum(o => o.Key * o.Value);
            var total_ngram_unique_chars = ngram_unique_breakdown.Select((o, i) => (o, i)).Sum(o => o.i * o.o); //ngram_breakdown.Select((o, i) => (o, i)).Sum(o => o.i * o.o);
 
            sb.AppendLine($"Count() = {this.Count}, ngrams = {ngram_count} / {ngram_unique_count} uniques");
            sb.AppendLine($"Min n-gram length = {this.MinNGramLength}");
            sb.AppendLine($"Max n-gram length = {this.MaxNGramLength}");
            sb.AppendLine($"Excess capacity   = {excess_count}");
 
            sb.AppendLine();
            sb.AppendLine($"n-grams breakdown  (count={ngram_count}, --- chars dont make sense in non-unique context)");
            sb.AppendLine("=============================");
            for(int i = 0; i < ngram_breakdown.Length; i++) {
                var item = ngram_breakdown[i];
                if(item == 0)
                    continue;
                sb.AppendLine($"[{i} length] {((double)item / ngram_count).ToString("P")}  {item}");
            }
 
            sb.AppendLine();
            sb.AppendLine($"n-grams breakdown UNIQUES ONLY  (count={ngram_unique_count} uniques only, {total_ngram_unique_chars} chars uniques only)");
            sb.AppendLine("=============================");
            for(int i = 0; i < ngram_unique_breakdown.Length; i++) {
                var item_unique = ngram_unique_breakdown[i];
                if(item_unique == 0)
                    continue;
                sb.AppendLine($"[{i} length] {((double)item_unique / ngram_unique_count).ToString("P")}  {item_unique} uniques only  ({item_unique * i} chars)");
            }
 
            sb.AppendLine();
            sb.AppendLine($"n-grams EXCESS capacity breakdown  (count={excess_count}, will not resize under capacity 4)");
            sb.AppendLine("=============================");
            for(int i = 0; i < excess_unique_breakdown.Length; i++) {
                var item = excess_unique_breakdown[i];
                if(item == 0)
                    continue;
                sb.AppendLine($"[{i} length] {((double)item / excess_count).ToString("P")}  {item}");
            }
 
            sb.AppendLine();
            sb.AppendLine($"value breakdown  (count={this.Count}, {total_value_chars} chars)");
            sb.AppendLine("=============================");
            foreach(var item in value_breakdown.OrderBy(o => o.Key)) {
                sb.AppendLine($"[{item.Key} length] {((double)item.Value / this.Count).ToString("P")}  {item.Value}   ({item.Key * item.Value} chars)");
            }
     
            if(include_ngrams) {
                sb.AppendLine();
                sb.AppendLine("n-grams");
                sb.AppendLine("=============================");
                foreach(var item in m_dict.OrderByDescending(o => o.Value.Count)) {
                    sb.AppendLine($"[{item.Key}] {((double)item.Value.Count / ngram_count).ToString("P")}  {item.Value.Count}");
                }
            }
     
            return sb.ToString();
        }
        #endregion
        #region static Benchmark()
        public static void Benchmark(int count = 1000000) {
            const int MIN_NGRAM = 3;
            const int MAX_NGRAM = 5;
            const uint SEED = 0xBADC0FFE;
            var TIME_PER_BENCH = TimeSpan.FromSeconds(5);

            Console.WriteLine($"building {nameof(MultiNGramIndex)}({MIN_NGRAM}, {MAX_NGRAM}) with {count} items");
            var culture    = System.Globalization.CultureInfo.InvariantCulture;
            var ngramindex = new MultiNGramIndex(MIN_NGRAM, MAX_NGRAM);

            var now = DateTime.UtcNow;

            for(int i = 0; i < count; i++) {
                ngramindex.Add(GetStableHashCode(i));
                if(i++ % 100000 == 0)
                    Console.WriteLine($"[{i - 1}] adding {DateTime.UtcNow - now}");
            }

            var bench = DateTime.UtcNow - now;
            Console.WriteLine($"total add time ({count} items): {bench}");
            Console.WriteLine();

            var all_items = ngramindex.Items.ToArray();

            var random = new Random(unchecked((int)SEED));
            Bench("EXACT MATCH", 
                SearchOption.ExactMatch, 
                WildcardRegex.SearchOption.ExactMatch,
                Enumerable.Range(0, int.MaxValue).Select(o => GetStableHashCode(random.Next(count))));

            random = new Random(unchecked((int)SEED));
            Bench("STARTS WITH", 
                SearchOption.StartsWith, 
                WildcardRegex.SearchOption.StartsWith,
                Enumerable.Range(0, int.MaxValue)
                    .Select(o => GetStableHashCode(random.Next(count)))
                    .SelectMany(o => Enumerable.Range(MIN_NGRAM, Math.Max(0, o.Length - MIN_NGRAM)).Select(k => o.Substring(0, k))));

            random = new Random(unchecked((int)SEED));
            Bench("ENDS WITH", 
                SearchOption.EndsWith, 
                WildcardRegex.SearchOption.EndsWith,
                Enumerable.Range(0, int.MaxValue)
                    .Select(o => GetStableHashCode(random.Next(count)))
                    .SelectMany(o => Enumerable.Range(MIN_NGRAM, Math.Max(0, o.Length - MIN_NGRAM)).Select(k => o.Substring(o.Length - k - 1, k))));

            random = new Random(unchecked((int)SEED));
            Bench("CONTAINS", 
                SearchOption.Contains, 
                WildcardRegex.SearchOption.Contains,
                Enumerable.Range(0, int.MaxValue)
                    .Select(o => GetStableHashCode(random.Next(count)))
                    .SelectMany(o => Enumerable.Range(MIN_NGRAM, Math.Max(0, o.Length - MIN_NGRAM)).Select(k => o.Substring(0, k))));

            foreach(var wildcard in new[] { '?', '*' }) {
                random = new Random(unchecked((int)SEED));
                Bench($"CONTAINS WITH ONE {wildcard}",
                    SearchOption.Contains,
                    WildcardRegex.SearchOption.Contains,
                    Enumerable.Range(0, int.MaxValue)
                        .Select(o => GetStableHashCode(random.Next(count)))
                        .SelectMany(item => Enumerable.Range(MIN_NGRAM + 1, Math.Max(0, item.Length - MIN_NGRAM - 1)).Select(j => item.Substring(0, j).ToCharArray()))
                        .SelectMany(o => {
                            var list = new List<string>();
                            for(int i = 0; i < o.Length; i++) {
                            // need at least one consecutive stretch of MIN_NGRAM length
                            var size_before = i;
                                var size_after  = o.Length - i - 1;
                                if(size_before < MIN_NGRAM && size_after < MIN_NGRAM)
                                    continue;

                                var c = o[i];
                                o[i] = wildcard;
                                list.Add(new string(o));
                                o[i] = c;
                            }
                            return list;
                        }));
            }

            void Bench(string search_type, SearchOption option, WildcardRegex.SearchOption regex_option, IEnumerable<string> generator) {
                Console.WriteLine($"{search_type} search...");
                now = DateTime.UtcNow;
                var enumerator = generator.GetEnumerator();
                long total = 0;
                var time = TIME_PER_BENCH;

                while(true) {
                    if(!enumerator.MoveNext()) {
                        time = DateTime.UtcNow - now;
                        break;
                    }
                    var item = enumerator.Current;
                    ngramindex.Search(item, option).Count();
                    var elapsed = DateTime.UtcNow - now;
                    if(elapsed > TIME_PER_BENCH)
                        break;
                    total++;
                }
                Console.WriteLine($"{total} {search_type} results ({time})     {total / time.TotalSeconds} / sec");
                
                now = DateTime.UtcNow;
                total = 0;
                time = new TimeSpan(TIME_PER_BENCH.Ticks / 2);

                while(true) {
                    if(!enumerator.MoveNext()) {
                        time = DateTime.UtcNow - now;
                        break;
                    }
                    var item = enumerator.Current;
                    var regex = new WildcardRegex(item, regex_option);
                    for(int i = 0; i < all_items.Length; i++) {
                        regex.IsMatch(all_items[i]);
                        if((i % 100000 == 0) && (DateTime.UtcNow - now) > new TimeSpan(TIME_PER_BENCH.Ticks / 2))
                            break;
                    }
                    var elapsed = DateTime.UtcNow - now;
                    if(elapsed > new TimeSpan(TIME_PER_BENCH.Ticks / 2))
                        break;
                    total++;
                }
                Console.WriteLine($"{total} {search_type} results ({time})     {total / time.TotalSeconds} / sec  (bruteforce)");

                now = DateTime.UtcNow;
                total = 0;
                time = new TimeSpan(TIME_PER_BENCH.Ticks / 2);

                while(true) {
                    if(!enumerator.MoveNext()) {
                        time = DateTime.UtcNow - now;
                        break;
                    }
                    var item = enumerator.Current;
                    var regex = WildcardRegex.ToRegex(item, System.Text.RegularExpressions.RegexOptions.None, regex_option);
                    for(int i = 0; i < all_items.Length; i++) {
                        regex.IsMatch(all_items[i]);
                        if((i % 100000 == 0) && (DateTime.UtcNow - now) > new TimeSpan(TIME_PER_BENCH.Ticks / 2))
                            break;
                    }
                    var elapsed = DateTime.UtcNow - now;
                    if(elapsed > new TimeSpan(TIME_PER_BENCH.Ticks / 2))
                        break;
                    total++;
                }
                Console.WriteLine($"{total} {search_type} results ({time})     {total / time.TotalSeconds} / sec  (bruteforce .net regex)");
                
                Console.WriteLine();
            }
            /// <summary>
            ///     FNV-1a (Fowler-Noll-Vo).
            ///     https://en.wikipedia.org/wiki/Fowler%E2%80%93Noll%E2%80%93Vo_hash_function
            /// </summary>
            string GetStableHashCode(int value) {
                unchecked {
                    const uint PRIME        = 16777619;
                    const uint OFFSET_BASIS = 2166136261;
                    
                    var val  = value.ToString("X", culture);
                    var hash = OFFSET_BASIS;
                    int len  = val.Length;
                    for(int i = 0; i < len; i++) {
                        hash ^= val[i];
                        hash *= PRIME;
                    }
                    return ((int)hash).ToString("X", culture);
                }
            }
        }
        #endregion
        #region static DebugBruteforceFindErrors()
        /// <summary>
        ///     Bruteforces trying to find cases that do not work.
        /// </summary>
        public static void DebugBruteforceFindErrors(double coverage = 0.25, uint seed = 0xBADC0FFE) {
            const int MIN_NGRAM = 2;
            const int MAX_NGRAM = 3;
            const int ITEMS_PER_GROUP = 1000;
            const int ITEM_COUNT = 100000;
 
            var random        = new Random(unchecked((int)seed));
            var ngramindex    = new MultiNGramIndex(MIN_NGRAM, MAX_NGRAM);
            var shuffed_items = Enumerable.Range(0, ITEM_COUNT)
                .Select(o => o.ToString())
                .OrderBy(o => random.NextDouble()) // very bad shuffle
                .ToArray();
 
            for(int group = 0; group < ITEM_COUNT; group += ITEMS_PER_GROUP) {
                var items = Enumerable.Range(group, ITEMS_PER_GROUP)
                    .Where(o => random.NextDouble() <= coverage)
                    .Select(o => shuffed_items[o])
                    .OrderBy(o => o) // for easier debugging
                    .ToArray();
                 
                ngramindex.Clear();
                ngramindex.AddRange(items);
 
                // exact match
                foreach(var item in items)
                    if(item.Length >= MIN_NGRAM && ngramindex.Search(item).Count() != 1)
                        Break();
 
                // starts with
                foreach(var item in items) {
                    for(int i = MIN_NGRAM; i < item.Length; i++)
                        Compare(items, item.Substring(0, i), SearchOption.StartsWith);
                }
 
                // ends with
                foreach(var item in items) {
                    for(int i = MIN_NGRAM; i < item.Length; i++)
                        Compare(items, item.Substring(item.Length - i - 1, i), SearchOption.EndsWith);
                }
 
                // contains
                foreach(var item in items) {
                    for(int i = MIN_NGRAM; i < item.Length; i++)
                        Compare(items, item.Substring(0, i), SearchOption.Contains);
                }
 
                // contains with one ?
                foreach(var item in items) {
                    for(int i = MIN_NGRAM + 1; i < item.Length; i++) {
                        var key = item.Substring(0, i).ToCharArray();
                        for(int j = 0; j < i; j++) {
                            // need at least one consecutive stretch of MIN_NGRAM length
                            var size_before = j;
                            var size_after  = i - j - 1;
                            if(size_before < MIN_NGRAM && size_after < MIN_NGRAM)
                                continue;
 
                            var c = key[j];
                            key[j] = '?';
                            Compare(items, new string(key), SearchOption.Contains);
                            key[j] = c;
                        }
                    }
                }
            }
 
            void Compare(string[] items, string format, SearchOption option) {
                var regex = ngramindex.BuildRegex(format, option);
                var res   = new HashSet<string>(ngramindex.Search(format, option));
                foreach(var item in items) {
                    if(!res.Contains(item) && regex.IsMatch(item))
                        Break();
                }
            }
            void Break() {
                System.Diagnostics.Debugger.Break();
            }
        }
        #endregion
 
        #region private SearchSection()
        /// <summary>
        ///     Searches for the strings that match the section.
        ///     ie: format='*123***456?78?9*'     sections = { "123", "456?78?9" }
        /// </summary>
        private IEnumerable<string> SearchSection(ParsedFormat format, int sectionIndex, Predicate<string> filter) {
            // algorithm explanation:
            // format='*123***456?78?9*'     sections = { "123", "456?78?9" }
            // section = "456?78?9"
            // 
            // basically we try and search for {'456', '78', '9'} and then do an intersection of results, 
            // since all of them need to be found
            //
            // Those searches do not focus on positioning being in-order, so we also need to manually verify at the end after intersecting
            // otherwise '789456' would match all 3 searches, but wouldnt make sense with the search format
            //
            // since lookup() is O(1) on any of those, we prioritize least amount of results for intersection
            // ie: '456'=200 results, '78'=4000 results, '9'=100000 results
            // so we process in that order
            // 
            // now there is no hard requirement to do intersection amongst multiple sub-section searches, since we can always check preceding/following chars
            // as such, we balance the filtering by using the 2 approaches
            // keep in mind that the check for preceding/following chars must always happen whether you use intersection filtering or not
            //
            // to make things more complicated, theres the case 
            // MaxNGramLength=5 
            // format='abcdeeefg?subsection*section2'
            // section = 'abcdeeefg?subsection'
            // 
            // we want to search for {'abcde', 'bcdee', 'cdeee', 'deeef', 'eeefg'}
            // this may seem redundant, but keep in mind the check for matches is O(1), and one combination might have a lot less results
            //
            // the alternative way to code this would be to use a RadixTree<string, List<NGram>> replacing Dictionary<string, List<NGram>>
            // and simply use a directed search 
            //var section_string = format.Format.Substring(section.SearchStart - section.WildcardUnknownBefore, section.WildcardUnknownBefore + section.SearchLength + section.WildcardUnknownAfter);
            //var results_before_verify = new AdaptiveRadixTree<string, List<NGram>>().WildcardMatchValues(section_string, this.WildcardUnknown);
 
            var section = format.Sections[sectionIndex];
 
            var sub_searches = SplitPosition(format.Format, section.SearchStart, section.SearchLength, this.WildcardUnknown) 
                .Where(o => o.Length >= this.MinNGramLength)
                .SelectMany(o => this.MaxNGrams(o.Index, o.Length))
                .Select(ngram => {
                    m_dict.TryGetValue(format.Format.Substring(ngram.Index, ngram.Length), out var list);
                    return (ngram.Index, list);
                })
                // avoid case where you get duplicated search patterns (ex: maxngramlength=2, search='qqq' => 'qq' & 'qq')
                .GroupBy(o => o.list) // ie: referenceequals()
                .Select(o => o.First())
                .OrderBy(o => o.list?.Count ?? 0)
                .ToList();
 
            if(sub_searches.Count == 0 || sub_searches[0].list == null)
                yield break;
 
            var searchFormatIndex = sub_searches[0].Index;
            var filtered          = format.Sections.Length == 1 && section.ResultMustMatchAtStart && section.ResultMustMatchAtEnd ?
                sub_searches[0].list.Where(o => o.Value.Length == format.TotalCharacters && filter(o.Value)) :
                sub_searches[0].list.Where(o => o.Value.Length >= format.TotalCharacters && filter(o.Value));
 
            var dict = filtered
                .GroupBy(o => o.Value)
                .ToDictionary(o => o.Key, o => new Matches(){ SearchFormatIndex = searchFormatIndex, NGrams = o.ToList(), Epoch = -1 });
                 
            // then do the intersection between those
            int epoch = 0;
            int intersection_count = dict.Count;
            for(int i = 1; i < sub_searches.Count; i++) {
                var sub_search = sub_searches[i];
 
                // ie: we either keep filtering by doing intersections, or we just try and compare the whole section
                // the rule of thumb here being that you can do roughly 4x dict compares in the time you verify the match (which will have to be done anyway)
                // keep in mind the further searches we go through, the more results we will get, thus filtering little
                // keep in mind we only care about the time savings, so this needs to filter a good portion of results
                // you could take into account the section.SearchLength and how many characters comparisons are avoided potentially if you want to try a smarter rule
                if(sub_search.list.Count > intersection_count * 4)
                    break;
 
                intersection_count = 0;
                foreach(var item in sub_search.list) {
                    if(dict.TryGetValue(item.Value, out var x) && x.Epoch == epoch - 1) {
                        x.Epoch = epoch;
                        intersection_count++;
                        //x.NGrams.Add(item);
                        // x.SearchFormatIndex = Math.Min(x.SearchFormatIndex, item.SearchFormatIndex);
                    }
                }
                epoch++;
            }
 
            // from the intersected results, make sure at least one ngram per Dict.KeyValuePair passes the format
            var intersection = dict.Where(v => v.Value.Epoch == epoch - 1);
            foreach(var intersect in intersection) {
                var potential_match = intersect.Value;
                int max             = potential_match.NGrams.Count;
                for(int i = 0; i < max; i++) {
                    var ngram           = potential_match.NGrams[i];
                    var ngramLength     = intersect.Key.Length; //ngram.Length;
                    var original_string = potential_match.Value;
 
                    if(ngram.Start < section.MinCharsBefore)
                        continue;
                    if(original_string.Length - (ngram.Start + ngramLength) < section.MinCharsAfter)
                        continue;
                     
                    // check for cases where maxngramlength=3, Add('999') Search('*1999*') ngram='999'
                    var remaining_chars_before = ngram.Start;
                    var expected_chars_before  = potential_match.SearchFormatIndex - section.SearchStart + section.WildcardUnknownBefore;
                    if(remaining_chars_before < expected_chars_before)
                        continue;
 
                    // check for cases where maxngramlength=3, Add('99173') Search('*1731*') ngram='173'
                    var remaining_chars_after = original_string.Length - (ngram.Start + ngramLength);
                    var expected_chars_after  = section.SearchLength - ngramLength - (potential_match.SearchFormatIndex - section.SearchStart) + section.WildcardUnknownAfter;
                    if(remaining_chars_after < expected_chars_after)
                        continue;
 
                    if(section.ResultMustMatchAtStart && remaining_chars_before != expected_chars_before)
                        continue;
                    if(section.ResultMustMatchAtEnd && ngram.Start + ngramLength + expected_chars_after != original_string.Length)
                        continue;
 
                    // make sure that the section matches the search format
 
                    // check preceding characters
                    // ex: 'zz*123?xxxxx' will check '123?' after 'xxxxx' match
                    var valid     = true;
                    int max2      = potential_match.SearchFormatIndex;
                    int readIndex = ngram.Start - (max2 - section.SearchStart);
                    if(readIndex < 0)
                        continue;
                     
                    for(int j = section.SearchStart; j < max2; j++) {
                        var searchFormatChar = format.Format[j];
                        var c                = original_string[readIndex++];
 
                        if(searchFormatChar != this.WildcardUnknown && c != searchFormatChar) {
                            valid = false;
                            break;
                        }
                    }
                    if(!valid)
                        continue;
 
                    // check following characters
                    // ex: 'xxxxx?123??*zz' will check '?123??' after 'xxxxx' match
                    max2      = section.SearchStart + section.SearchLength;
                    readIndex = ngram.Start + ngramLength;
 
                    for(int j = potential_match.SearchFormatIndex + ngramLength; j < max2; j++) {
                        var searchFormatChar = format.Format[j];
                        var c                = original_string[readIndex++];
 
                        if(searchFormatChar != this.WildcardUnknown && c != searchFormatChar) {
                            valid = false;
                            break;
                        }
                    }
                    if(!valid)
                        continue;
 
                    // as soon as one match is found within ngram, no need to check further
                    yield return original_string;
                    break;
                }
            }
        }
        private sealed class Matches {
            public List<NGram> NGrams;
            public int Epoch;
            public int SearchFormatIndex;
            public string Value => this.NGrams[0].Value; // they all have the same value
        }
        #endregion
        #region private MaxNGrams()
        private IEnumerable<NGramPart> MaxNGrams(int start, int length) {
            var len = Math.Min(length, this.MaxNGramLength);
            int max = start + length;
            while(start + len <= max) {
                yield return new NGramPart(start, len);
                start++;
            }
        }
        private readonly struct NGramPart {
            public readonly int Index;
            public readonly int Length;
            public NGramPart(int index, int length) {
                this.Index  = index;
                this.Length = length;
            }
        }
        #endregion
        #region private static SplitPosition()
        /// <summary>
        ///     Same as string.Split(), but for returns positions instead.
        ///     ex: "abcde".SplitPosition(1, 4, new []{'b'}) = {(2,3)}
        /// </summary>
        private static IEnumerable<Range> SplitPosition(string source, int startIndex, int length, char separator) {
            int start = startIndex;
            int max   = startIndex + length;
            int i     = startIndex;
            for(; i < max; i++) {
                if(source[i] == separator) {
                    yield return new Range(start, i - start);
                    start = i + 1;
                }
            }
            yield return new Range(start, i - start);
        }
        #endregion
        #region private BuildRegex()
        private WildcardRegex BuildRegex(string format_including_wildcards, SearchOption match) {
            WildcardRegex.SearchOption option;
            switch(match) {
                case SearchOption.ExactMatch: option = WildcardRegex.SearchOption.ExactMatch; break;
                case SearchOption.Contains:   option = WildcardRegex.SearchOption.Contains; break;
                case SearchOption.StartsWith: option = WildcardRegex.SearchOption.StartsWith; break;
                case SearchOption.EndsWith:   option = WildcardRegex.SearchOption.EndsWith; break;
                default: throw new NotImplementedException();
            }
 
            return new WildcardRegex(
                format_including_wildcards, 
                option, 
                this.WildcardUnknown, 
                this.WildcardAnything);
                //.ToRegex(System.Text.RegularExpressions.RegexOptions.None)
        }
        #endregion
 
        #region private ParseSearchFormat()
        private ParsedFormat ParseSearchFormat(string format, SearchOption match = SearchOption.ExactMatch) {
            var sections = ParseSearchFormatSections(format)
                .Where(o => o.len > 0) // avoids empty sections in cases such as 'aa**aa', '*aa' and 'aa*'
                .Select(o => new ConsecutiveParseSection(){ SearchStart = o.start, SearchLength = o.len })
                .ToList();
 
            for(int i = 0; i < sections.Count; i++) {
                var section = sections[i];
                 
                // TrimStart(this.WildcardUnknown)
                while(section.SearchLength > 0 && format[section.SearchStart] == this.WildcardUnknown) {
                    section.SearchLength--;
                    section.SearchStart++;
                    section.WildcardUnknownBefore++;
                    section.MinCharsBefore++;
                }
                // TrimEnd(this.WildcardUnknown)
                while(section.SearchLength > 0 && format[section.SearchStart + section.SearchLength - 1] == this.WildcardUnknown) {
                    section.SearchLength--;
                    section.WildcardUnknownAfter++;
                    section.MinCharsAfter++;
                }
 
                if(i > 0) {
                    var prev = sections[i - 1];
                    section.MinCharsBefore += prev.SearchLength + prev.MinCharsBefore + prev.MinCharsAfter;
                }
            }
            for(int i = sections.Count - 2; i >= 0; i--) {
                var next = sections[i + 1];
                sections[i].MinCharsAfter += next.SearchLength + next.MinCharsBefore + next.MinCharsAfter;
            }
 
            if(match == SearchOption.ExactMatch || match == SearchOption.StartsWith)
                sections[0].ResultMustMatchAtStart = format[0] != this.WildcardAnything;
            if(match == SearchOption.ExactMatch || match == SearchOption.EndsWith)
                sections[sections.Count - 1].ResultMustMatchAtEnd = format[format.Length - 1] != this.WildcardAnything;
 
            // merge '??' section with prev
            // ex: 'abc*??*456' -> 'abc??*456'
            int index = 1;
            while(index < sections.Count) {
                var section = sections[index];
                if(section.SearchLength == 0 && !section.ResultMustMatchAtStart && !section.ResultMustMatchAtEnd && (section.WildcardUnknownBefore > 0 || section.WildcardUnknownAfter > 0)) {
                    sections[index - 1].WildcardUnknownAfter += section.WildcardUnknownBefore + section.WildcardUnknownAfter;
                    sections.RemoveAt(index);
                } else
                    index++;
            }
            // move ['??' at section start] to [previous section end] for faster parse
            // ex: 'abc?*??456' -> 'abc???*456'
            index = 1;
            while(index < sections.Count) {
                var section = sections[index];
                if(section.WildcardUnknownBefore > 0) {
                    sections[index - 1].WildcardUnknownAfter += section.WildcardUnknownBefore;
                    section.WildcardUnknownBefore = 0;
                }
                index++;
            }

            int max_search = 0;
            for(int i = 0; i < sections.Count; i++) {
                var section = sections[i];
                if(section.SearchLength <= max_search)
                    continue;
                int count = 0;
                for(int j = 0; j < section.SearchLength; j++) {
                    var c = format[section.SearchStart + j];
                    if(c != this.WildcardUnknown)
                        count++;
                    else {
                        max_search = Math.Max(max_search, count);
                        count = 0;
                    }
                }
                max_search = Math.Max(max_search, count);
            }

            var res = new ParsedFormat() {
                Format                   = format,
                Sections                 = sections.ToArray(),
                TotalCharacters          = sections[0].SearchLength + sections[0].MinCharsBefore + sections[0].MinCharsAfter,
                MaxSearchableNGramLength = max_search,
                //ExactMatchSearch       = ,
            };
            // && match == SearchOption.ExactMatch 
            res.ExactMatchSearch = res.MaxSearchableNGramLength == res.TotalCharacters && sections.Count == 1 && sections[0].ResultMustMatchAtStart && sections[0].ResultMustMatchAtEnd;
            return res;
        }
        /// <summary>
        ///     basically does format.Split(WildcardAnything)
        /// </summary>
        private IEnumerable<(int start, int len)> ParseSearchFormatSections(string format) {
            int start = 0;
            int len   = 0;
 
            for(int i = 0; i < format.Length; i++) {
                var c = format[i];
                len++;
 
                if(c == this.WildcardAnything) {
                    if(len > 1)
                        yield return (start, len - 1);
 
                    start = i + 1;
                    len   = 0;
                }
            }
            if(len > 0)
                yield return (start, len);
        }
        private sealed class ParsedFormat {
            /// <summary>
            ///     Unmodified user-specified search format.
            /// </summary>
            public string Format;
            /// <summary>
            ///     The longest length of consecutive characters that arent {?, *}.
            /// </summary>
            public int MaxSearchableNGramLength;
            /// <summary>
            ///     Total number of characters to match, including ?s but excluding *s.
            /// </summary>
            public int TotalCharacters;
            public ConsecutiveParseSection[] Sections;
            /// <summary>
            ///     If true, means we are searching for an exact value with an ExactMatch and without any wildcards.
            ///     ex: 'ABC123'
            /// </summary>
            public bool ExactMatchSearch;
        }
        /// <summary>
        ///     Represents a section of consecutive characters without any WILDCARD_ANYTHING in it.
        ///     This may include multiple WILDCARD_UNKNOWN.
        /// </summary>
        private sealed class ConsecutiveParseSection {
            /// <summary>
            ///     The start position within the search format of the ngram to search for.
            /// </summary>
            public int SearchStart;
            /// <summary>
            ///     The length within the search format of the ngram to search for.
            /// </summary>
            public int SearchLength;
            /// <summary>
            ///     How many characters before SearchStart within search format to continue matching.
            ///     ie: how many WILDCARD_UNKNOWN are at the start of the section.
            /// </summary>
            public int WildcardUnknownBefore;
            /// <summary>
            ///     How many characters after {SearchStart+SearchLength} within search format to continue matching.
            ///     ie: how many WILDCARD_UNKNOWN are at the end of the section.
            /// </summary>
            public int WildcardUnknownAfter;
            /// <summary>
            ///     How many characters before SearchStart must exist in the match.
            ///     ex: search format 'prev_section*current_section' -> length('prev_section') = 12
            /// </summary>
            public int MinCharsBefore;
            /// <summary>
            ///     How many characters after {SearchStart+SearchLength} must exist in the match.
            ///     ex: search format 'current_section*next_section' -> length('next_section') = 12
            /// </summary>
            public int MinCharsAfter;
            /// <summary>
            ///     Indicates result/match must start at 0.
            /// </summary>
            public bool ResultMustMatchAtStart;
            /// <summary>
            ///     Indicates result/match must end at comparand.Length.
            /// </summary>
            public bool ResultMustMatchAtEnd;
        }
        #endregion
        #region private GenerateNGrams()
        /// <summary>
        ///     Decomposes the input into all n-gram variations.
        ///     ie: [abcd] => {abcd, abc, bcd, ab, bc, cd, a, b, c, d}
        ///     
        ///     This is typically used for efficient sub-string searching.
        ///     Could also be used as an alternative to a Generalized Suffix Tree for string searching.
        /// </summary>
        /// <param name="length">The string.Length you wish to decompose.</param>
        private IEnumerable<Range> GenerateNGram(int length) {
            // note: reverse-order is intentional, as that yields better performance (ie: more initial filtering)
            for(int n = Math.Min(length, this.MaxNGramLength); n >= this.MinNGramLength; n--) {
                int count = length - n;
                for(int i = 0; i <= count; i++)
                    yield return new Range(i, n);
            }
        }
        #endregion
 
        // maybe replace dict by radix/btree and use btree.startswith()
        // or maybe try with StringBuilder() instead of List<NGram> for faster building
        // consider making a startswith and endswith -specific index
        // better yet, a startswith index where the positioning is known
        // in other words, i could search for '??abc' where 'abc' starts at exactly index 2
        // could also be done via List<NGram> -> BTree<NGram> ordered by position
        // could also include a negativeindex ordering for end positioning
        // or maybe just do List<NGram> -> List<NGram>[], where theres one list per position
 
            //CREATE INDEX titles_trigrams_gin_idx ON titles USING GIN(trigrams_vector(title));
            // SELECT COUNT(*) FROM titles WHERE trigrams_vector(title) @@ trigrams_query('adventures');
            //CREATE OR REPLACE FUNCTION trigrams_array(word text)
            //        RETURNS text[]
            //        IMMUTABLE STRICT
            //        LANGUAGE "plpgsql"
            //AS $$
            //        DECLARE
            //                result text[];
            //        BEGIN
            //                FOR i IN 1 .. length(word) - 2 LOOP
            //                        result := result || quote_literal(substr(lower(word), i, 3));
            //                END LOOP;
            //
            //                RETURN result;
            //        END;
            //$$;
            //CREATE OR REPLACE FUNCTION trigrams_vector(text)
            //        RETURNS tsvector
            //        IMMUTABLE STRICT
            //        LANGUAGE "SQL"
            //AS $$
            //        SELECT array_to_string(trigrams_array($1), ' ')::tsvector;
            //$$;
            //CREATE OR REPLACE FUNCTION trigrams_query(text)
            //        RETURNS tsquery
            //        IMMUTABLE STRICT
            //        LANGUAGE "SQL"
            //AS $$
            //        SELECT array_to_string(trigrams_array($1), ' & ')::tsquery;
            //$$;
 
        

        #region public enum SearchOption
        public enum SearchOption {
            /// <summary>
            ///     equivalent to "value = 'searchstring'"
            /// </summary>
            ExactMatch,
            /// <summary>
            ///     equivalent to "value LIKE '%searchstring%'"
            ///     ie: contains(value)
            /// </summary>
            Contains,
            /// <summary>
            ///     equivalent to "value LIKE 'searchstring%'"
            /// </summary>
            StartsWith,
            /// <summary>
            ///     equivalent to "value LIKE '%searchstring'"
            /// </summary>
            EndsWith,
        }
        #endregion
        #region private readonly struct NGram
        private readonly struct NGram : IEquatable<NGram> {
            public readonly string Value;
            public readonly int Start;
 
            public NGram(string value, int start) {
                this.Value  = value;
                this.Start  = start;
            }
 
            public override string ToString() => $"[{this.Value.Substring(this.Start)}] {this.Value}  ({{{this.Start}}})";

            
            public bool Equals(NGram other) {
                return this.Value == other.Value && this.Start == other.Start;
            }
            public override bool Equals(object obj) {
                if(obj is NGram memseg)
                    return this.Equals(memseg);
                return false;
            }
            public static bool operator ==(NGram x, NGram y) {
                return x.Equals(y);
            }
            public static bool operator !=(NGram x, NGram y) {
                return !(x == y);
            }
            public override int GetHashCode() {
                return (this.Value, this.Start).GetHashCode();
            }
        }
        #endregion
        #region private struct Range
        private readonly struct Range {
            public readonly int Index;
            public readonly int Length;
            public Range(int index, int length) {
                this.Index  = index;
                this.Length = length;
            }
        }
        #endregion
    }
}