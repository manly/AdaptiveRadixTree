using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;


namespace System.Collections.Specialized
{
    public class NGramIndex {
        #region static GenerateNGrams()
        /// <summary>
        ///     Decomposes the input into all n-gram variations.
        ///     ie: [abcd] => {a, b, c, d, ab, bc, cd, abc, bcd, abcd}
        ///     
        ///     This is typically used for efficient sub-string searching.
        ///     Could also be used as an alternative to a Generalized Suffix Tree for string searching.
        /// </summary>
        /// <param name="length">The string.Length you wish to decompose.</param>
        public static IEnumerable<NGram> GenerateNGram(int length, int min, int max) {
            if(min <= 0)
                min = 1;
            if(max > length)
                max = length;
            
            for(int n = min; n < max; n++) {
                int count = length - n;
                for(int i = 0; i < count; i++)
                    yield return new NGram(i, n);
            }
        }
        public readonly struct NGram {
            public readonly int Start;
            public readonly int Length;
            public NGram(int start, int length) {
                this.Start  = start;
                this.Length = length;
            }
        }
        #endregion
    }
}
