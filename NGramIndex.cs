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
    ///     ie: [abcd] => {a, b, c, d, ab, bc, cd, abc, bcd, abcd}
    /// </summary>
    public sealed class NGramIndex {
        private readonly Dictionary<string, List<NGram>> m_dict = new Dictionary<string, List<NGram>>();

        public readonly int MinNGramLength;
        public readonly int MaxNGramLength;
        public int Count { get; private set; }

        #region constructors
        public NGramIndex(int minNGramLength, int maxNGramLength) {
            this.MinNGramLength = minNGramLength;
            this.MaxNGramLength = maxNGramLength;
        }
        #endregion

        #region Add()
        public void Add(string value) {
            foreach(var ngram in GenerateNGram(value.Length, this.MinNGramLength, this.MaxNGramLength)) {
                var ngram_string = value.Substring(ngram.Start, ngram.Length);

                if(!m_dict.TryGetValue(ngram_string, out var list)) {
                    list = new List<NGram>();
                    m_dict.Add(ngram_string, list);
                }

                list.Add(new NGram(value, ngram.Start, ngram.Length));
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
        public bool Remove(string value) {
            foreach(var ngram in GenerateNGram(value.Length, this.MinNGramLength, this.MaxNGramLength)) {
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
                        list.RemoveAt(index);
                        break;
                    }
                }

                if(!found)
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
            this.Count = 0;
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

        #region private static GenerateNGrams()
        /// <summary>
        ///     Decomposes the input into all n-gram variations.
        ///     ie: [abcd] => {a, b, c, d, ab, bc, cd, abc, bcd, abcd}
        ///     
        ///     This is typically used for efficient sub-string searching.
        ///     Could also be used as an alternative to a Generalized Suffix Tree for string searching.
        /// </summary>
        /// <param name="length">The string.Length you wish to decompose.</param>
        private static IEnumerable<InternalNGram> GenerateNGram(int length, int min, int max) {
            if(min <= 0)
                min = 1;
            if(max > length)
                max = length;
            
            for(int n = min; n < max; n++) {
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
