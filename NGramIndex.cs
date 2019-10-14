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
    ///     Allows n-gram indexing, used typically for O(1) sub-string searches.
    ///     ie: [abcd] => {abcd, abc, bcd, ab, bc, cd, a, b, c, d}
    /// </summary>
    public sealed class NGramIndex {
        private const char DEFAULT_WILDCARD_UNKNOWN  = '?';
        private const char DEFAULT_WILDCARD_ANYTHING = '*';

        private readonly Dictionary<string, List<NGram>> m_dict = new Dictionary<string, List<NGram>>();

        public readonly int MinNGramLength; // inclusive
        public readonly int MaxNGramLength; // inclusive
        public readonly bool AllowDuplicates;
        private readonly char WildcardUnknown;  // = ?
        private readonly char WildcardAnything; // = *
        public int Count { get; private set; }

        #region constructors
        /// <param name="minNGramLength">Inclusive.</param>
        /// <param name="maxNGramLength">Inclusive.</param>
        public NGramIndex(int minNGramLength, int maxNGramLength, bool allowDuplicates, char wildcard_unknown_character = DEFAULT_WILDCARD_UNKNOWN, char wildcard_anything_character = DEFAULT_WILDCARD_ANYTHING) {
            this.MinNGramLength   = Math.Max(minNGramLength, 1);
            this.MaxNGramLength   = Math.Max(maxNGramLength, this.MinNGramLength);
            this.AllowDuplicates  = allowDuplicates;
            this.WildcardUnknown  = wildcard_unknown_character;
            this.WildcardAnything = wildcard_anything_character;
        }
        #endregion

        #region Add()
        /// <summary>
        ///     Average: O(k)    k = value.Length
        ///     Slightly slower if AllowDuplicates==false.
        ///     
        ///     Throws ArgumentNullException on null value.
        ///     Throws ArgumentException on empty/duplicate value.
        /// </summary>
        public void Add(string value) {
            if(value == null)
                throw new ArgumentNullException(nameof(value));
            if(value.Length == 0)
                throw new ArgumentException(nameof(value));

            using(var enumerator = this.GenerateNGram(value.Length).GetEnumerator()) {
                if(!this.AllowDuplicates) {
                    if(!enumerator.MoveNext())
                        return;

                    var ngram        = enumerator.Current;
                    var ngram_string = value.Substring(ngram.Start, ngram.Length);

                    if(!m_dict.TryGetValue(ngram_string, out var list)) {
                        list = new List<NGram> {
                        new NGram(value, ngram.Start, ngram.Length)
                    };
                        m_dict.Add(ngram_string, list);
                    } else {
                        // check if the value exists
                        for(int i = 0; i < list.Count; i++) {
                            if(list[i].Value == value)
                                throw new ArgumentException($"The value ({value}) already exists.", nameof(value));
                        }
                        list.Add(new NGram(value, ngram.Start, ngram.Length));
                    }
                }

                while(enumerator.MoveNext()) {
                    var ngram        = enumerator.Current;
                    var ngram_string = value.Substring(ngram.Start, ngram.Length);

                    if(!m_dict.TryGetValue(ngram_string, out var list)) {
                        list = new List<NGram>();
                        m_dict.Add(ngram_string, list);
                    }

                    list.Add(new NGram(value, ngram.Start, ngram.Length));
                }
            }
            this.Count++;
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
        ///     Throws ArgumentException on empty/duplicate value.
        /// </summary>
        public bool Remove(string value) {
            if(value == null)
                throw new ArgumentNullException(nameof(value));
            if(value.Length == 0)
                throw new ArgumentException(nameof(value));

            // avoid expensive string comparison
            string reference = null;
            using(var enumerator = this.GenerateNGram(value.Length).GetEnumerator()) {
                if(!enumerator.MoveNext())
                    return false;

                // try find the first item so as to get the actual reference to the original string
                // this allows significant speedup in string comparison for all future compares
                var ngram        = enumerator.Current;
                var ngram_string = value.Substring(ngram.Start, ngram.Length);

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
                    ngram = enumerator.Current;
                    ngram_string = value.Substring(ngram.Start, ngram.Length);

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
            this.Count = 0;
        }
        #endregion
        #region ContainsFullValue()
        /// <summary>
        ///     Returns true if value was previously added.
        ///     
        ///     Throws ArgumentNullException on null value.
        ///     Throws ArgumentException on empty/duplicate value.
        /// </summary>
        public bool ContainsFullValue(string value) {
            if(value == null)
                throw new ArgumentNullException(nameof(value));
            if(value.Length == 0)
                throw new ArgumentException(nameof(value));

            if(!this.GenerateFirstNGram(value.Length, out var ngram))
                return false;
                
            var ngram_string = value.Substring(ngram.Start, ngram.Length);

            if(m_dict.TryGetValue(ngram_string, out var list)) {
                for(int i = 0; i < list.Count; i++) {
                    if(list[i].Value == value)
                        return true;
                }
            }

            return false;
        }
        #endregion

        #region Search()
        /// <summary>
        ///     O(1)
        ///     Returns all n-gram matches.
        /// </summary>
        public List<NGram> Search(string value) {
            m_dict.TryGetValue(value, out var result);
            return result;
        }
        #endregion
        //#region SearchWithWildcards()
        ///// <summary>
        /////     O(1)
        /////     Returns all n-gram matches.
        ///// </summary>
        //public List<NGram> SearchWithWildcards(string value) {
        //}
        //#endregion

        #region private ParseSearchFormat()
        private IEnumerable<ParseSection> ParseSearchFormat(string format) {
            var current = new ParseSection();

            for(int i = 0; i < format.Length; i++) {
                var c = format[i];
                current.Length++;

                if(c == this.WildcardAnything || c == this.WildcardUnknown) {
                    if(current.Length > 1) {
                        current.Length--;
                        yield return current;
                        yield return new ParseSection() { Start = i, Length = 1, IsWildcard = true };
                    } else {
                        current.IsWildcard = true;
                        yield return current;
                    }

                    current = new ParseSection() {
                        Start = i + 1,
                    };
                }   
            }
            if(current.Length > 0)
                yield return current;
        }
        private sealed class ParseSection {
            public int Start;
            public int Length;
            public bool IsWildcard;
        }
        #endregion
        // todo: combinesearchformat() so that anything following * is treated with uncertain positioning
        //       and any [chunk+?+chunk], [chunk+?] or [?+chunk] gets combined into one chunk

        #region private GenerateNGrams()
        /// <summary>
        ///     Decomposes the input into all n-gram variations.
        ///     ie: [abcd] => {abcd, abc, bcd, ab, bc, cd, a, b, c, d}
        ///     
        ///     This is typically used for efficient sub-string searching.
        ///     Could also be used as an alternative to a Generalized Suffix Tree for string searching.
        /// </summary>
        /// <param name="length">The string.Length you wish to decompose.</param>
        private IEnumerable<InternalNGram> GenerateNGram(int length) {
            int max = this.MaxNGramLength;
            if(max > length)
                max = length;
            
            // note: reverse-order is intentional, as that yields better performance (ie: more initial filtering)
            for(int n = max; n >= this.MinNGramLength; n--) {
                int count = length - n;
                for(int i = 0; i < count; i++)
                    yield return new InternalNGram(i, n);
            }
        }
        private readonly struct InternalNGram {
            public readonly int Start;
            public readonly int Length;
            public InternalNGram(int start, int length) {
                this.Start  = start;
                this.Length = length;
            }
        }
        #endregion
        #region private GenerateFirstNGram()
        private bool GenerateFirstNGram(int length, out InternalNGram result) {
            int max = this.MaxNGramLength;
            if(max > length)
                max = length;

            // note: reverse-order is intentional, as that yields better performance (ie: more initial filtering)
            if(max >= this.MinNGramLength) {
                result = new InternalNGram(0, max);
                return true;
            } else {
                result = default;
                return false;
            }
        }
        #endregion

        public sealed class NGram {
            public readonly string Value;
            public readonly int Start;
            public readonly int length;

            public NGram(string value, int start, int length) {
                this.Value  = value;
                this.Start  = start;
                this.length = length;
            }
        }
    }
}
