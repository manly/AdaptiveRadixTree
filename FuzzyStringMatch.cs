using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;


namespace System.Collections.Specialized
{
    public static class FuzzyStringMatch {
        #region static SorensenDiceCoefficient()
        /// <summary>
        ///     O(n log n)
        ///     Calculates the Sørensen–Dice Coefficient between 2 strings, which are assumed to be sentences.
        ///     Sometimes called the "Strike a match" algorithm, due to re-discovery of old algorithms.
        ///     Does not satisfy the triangle inequality.
        ///     
        ///     This is a very efficient string similarity check that
        ///         - ranks well lexical similarity
        ///         - works great with changed word ordering
        ///         - language agnostic
        ///         
        ///     This works simply by generating the bigrams (ex: 'abcd' -> {ab,bc,cd})
        ///     and then counting the number of matching bigrams.
        ///     
        ///     0 = no match
        ///     1 = perfect match
        ///     
        ///     this() = (bigram_overlap_count * 2) / (bigrams_a_count + bigrams_b_count)
        /// </summary>
        /// <param name="filter">Default: null. If null, assumes all characters are valid. Implicit castable from string.</param>
        public static double SorensenDiceCoefficient(string source, string target, CharacterFilter filter = null) {
            if(string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0;
            if(source.Length == 1 || target.Length == 1)
                return 0;
            if(object.ReferenceEquals(source, target))
                return 1;

            if(filter == null){
                // use a single array for improved cache locality
                int sourceCount = source.Length;
                int targetCount = target.Length;
                int bigramTotal = sourceCount + targetCount - 2;
                int[] bigrams   = new int[bigramTotal];

                for(int i = 1; i < sourceCount; i++)
                    bigrams[i - 1]               = source[i - 1] | (source[i] << 16);
                for(int i = 1; i < targetCount; i++)
                    bigrams[i - 2 + sourceCount] = target[i - 1] | (target[i] << 16);
            
                Array.Sort(bigrams, 0, sourceCount - 1);
                Array.Sort(bigrams, sourceCount - 1, targetCount - 1);
            
                int matches     = 0;
                int indexSource = 0;
                int indexTarget = sourceCount - 1;
                while(indexSource < sourceCount - 1 && indexTarget < bigramTotal) {
                    var diff = bigrams[indexSource] - bigrams[indexTarget];
                    if(diff == 0) {
                        matches += 2;
                        indexSource++;
                        indexTarget++;
                    } else if(diff < 0)
                        indexSource++;
                    else
                        indexTarget++;
                }
                return (double)matches / bigramTotal;
            } else {
                int[] sourceBigrams = new int[16];
                int[] targetBigrams = new int[16];
                int sourceCount     = 0;
                int targetCount     = 0;

                int sourceLength    = source.Length;
                int targetLength    = target.Length;

                var internalFilter        = filter.Filter;

                char prevC                = source[0];
                bool prevCharacterIsValid = internalFilter[prevC];
                for(int i = 1; i < sourceLength; i++) {
                    var c              = source[i];
                    var validCharacter = internalFilter[c];

                    if(prevCharacterIsValid && validCharacter) {
                        if(sourceCount == sourceBigrams.Length)
                            Array.Resize(ref sourceBigrams, sourceCount * 2);
                        sourceBigrams[sourceCount++] = prevC | (c << 16);
                    }

                    prevC                = c;
                    prevCharacterIsValid = validCharacter;
                }

                prevC                = target[0];
                prevCharacterIsValid = internalFilter[prevC];
                for(int i = 1; i < targetLength; i++) {
                    var c              = target[i];
                    var validCharacter = internalFilter[c];

                    if(prevCharacterIsValid && validCharacter) {
                        if(targetCount == targetBigrams.Length)
                            Array.Resize(ref targetBigrams, targetCount * 2);
                        targetBigrams[targetCount++] = prevC | (c << 16);
                    }

                    prevC                = c;
                    prevCharacterIsValid = validCharacter;
                }

                Array.Sort(sourceBigrams, 0, sourceCount);
                Array.Sort(targetBigrams, 0, targetCount);
            
                int matches     = 0;
                int indexSource = 0;
                int indexTarget = 0;
                while(indexSource < sourceCount && indexTarget < targetCount) {
                    var diff = sourceBigrams[indexSource] - targetBigrams[indexTarget];
                    if(diff == 0) {
                        matches += 2;
                        indexSource++;
                        indexTarget++;
                    } else if(diff < 0)
                        indexSource++;
                    else
                        indexTarget++;
                }

                return (double)matches / (sourceCount + targetCount);
            }
        }
        #endregion
        #region static LevenshteinDistance()
        /// <summary>
        ///     O(m * n)
        ///     Returns the Levenshtein distance between the 2 strings (0 = perfect match).
        ///     AKA the edit distance (how many edits are needed to modify s to t).
        ///     Satisfies the triangle inequality.
        /// 
        ///     Great for single words comparison, poorly handles multi-word and word substitutions.
        /// 
        ///     If you want a percentage: this()/(double)Math.Max(source.Length, target.Length).
        /// </summary>
        /// <param name="cost_transpose_adjacent">Default: -1. If -1, means the feature is disabled. Transposes are adjacent letters that are inverted. Uses the *True* Damerau-Levenshtein distance variant, which respects the triangle equality. ex: 'ah'/'ha' would return 1x cost_transpose_adjacent instead of 2x cost_substitute.</param>
        /// <param name="max_cost">Default: -1. If -1, will assume no stopping condition. Stops counting once the value is reached and returns that value if so. Returned values may be higher than this, but the main loop will be stopped.</param>
        public static int LevenshteinDistance(string source, string target, int cost_insert = 1, int cost_delete = 1, int cost_substitute = 1, int cost_transpose_adjacent = -1, int max_cost = -1) {
            // memory optimisation
            if(target.Length < source.Length) {
                var temp1    = source;
                source       = target;
                target       = temp1;

                var temp2    = cost_insert;
                cost_insert  = cost_delete;
                cost_delete  = temp2;
            }

            var sourceLength = source.Length;
            var targetLength = target.Length;
            int start        = 0;
            var process_max  = max_cost >= 0 && max_cost < int.MaxValue;

            if(sourceLength == 0)
                return targetLength * cost_insert;
            if(process_max) {
                var diff = (targetLength - sourceLength) * cost_insert;
                if(diff >= max_cost)
                    return max_cost;
            }

            // ignore common suffixes
            while(sourceLength > 0 && source[sourceLength - 1] == target[targetLength - 1]) {
                sourceLength--; 
                targetLength--;
            }
            // ignore common prefixes
            while(start < sourceLength && source[start] == target[start])
                start++;
            sourceLength -= start;
            targetLength -= start;
            if(sourceLength == 0)
                return targetLength * cost_delete;

            // todo: make a version with byte/ushort/uint rather than int, since most time we dont need to count that high
            // this would improve cache locality and mem usage
            //var maxCost = (sourceLength + 1) * Math.Max(cost_delete, Math.Max(cost_insert, Math.Max(cost_substitute, cost_transpose_real)));
            //if(maxCost <= byte.MaxValue) {
            //}

            var process_transposes = cost_transpose_adjacent >= 0;
            var prev_row_min       = int.MinValue; // intentionally min

            var curr               = new int[sourceLength + 1];
            var prev               = new int[sourceLength + 1];
            var prev_prev          = !process_transposes ? null : new int[sourceLength + 1];

            // to transform the first i characters of s into the first 0 characters of t, 
            // we must perform i deletions.
            for(var i = 1; i <= sourceLength; i++)
                prev[i] = i * cost_delete;

            for(var i = 1; i <= targetLength; i++) {
                var current_y = target[i + start - 1];
                curr[0]       = i * cost_insert;
                var row_min   = int.MaxValue;

                for(var j = 1; j <= sourceLength; j++) {
                    var current_x  = source[j + start - 1];
                    var is_same    = current_x == current_y;

                    var insert     = prev[j    ] + cost_insert;
                    var delete     = curr[j - 1] + cost_delete;
                    var substitute = prev[j - 1] + (is_same ? 0 : cost_substitute);

                    var cost = Math.Min(insert, Math.Min(delete, substitute));

                    // *True* Damereau-Levenshtein variant
                    if(process_transposes && i > 1 && j > 1 && current_x == target[i + start - 2] && source[j + start - 2] == current_y)
                        cost = Math.Min(cost, prev_prev[j - 2] + (is_same ? 0 : cost_transpose_adjacent)); // i?

                    if(process_max)
                        row_min = Math.Min(row_min, cost);

                    curr[j] = cost;
                }

                if(process_max) {
                    if(row_min >= max_cost && (!process_transposes || prev_row_min >= max_cost))
                        return max_cost;
                    prev_row_min = row_min;
                }

                var swap = prev;
                prev     = curr;
                curr     = swap;

                if(process_transposes) {
                    swap      = prev_prev;
                    prev_prev = curr;
                    curr      = swap;
                }
            }

            return prev[sourceLength];
        }
        /// <summary>
        ///     O(m * n)
        ///     Returns the Levenshtein distance between the 2 strings (0 = perfect match).
        ///     AKA the edit distance (how many edits are needed to modify s to t).
        ///     Satisfies the triangle inequality.
        ///     
        ///     Great for single words comparison, poorly handles multi-word and word substitutions.
        ///     
        ///     If you want a percentage: this()/(double)Math.Max(source_len, target_len).
        /// </summary>
        /// <param name="cost_transpose_adjacent">Default: -1. If -1, means the feature is disabled. Transposes are adjacent letters that are inverted. Uses the *True* Damerau-Levenshtein distance variant, which respects the triangle equality. ex: 'ah'/'ha' would return 1x cost_transpose_adjacent instead of 2x cost_substitute.</param>
        /// <param name="max_cost">Default: -1. If -1, will assume no stopping condition. Stops counting once the value is reached and returns that value if so. Returned values may be higher than this, but the main loop will be stopped.</param>
        public static int LevenshteinDistance(string source, int source_index, int source_len, string target, int target_index, int target_len, int cost_insert = 1, int cost_delete = 1, int cost_substitute = 1, int cost_transpose_adjacent = -1, int max_cost = -1) {
            // memory optimisation
            if(target_len < source_len) {
                var temp1    = source;
                source       = target;
                target       = temp1;

                var temp2    = source_index;
                source_index = target_index;
                target_index = temp2;

                var temp3    = source_len;
                source_len   = target_len;
                target_len   = temp3;

                var temp4    = cost_insert;
                cost_insert  = cost_delete;
                cost_delete  = temp4;
            }

            var process_max  = max_cost >= 0 && max_cost < int.MaxValue;
            if(source_len == 0)
                return target_len * cost_insert;
            if(process_max) {
                var diff = (target_len - source_len) * cost_insert;
                if(diff >= max_cost)
                    return max_cost;
            }

            // ignore common suffixes
            while(source_len > 0 && source[source_len - 1] == target[target_len - 1]) {
                source_len--; 
                target_len--;
            }
            // ignore common prefixes
            int start = 0;
            while(start < source_len && source[start] == target[start])
                start++;
            source_len -= start;
            target_len -= start;
            if(source_len == 0)
                return target_len * cost_delete;
            source_index += start;
            target_index += start;

            var process_transposes = cost_transpose_adjacent >= 0;
            var prev_row_min       = int.MinValue; // intentionally min

            var curr               = new int[source_len + 1];
            var prev               = new int[source_len + 1];
            var prev_prev          = !process_transposes ? null : new int[source_len + 1];

            // to transform the first i characters of s into the first 0 characters of t, 
            // we must perform i deletions.
            for(var i = 1; i <= source_len; i++)
                prev[i] = i * cost_delete;

            for(var i = 1; i <= target_len; i++) {
                var current_y = target[i + target_index - 1];
                curr[0]       = i * cost_insert;
                var row_min   = int.MaxValue;

                for(var j = 1; j <= source_len; j++) {
                    var current_x  = source[j + source_index - 1];
                    var is_same    = current_x == current_y;

                    var insert     = prev[j    ] + cost_insert;
                    var delete     = curr[j - 1] + cost_delete;
                    var substitute = prev[j - 1] + (is_same ? 0 : cost_substitute);

                    var cost = Math.Min(insert, Math.Min(delete, substitute));

                    // *True* Damereau-Levenshtein variant
                    if(process_transposes && i > 1 && j > 1 && current_x == target[i + target_index - 2] && source[j + source_index - 2] == current_y)
                        cost = Math.Min(cost, prev_prev[j - 2] + (is_same ? 0 : cost_transpose_adjacent)); // i?

                    if(process_max)
                        row_min = Math.Min(row_min, cost);

                    curr[j] = cost;
                }

                if(process_max) {
                    if(row_min >= max_cost && (!process_transposes || prev_row_min >= max_cost))
                        return max_cost;
                    prev_row_min = row_min;
                }

                var swap = prev;
                prev     = curr;
                curr     = swap;

                if(process_transposes) {
                    swap      = prev_prev;
                    prev_prev = curr;
                    curr      = swap;
                }
            }

            return prev[source_len];
        }
        #endregion
        #region static WeightedLevenshteinDistance()
        /// <summary>
        ///     O(m * n)
        ///     Returns the Levenshtein distance between the 2 strings (0 = perfect match).
        ///     AKA the edit distance (how many edits are needed to modify s to t).
        ///     Satisfies the triangle inequality.
        /// 
        ///     Great for single words comparison, poorly handles multi-word and word substitutions.
        /// 
        ///     If you want a percentage: this()/Math.Max(source.Length, target.Length).
        /// </summary>
        /// <param name="cost_substitute">Default: 1. Func(char from, char to, double cost)</param>
        /// <param name="cost_transpose_adjacent">Default: -1. If -1, means the feature is disabled. Transposes are adjacent letters that are inverted. Uses the *True* Damerau-Levenshtein distance variant, which respects the triangle equality. ex: 'ah'/'ha' would return 1x cost_transpose_adjacent instead of 2x cost_substitute.</param>
        /// <param name="max_cost">Default: -1. If -1, will assume no stopping condition. Stops counting once the value is reached and returns that value if so. Returned values may be higher than this, but the main loop will be stopped.</param>
        public static double WeightedLevenshteinDistance(string source, string target, double cost_insert = 1, double cost_delete = 1, Func<char, char, double> cost_substitute = null, double cost_transpose_adjacent = -1, double max_cost = -1) {
            // memory optimisation
            if(target.Length < source.Length) {
                var temp1    = source;
                source       = target;
                target       = temp1;

                var temp2    = cost_insert;
                cost_insert  = cost_delete;
                cost_delete  = temp2;
            }

            var sourceLength = source.Length;
            var targetLength = target.Length;
            int start        = 0;
            var process_max  = max_cost >= 0 && max_cost < int.MaxValue;

            if(sourceLength == 0)
                return targetLength * cost_insert;
            if(process_max) {
                var diff = (targetLength - sourceLength) * cost_insert;
                if(diff >= max_cost)
                    return max_cost;
            }

            // ignore common suffixes
            while(sourceLength > 0 && source[sourceLength - 1] == target[targetLength - 1]) {
                sourceLength--; 
                targetLength--;
            }
            // ignore common prefixes
            while(start < sourceLength && source[start] == target[start])
                start++;
            sourceLength -= start;
            targetLength -= start;
            if(sourceLength == 0)
                return targetLength * cost_delete;

            // todo: make a version with byte/ushort/uint rather than int, since most time we dont need to count that high
            // this would improve cache locality and mem usage
            //var maxCost = (sourceLength + 1) * Math.Max(cost_delete, Math.Max(cost_insert, Math.Max(cost_substitute, cost_transpose_real)));
            //if(maxCost <= byte.MaxValue) {
            //}

            var process_transposes = cost_transpose_adjacent >= 0;
            var prev_row_min       = double.MinValue; // intentionally min

            var curr               = new double[sourceLength + 1];
            var prev               = new double[sourceLength + 1];
            var prev_prev          = !process_transposes ? null : new double[sourceLength + 1];

            if(cost_substitute == null)
                cost_substitute = (from, to) => 1;

            // to transform the first i characters of s into the first 0 characters of t, 
            // we must perform i deletions.
            for(var i = 1; i <= sourceLength; i++)
                prev[i] = i * cost_delete;

            for(var i = 1; i <= targetLength; i++) {
                var current_y = target[i + start - 1];
                curr[0]       = i * cost_insert;
                var row_min   = double.MaxValue;

                for(var j = 1; j <= sourceLength; j++) {
                    var current_x  = source[j + start - 1];
                    var is_same    = current_x == current_y;

                    var insert     = prev[j    ] + cost_insert;
                    var delete     = curr[j - 1] + cost_delete;
                    var substitute = prev[j - 1] + (is_same ? 0 : cost_substitute(current_x, current_y));

                    var cost = Math.Min(insert, Math.Min(delete, substitute));

                    // *True* Damereau-Levenshtein variant
                    if(process_transposes && i > 1 && j > 1 && current_x == target[i + start - 2] && source[j + start - 2] == current_y)
                        cost = Math.Min(cost, prev_prev[j - 2] + (is_same ? 0 : cost_transpose_adjacent)); // i?

                    if(process_max)
                        row_min = Math.Min(row_min, cost);

                    curr[j] = cost;
                }

                if(process_max) {
                    if(row_min >= max_cost && (!process_transposes || prev_row_min >= max_cost))
                        return max_cost;
                    prev_row_min = row_min;
                }

                var swap = prev;
                prev     = curr;
                curr     = swap;

                if(process_transposes) {
                    swap      = prev_prev;
                    prev_prev = curr;
                    curr      = swap;
                }
            }

            return prev[sourceLength];
        }
        /// <summary>
        ///     O(m * n)
        ///     Returns the Levenshtein distance between the 2 strings (0 = perfect match).
        ///     AKA the edit distance (how many edits are needed to modify s to t).
        ///     Satisfies the triangle inequality.
        ///     
        ///     Great for single words comparison, poorly handles multi-word and word substitutions.
        ///     
        ///     If you want a percentage: this()/Math.Max(source_len, target_len).
        /// </summary>
        /// <param name="cost_substitute">Default: 1. Func(char from, char to, double cost)</param>
        /// <param name="cost_transpose_adjacent">Default: -1. If -1, means the feature is disabled. Transposes are adjacent letters that are inverted. Uses the *True* Damerau-Levenshtein distance variant, which respects the triangle equality. ex: 'ah'/'ha' would return 1x cost_transpose_adjacent instead of 2x cost_substitute.</param>
        /// <param name="max_cost">Default: -1. If -1, will assume no stopping condition. Stops counting once the value is reached and returns that value if so. Returned values may be higher than this, but the main loop will be stopped.</param>
        public static double WeightedLevenshteinDistance(string source, int source_index, int source_len, string target, int target_index, int target_len, double cost_insert = 1, double cost_delete = 1, Func<char, char, double> cost_substitute = null, double cost_transpose_adjacent = -1, double max_cost = -1) {
            // memory optimisation
            if(target_len < source_len) {
                var temp1    = source;
                source       = target;
                target       = temp1;

                var temp2    = source_index;
                source_index = target_index;
                target_index = temp2;

                var temp3    = source_len;
                source_len   = target_len;
                target_len   = temp3;

                var temp4    = cost_insert;
                cost_insert  = cost_delete;
                cost_delete  = temp4;
            }

            var process_max  = max_cost >= 0 && max_cost < int.MaxValue;
            if(source_len == 0)
                return target_len * cost_insert;
            if(process_max) {
                var diff = (target_len - source_len) * cost_insert;
                if(diff >= max_cost)
                    return max_cost;
            }

            // ignore common suffixes
            while(source_len > 0 && source[source_len - 1] == target[target_len - 1]) {
                source_len--; 
                target_len--;
            }
            // ignore common prefixes
            int start = 0;
            while(start < source_len && source[start] == target[start])
                start++;
            source_len -= start;
            target_len -= start;
            if(source_len == 0)
                return target_len * cost_delete;
            source_index += start;
            target_index += start;

            var process_transposes = cost_transpose_adjacent >= 0;
            var prev_row_min       = double.MinValue; // intentionally min

            var curr               = new double[source_len + 1];
            var prev               = new double[source_len + 1];
            var prev_prev          = !process_transposes ? null : new double[source_len + 1];

            if(cost_substitute == null)
                cost_substitute = (from, to) => 1;

            // to transform the first i characters of s into the first 0 characters of t, 
            // we must perform i deletions.
            for(var i = 1; i <= source_len; i++)
                prev[i] = i * cost_delete;

            for(var i = 1; i <= target_len; i++) {
                var current_y = target[i + target_index - 1];
                curr[0]       = i * cost_insert;
                var row_min   = double.MaxValue;

                for(var j = 1; j <= source_len; j++) {
                    var current_x  = source[j + source_index - 1];
                    var is_same    = current_x == current_y;

                    var insert     = prev[j    ] + cost_insert;
                    var delete     = curr[j - 1] + cost_delete;
                    var substitute = prev[j - 1] + (is_same ? 0 : cost_substitute(current_x, current_y));

                    var cost = Math.Min(insert, Math.Min(delete, substitute));

                    // *True* Damereau-Levenshtein variant
                    if(process_transposes && i > 1 && j > 1 && current_x == target[i + target_index - 2] && source[j + source_index - 2] == current_y)
                        cost = Math.Min(cost, prev_prev[j - 2] + (is_same ? 0 : cost_transpose_adjacent)); // i?

                    if(process_max)
                        row_min = Math.Min(row_min, cost);

                    curr[j] = cost;
                }

                if(process_max) {
                    if(row_min >= max_cost && (!process_transposes || prev_row_min >= max_cost))
                        return max_cost;
                    prev_row_min = row_min;
                }

                var swap = prev;
                prev     = curr;
                curr     = swap;

                if(process_transposes) {
                    swap      = prev_prev;
                    prev_prev = curr;
                    curr      = swap;
                }
            }

            return prev[source_len];
        }
        #endregion
        #region static VisualAcuitySimilarity()
        // A character visual-similarity matrix
        // table extracted from https://link.springer.com/article/10.3758/s13428-012-0271-4  "A letter visual-similarity matrix for Latin-based alphabets"     (https://static-content.springer.com/esm/art%3A10.3758%2Fs13428-012-0271-4/MediaObjects/13428_2012_271_MOESM1_ESM.xlsx)
        private const string VisualAcuitySimilarityCharacterMatrix_Source = 
@",0,1,2,3,4,5,6,7,8,9,a,b,c,d,e,f,g,h,i,j,k,l,m,n,o,p,q,r,s,t,u,v,w,x,y,z,α,à,á,â,ã,ä,æ,ß,ç,è,é,ê,ë,í,î,ï,ñ,ò,ó,ô,õ,ö,ù,ú,û,ü,œ,A,B,C,D,E,F,G,H,I,J,K,L,M,N,O,P,Q,R,S,T,U,V,W,X,Y,Z,À,Á,Â,Ã,Ä,Æ,Ç,È,É,Ê,Ë,Í,Î,Ï,Ñ,Ò,Ó,Ô,Õ,Ö,Ù,Ú,Û,Ü,Œ
0,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
1,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
2,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
3,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
4,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
5,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
6,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
7,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
8,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
9,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
a,,,,,,,,,,,,2.13,2.50,2.57,3.40,1.06,3.30,1.57,1.16,1.13,1.13,1.10,1.40,1.63,3.13,2.03,2.60,1.43,2.13,1.07,2.40,1.23,1.13,1.10,1.07,1.37,5.37,4.69,5.07,5.00,4.30,4.67,3.80,1.60,1.87,2.57,2.63,2.47,3.13,1.10,1.03,1.20,1.60,2.77,2.94,2.83,2.70,2.42,1.83,1.86,1.80,1.93,2.57,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
b,,,,,,,,,,,2.13,,3.03,5.60,2.27,1.83,3.53,3.70,1.43,1.43,2.13,2.77,1.23,1.77,4.20,5.07,4.67,1.27,1.40,1.63,1.93,1.40,1.10,1.07,1.20,1.13,3.53,2.97,3.03,3.03,2.57,2.80,2.00,3.80,2.93,2.37,2.10,2.30,2.00,1.73,1.37,1.50,1.60,3.93,4.20,3.87,3.17,3.07,1.87,2.10,2.00,1.67,2.03,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
c,,,,,,,,,,,2.50,3.03,,3.57,4.43,1.24,2.47,1.30,1.10,1.13,1.30,1.17,1.20,2.00,5.23,2.60,2.77,1.40,2.43,1.40,2.17,1.53,1.23,1.23,1.29,1.47,4.23,3.73,2.60,3.37,2.90,3.37,2.23,1.52,6.40,4.40,3.77,3.23,3.63,1.19,1.17,1.23,1.43,4.03,4.27,3.93,4.00,3.97,1.63,1.97,1.63,1.67,2.70,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
d,,,,,,,,,,,2.57,5.60,3.57,,2.87,1.43,4.10,2.73,1.73,1.27,1.33,2.30,1.13,1.93,4.90,5.10,5.10,1.33,1.27,1.37,1.83,1.55,1.07,1.23,1.13,1.23,4.17,4.07,3.63,3.90,3.60,3.37,1.73,2.33,2.97,2.20,2.67,2.23,2.17,1.43,1.53,1.20,1.23,3.61,3.27,3.50,2.57,3.30,2.13,2.53,1.73,1.67,2.10,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
e,,,,,,,,,,,3.40,2.27,4.43,2.87,,1.33,2.37,1.23,1.20,1.17,1.20,1.07,1.13,1.73,4.13,2.40,2.43,1.27,2.20,1.20,1.57,1.23,1.30,1.30,1.37,1.33,2.97,2.43,2.83,3.03,2.80,3.20,4.57,1.87,3.20,6.03,6.30,6.33,6.43,1.17,1.30,1.23,1.37,3.07,3.83,3.53,2.80,3.13,1.63,1.43,1.50,1.57,4.30,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
f,,,,,,,,,,,1.06,1.83,1.24,1.43,1.33,,1.40,1.80,4.16,3.67,1.57,4.03,1.33,1.20,1.17,1.63,1.47,3.80,1.27,4.80,1.27,1.23,1.13,1.28,1.40,1.30,1.07,1.13,1.10,1.07,1.20,1.33,1.07,1.83,1.30,1.37,1.30,1.40,1.30,3.43,3.60,3.60,1.43,1.03,1.00,1.13,1.20,1.10,1.10,1.33,1.13,1.13,1.07,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
g,,,,,,,,,,,3.30,3.53,2.47,4.10,2.37,1.40,,1.57,1.53,2.33,1.17,1.30,1.27,1.70,3.47,4.50,5.30,1.27,1.80,1.13,1.73,1.20,1.17,1.20,2.67,1.23,3.40,3.20,3.27,3.03,2.57,2.70,1.73,1.70,4.20,2.10,2.23,2.03,2.13,1.30,1.33,1.30,1.73,3.37,3.47,3.20,3.33,2.53,2.03,1.90,1.63,1.97,2.47,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
h,,,,,,,,,,,1.57,3.70,1.30,2.73,1.23,1.80,1.57,,1.93,1.60,2.77,2.53,2.53,5.53,1.47,2.47,2.17,2.37,1.23,1.97,3.33,1.40,1.30,1.23,1.50,1.20,1.37,1.30,1.43,1.63,1.60,1.43,1.10,2.10,1.43,1.23,1.20,1.50,1.27,1.70,1.73,1.93,4.40,1.60,1.83,1.43,1.57,1.60,2.77,3.10,2.53,3.13,1.07,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
i,,,,,,,,,,,1.16,1.43,1.10,1.73,1.20,4.16,1.53,1.93,,5.17,1.90,6.13,1.63,1.50,1.33,1.60,1.55,2.70,1.07,3.90,1.67,1.67,1.30,1.40,1.50,1.13,1.20,1.57,1.27,1.30,1.33,1.60,1.20,1.30,1.13,1.20,1.23,1.20,1.47,6.63,6.23,6.17,1.40,1.70,1.07,1.23,1.33,1.53,1.70,1.63,1.83,2.33,1.20,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
j,,,,,,,,,,,1.13,1.43,1.13,1.27,1.17,3.67,2.33,1.60,5.17,,1.47,4.67,1.13,1.30,1.00,1.57,1.63,2.30,1.17,3.80,1.30,1.37,1.10,1.26,2.87,1.23,1.07,1.03,1.13,1.07,1.23,1.33,1.00,1.30,1.43,1.13,1.23,1.17,1.13,4.80,4.93,4.60,1.23,1.13,1.10,1.07,1.07,1.07,1.17,1.30,1.32,1.73,1.07,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
k,,,,,,,,,,,1.13,2.13,1.30,1.33,1.20,1.57,1.17,2.77,1.90,1.47,,2.80,1.20,1.53,1.03,1.53,1.47,2.00,1.40,2.13,1.26,1.97,2.07,3.55,2.03,1.50,1.13,1.37,1.17,1.27,1.00,1.20,1.00,2.83,1.30,1.37,1.27,1.33,1.13,1.73,1.97,1.74,1.30,1.20,1.13,1.10,1.10,1.20,1.17,1.37,1.20,1.20,1.00,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
l,,,,,,,,,,,1.10,2.77,1.17,2.30,1.07,4.03,1.30,2.53,6.13,4.67,2.80,,1.07,1.37,1.23,1.80,1.83,3.26,1.10,4.50,1.29,1.37,1.33,1.27,1.81,1.10,1.17,1.10,1.13,1.03,1.30,1.20,1.00,1.97,1.10,1.07,1.00,1.10,1.13,5.90,5.70,5.73,1.23,1.07,1.10,1.07,1.00,1.10,1.33,1.33,1.20,1.40,1.00,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
m,,,,,,,,,,,1.40,1.23,1.20,1.13,1.13,1.33,1.27,2.53,1.63,1.13,1.20,1.07,,4.67,1.30,1.33,1.17,2.23,1.20,1.07,2.27,1.33,3.40,1.10,1.20,1.10,1.20,1.20,1.47,1.43,1.30,1.23,1.80,1.47,1.50,1.27,1.23,1.17,1.28,1.20,1.33,1.53,3.43,1.17,1.20,1.23,1.30,1.20,1.87,2.30,2.17,1.93,2.10,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
n,,,,,,,,,,,1.63,1.77,2.00,1.93,1.73,1.20,1.70,5.53,1.50,1.30,1.53,1.37,4.67,,2.40,1.83,1.90,3.13,1.52,1.10,4.53,1.97,1.61,1.03,1.43,1.57,1.83,1.73,1.67,1.47,2.00,1.73,1.37,1.67,1.83,1.23,1.27,1.57,1.60,1.37,1.87,1.27,6.27,2.10,1.63,1.57,1.60,1.57,4.30,3.67,3.93,3.77,1.43,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
o,,,,,,,,,,,3.13,4.20,5.23,4.90,4.13,1.17,3.47,1.47,1.33,1.00,1.03,1.23,1.30,2.40,,3.60,4.10,1.13,2.27,1.13,2.83,1.27,1.13,1.07,1.17,1.27,5.00,4.17,3.90,3.47,3.87,4.20,2.03,1.70,4.60,3.90,3.27,3.70,3.73,1.03,1.03,1.20,1.93,6.50,6.67,6.57,6.17,6.60,2.87,2.50,2.30,2.70,4.13,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
p,,,,,,,,,,,2.03,5.07,2.60,5.10,2.40,1.63,4.50,2.47,1.60,1.57,1.53,1.80,1.33,1.83,3.60,,5.57,1.77,1.26,1.33,1.80,1.13,1.17,1.10,1.97,1.17,3.13,2.73,2.87,2.33,3.10,2.67,1.73,2.30,3.37,2.03,1.73,1.83,1.97,1.63,1.50,1.43,1.73,3.20,3.03,3.47,3.30,3.03,1.90,1.67,1.74,1.77,2.40,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
q,,,,,,,,,,,2.60,4.67,2.77,5.10,2.43,1.47,5.30,2.17,1.55,1.63,1.47,1.83,1.17,1.90,4.10,5.57,,1.47,1.23,1.80,2.30,1.20,1.03,1.10,2.03,1.17,3.97,3.33,3.30,3.10,3.53,3.70,1.77,1.87,2.80,1.93,1.93,1.80,2.07,1.63,1.20,1.65,1.33,3.30,3.43,3.57,3.07,3.00,2.00,2.13,2.03,1.71,2.20,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
r,,,,,,,,,,,1.43,1.27,1.40,1.33,1.27,3.80,1.27,2.37,2.70,2.30,2.00,3.26,2.23,3.13,1.13,1.77,1.47,,1.60,3.37,1.97,1.67,1.03,1.43,1.27,1.30,1.50,1.20,1.40,1.30,1.23,1.17,1.13,1.40,1.29,1.03,1.27,1.37,1.17,3.17,4.00,2.57,1.93,1.17,1.10,1.17,1.23,1.20,1.30,1.47,1.53,1.43,1.33,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
s,,,,,,,,,,,2.13,1.40,2.43,1.27,2.20,1.27,1.80,1.23,1.07,1.17,1.40,1.10,1.20,1.52,2.27,1.26,1.23,1.60,,1.20,1.33,1.13,1.10,1.63,1.33,2.17,2.03,1.40,1.50,1.50,1.50,1.60,2.07,3.07,2.77,1.83,1.50,1.73,1.80,1.13,1.13,1.30,1.47,1.53,1.50,1.70,1.93,1.83,1.27,1.30,1.13,1.13,1.90,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
t,,,,,,,,,,,1.07,1.63,1.40,1.37,1.20,4.80,1.13,1.97,3.90,3.80,2.13,4.50,1.07,1.10,1.13,1.33,1.80,3.37,1.20,,1.60,1.40,1.00,1.47,1.50,1.27,1.17,1.03,1.07,1.37,1.07,1.13,1.07,1.40,1.10,1.07,1.20,1.33,1.17,4.00,3.93,3.83,1.20,1.13,1.07,1.10,1.17,1.10,1.40,1.20,1.30,1.33,1.00,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
u,,,,,,,,,,,2.40,1.93,2.17,1.83,1.57,1.27,1.73,3.33,1.67,1.30,1.26,1.29,2.27,4.53,2.83,1.80,2.30,1.97,1.33,1.60,,4.93,2.73,1.40,3.13,1.57,2.97,2.33,1.90,1.93,2.30,2.57,1.33,1.37,1.66,1.53,1.87,1.48,1.93,1.83,1.23,1.57,3.93,2.43,2.37,2.27,2.43,2.67,6.40,6.77,6.63,6.33,1.50,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
v,,,,,,,,,,,1.23,1.40,1.53,1.55,1.23,1.23,1.20,1.40,1.67,1.37,1.97,1.37,1.33,1.97,1.27,1.13,1.20,1.67,1.13,1.40,4.93,,5.03,2.63,5.33,1.97,1.47,1.30,1.40,1.33,1.23,1.47,1.17,1.27,1.30,1.23,1.33,1.27,1.30,1.70,2.20,1.67,1.23,1.37,1.30,1.27,1.23,1.23,4.17,4.53,3.97,3.93,1.03,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
w,,,,,,,,,,,1.13,1.10,1.23,1.07,1.30,1.13,1.17,1.30,1.30,1.10,2.07,1.33,3.40,1.61,1.13,1.17,1.03,1.03,1.10,1.00,2.73,5.03,,2.23,2.43,1.67,1.20,1.33,1.27,1.19,1.07,1.03,1.50,1.13,1.23,1.07,1.07,1.07,1.07,1.17,1.17,1.30,1.20,1.06,1.17,1.07,1.43,1.10,2.43,2.30,2.47,2.00,1.47,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
x,,,,,,,,,,,1.10,1.07,1.23,1.23,1.30,1.28,1.20,1.23,1.40,1.26,3.55,1.27,1.10,1.03,1.07,1.10,1.10,1.43,1.63,1.47,1.40,2.63,2.23,,3.10,1.80,1.13,1.13,1.07,1.27,1.20,1.20,1.63,1.13,1.19,1.07,1.33,1.37,1.23,1.30,1.50,1.33,1.17,1.23,1.43,1.20,1.27,1.17,1.77,1.40,1.23,1.57,1.37,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
y,,,,,,,,,,,1.07,1.20,1.29,1.13,1.37,1.40,2.67,1.50,1.50,2.87,2.03,1.81,1.20,1.43,1.17,1.97,2.03,1.27,1.33,1.50,3.13,5.33,2.43,3.10,,1.93,1.13,1.13,1.03,1.10,1.10,1.07,1.07,1.13,1.33,1.13,1.17,1.17,1.27,1.65,1.50,1.40,1.13,1.23,1.07,1.23,1.13,1.03,2.87,2.29,2.77,2.07,1.10,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
z,,,,,,,,,,,1.37,1.13,1.47,1.23,1.33,1.30,1.23,1.20,1.13,1.23,1.50,1.10,1.10,1.57,1.27,1.17,1.17,1.30,2.17,1.27,1.57,1.97,1.67,1.80,1.93,,1.17,1.10,1.13,1.17,1.17,1.17,1.13,1.13,1.23,1.43,1.23,1.40,1.27,1.30,1.17,1.20,1.27,1.27,1.00,1.03,1.30,1.27,1.47,1.30,1.27,1.30,1.23,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
α,,,,,,,,,,,5.37,3.53,4.23,4.17,2.97,1.07,3.40,1.37,1.20,1.07,1.13,1.17,1.20,1.83,5.00,3.13,3.97,1.50,2.03,1.17,2.97,1.47,1.20,1.13,1.13,1.17,,6.63,6.53,6.10,6.00,6.35,2.87,2.13,3.60,2.83,2.77,2.57,2.73,1.30,1.17,1.17,1.53,4.00,4.48,4.00,4.33,3.97,2.70,2.26,2.40,2.70,2.43,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
à,,,,,,,,,,,4.69,2.97,3.73,4.07,2.43,1.13,3.20,1.30,1.57,1.03,1.37,1.10,1.20,1.73,4.17,2.73,3.33,1.20,1.40,1.03,2.33,1.30,1.33,1.13,1.13,1.10,6.63,,6.53,6.33,5.77,6.13,3.13,1.57,2.73,3.80,3.60,3.20,2.20,1.53,1.37,1.40,1.53,5.07,3.90,3.83,3.57,3.90,3.23,3.00,2.37,1.97,2.53,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
á,,,,,,,,,,,5.07,3.03,2.60,3.63,2.83,1.10,3.27,1.43,1.27,1.13,1.17,1.13,1.47,1.67,3.90,2.87,3.30,1.40,1.50,1.07,1.90,1.40,1.27,1.07,1.03,1.13,6.53,6.53,,6.20,6.27,6.20,2.30,1.60,2.90,3.17,4.10,2.67,2.93,1.77,1.27,1.23,1.50,4.13,5.07,4.13,4.07,3.63,3.17,3.43,2.30,2.03,2.57,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
â,,,,,,,,,,,5.00,3.03,3.37,3.90,3.03,1.07,3.03,1.63,1.30,1.07,1.27,1.03,1.43,1.47,3.47,2.33,3.10,1.30,1.50,1.37,1.93,1.33,1.19,1.27,1.10,1.17,6.10,6.33,6.20,,6.27,6.13,2.33,1.37,2.57,2.97,2.37,3.50,2.50,1.17,1.87,1.37,1.93,3.73,4.07,4.77,4.33,3.43,3.03,2.07,3.03,2.30,2.20,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ã,,,,,,,,,,,4.30,2.57,2.90,3.60,2.80,1.20,2.57,1.60,1.33,1.23,1.00,1.30,1.30,2.00,3.87,3.10,3.53,1.23,1.50,1.07,2.30,1.23,1.07,1.20,1.10,1.17,6.00,5.77,6.27,6.27,,6.13,2.60,1.80,3.30,2.83,2.97,2.53,2.83,1.41,1.63,1.27,3.33,3.67,4.03,4.07,4.57,3.63,2.43,2.70,2.93,2.27,1.97,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ä,,,,,,,,,,,4.67,2.80,3.37,3.37,3.20,1.33,2.70,1.43,1.60,1.33,1.20,1.20,1.23,1.73,4.20,2.67,3.70,1.17,1.60,1.13,2.57,1.47,1.03,1.20,1.07,1.17,6.35,6.13,6.20,6.13,6.13,,2.57,1.67,2.97,2.53,2.97,2.47,4.00,1.13,1.27,2.10,2.17,4.03,3.77,3.47,4.13,4.60,2.23,2.33,2.17,3.55,2.33,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
æ,,,,,,,,,,,3.80,2.00,2.23,1.73,4.57,1.07,1.73,1.10,1.20,1.00,1.00,1.00,1.80,1.37,2.03,1.73,1.77,1.13,2.07,1.07,1.33,1.17,1.50,1.63,1.07,1.13,2.87,3.13,2.30,2.33,2.60,2.57,,2.13,2.17,4.03,4.00,4.00,3.03,1.03,1.00,1.03,1.13,1.77,2.37,1.97,1.73,2.13,1.17,1.57,1.23,1.57,5.33,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ß,,,,,,,,,,,1.60,3.80,1.52,2.33,1.87,1.83,1.70,2.10,1.30,1.30,2.83,1.97,1.47,1.67,1.70,2.30,1.87,1.40,3.07,1.40,1.37,1.27,1.13,1.13,1.13,1.13,2.13,1.57,1.60,1.37,1.80,1.67,2.13,,2.20,2.00,1.60,1.57,1.70,1.37,1.45,1.37,1.57,1.47,1.40,1.77,1.80,1.53,1.20,1.37,1.53,1.70,1.60,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ç,,,,,,,,,,,1.87,2.93,6.40,2.97,3.20,1.30,4.20,1.43,1.13,1.43,1.30,1.10,1.50,1.83,4.60,3.37,2.80,1.29,2.77,1.10,1.66,1.30,1.23,1.19,1.33,1.23,3.60,2.73,2.90,2.57,3.30,2.97,2.17,2.20,,3.13,3.10,3.00,3.07,1.20,1.20,1.10,1.50,3.50,3.70,3.93,3.77,3.70,1.63,1.47,1.87,1.67,2.57,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
è,,,,,,,,,,,2.57,2.37,4.40,2.20,6.03,1.37,2.10,1.23,1.20,1.13,1.37,1.07,1.27,1.23,3.90,2.03,1.93,1.03,1.83,1.07,1.53,1.23,1.07,1.07,1.13,1.43,2.83,3.80,3.17,2.97,2.83,2.53,4.03,2.00,3.13,,6.47,6.07,6.53,1.87,1.37,1.23,1.43,4.30,4.53,3.40,3.33,3.30,2.80,1.81,1.87,1.83,4.13,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
é,,,,,,,,,,,2.63,2.10,3.77,2.67,6.30,1.30,2.23,1.20,1.23,1.23,1.27,1.00,1.23,1.27,3.27,1.73,1.93,1.27,1.50,1.20,1.87,1.33,1.07,1.33,1.17,1.23,2.77,3.60,4.10,2.37,2.97,2.97,4.00,1.60,3.10,6.47,,6.27,6.23,1.87,1.27,1.37,1.60,4.17,4.53,3.83,3.10,3.33,2.37,2.80,1.63,1.63,3.87,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ê,,,,,,,,,,,2.47,2.30,3.23,2.23,6.33,1.40,2.03,1.50,1.20,1.17,1.33,1.10,1.17,1.57,3.70,1.83,1.80,1.37,1.73,1.33,1.48,1.27,1.07,1.37,1.17,1.40,2.57,3.20,2.67,3.50,2.53,2.47,4.00,1.57,3.00,6.07,6.27,,6.10,1.33,1.70,1.13,1.87,3.27,3.33,4.13,3.30,3.53,2.07,1.60,2.87,1.83,3.90,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ë,,,,,,,,,,,3.13,2.00,3.63,2.17,6.43,1.30,2.13,1.27,1.47,1.13,1.13,1.13,1.28,1.60,3.73,1.97,2.07,1.17,1.80,1.17,1.93,1.30,1.07,1.23,1.27,1.27,2.73,2.20,2.93,2.50,2.83,4.00,3.03,1.70,3.07,6.53,6.23,6.10,,1.37,1.57,2.40,1.57,3.33,3.63,3.27,3.53,4.23,1.87,1.73,1.80,3.30,4.13,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
í,,,,,,,,,,,1.10,1.73,1.19,1.43,1.17,3.43,1.30,1.70,6.63,4.80,1.73,5.90,1.20,1.37,1.03,1.63,1.63,3.17,1.13,4.00,1.83,1.70,1.17,1.30,1.65,1.30,1.30,1.53,1.77,1.17,1.41,1.13,1.03,1.37,1.20,1.87,1.87,1.33,1.37,,6.47,6.43,1.97,1.80,2.00,1.40,1.07,1.37,2.77,2.87,1.73,1.97,1.17,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
î,,,,,,,,,,,1.03,1.37,1.17,1.53,1.30,3.60,1.33,1.73,6.23,4.93,1.97,5.70,1.33,1.87,1.03,1.50,1.20,4.00,1.13,3.93,1.23,2.20,1.17,1.50,1.50,1.17,1.17,1.37,1.27,1.87,1.63,1.27,1.00,1.45,1.20,1.37,1.27,1.70,1.57,6.47,,6.30,1.97,1.30,1.40,2.23,1.47,1.35,1.70,2.00,2.40,1.77,1.13,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ï,,,,,,,,,,,1.20,1.50,1.23,1.20,1.23,3.60,1.30,1.93,6.17,4.60,1.74,5.73,1.53,1.27,1.20,1.43,1.65,2.57,1.30,3.83,1.57,1.67,1.30,1.33,1.40,1.20,1.17,1.40,1.23,1.37,1.27,2.10,1.03,1.37,1.10,1.23,1.37,1.13,2.40,6.43,6.30,,1.57,1.20,1.43,1.47,1.10,2.70,1.70,1.63,1.57,3.43,1.10,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ñ,,,,,,,,,,,1.60,1.60,1.43,1.23,1.37,1.43,1.73,4.40,1.40,1.23,1.30,1.23,3.43,6.27,1.93,1.73,1.33,1.93,1.47,1.20,3.93,1.23,1.20,1.17,1.13,1.27,1.53,1.53,1.50,1.93,3.33,2.17,1.13,1.57,1.50,1.43,1.60,1.87,1.57,1.97,1.97,1.57,,1.73,1.47,2.30,3.70,1.67,3.30,4.03,4.17,3.60,1.17,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ò,,,,,,,,,,,2.77,3.93,4.03,3.61,3.07,1.03,3.37,1.60,1.70,1.13,1.20,1.07,1.17,2.10,6.50,3.20,3.30,1.17,1.53,1.13,2.43,1.37,1.06,1.23,1.23,1.27,4.00,5.07,4.13,3.73,3.67,4.03,1.77,1.47,3.50,4.30,4.17,3.27,3.33,1.80,1.30,1.20,1.73,,6.67,6.53,6.13,6.47,3.43,3.10,2.87,2.33,3.77,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ó,,,,,,,,,,,2.94,4.20,4.27,3.27,3.83,1.00,3.47,1.83,1.07,1.10,1.13,1.10,1.20,1.63,6.67,3.03,3.43,1.10,1.50,1.07,2.37,1.30,1.17,1.43,1.07,1.00,4.48,3.90,5.07,4.07,4.03,3.77,2.37,1.40,3.70,4.53,4.53,3.33,3.63,2.00,1.40,1.43,1.47,6.67,,6.27,6.33,6.27,3.53,3.70,2.63,2.70,4.37,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ô,,,,,,,,,,,2.83,3.87,3.93,3.50,3.53,1.13,3.20,1.43,1.23,1.07,1.10,1.07,1.23,1.57,6.57,3.47,3.57,1.17,1.70,1.10,2.27,1.27,1.07,1.20,1.23,1.03,4.00,3.83,4.13,4.77,4.07,3.47,1.97,1.77,3.93,3.40,3.83,4.13,3.27,1.40,2.23,1.47,2.30,6.53,6.27,,6.10,6.23,2.73,2.33,3.83,2.47,4.23,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
õ,,,,,,,,,,,2.70,3.17,4.00,2.57,2.80,1.20,3.33,1.57,1.33,1.07,1.10,1.00,1.30,1.60,6.17,3.30,3.07,1.23,1.93,1.17,2.43,1.23,1.43,1.27,1.13,1.30,4.33,3.57,4.07,4.33,4.57,4.13,1.73,1.80,3.77,3.33,3.10,3.30,3.53,1.07,1.47,1.10,3.70,6.13,6.33,6.10,,5.93,2.47,2.40,3.10,2.87,3.03,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ö,,,,,,,,,,,2.42,3.07,3.97,3.30,3.13,1.10,2.53,1.60,1.53,1.07,1.20,1.10,1.20,1.57,6.60,3.03,3.00,1.20,1.83,1.10,2.67,1.23,1.10,1.17,1.03,1.27,3.97,3.90,3.63,3.43,3.63,4.60,2.13,1.53,3.70,3.30,3.33,3.53,4.23,1.37,1.35,2.70,1.67,6.47,6.27,6.23,5.93,,2.60,2.73,2.80,3.97,3.63,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ù,,,,,,,,,,,1.83,1.87,1.63,2.13,1.63,1.10,2.03,2.77,1.70,1.17,1.17,1.33,1.87,4.30,2.87,1.90,2.00,1.30,1.27,1.40,6.40,4.17,2.43,1.77,2.87,1.47,2.70,3.23,3.17,3.03,2.43,2.23,1.17,1.20,1.63,2.80,2.37,2.07,1.87,2.77,1.70,1.70,3.30,3.43,3.53,2.73,2.47,2.60,,6.27,6.50,6.27,1.47,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ú,,,,,,,,,,,1.86,2.10,1.97,2.53,1.43,1.33,1.90,3.10,1.63,1.30,1.37,1.33,2.30,3.67,2.50,1.67,2.13,1.47,1.30,1.20,6.77,4.53,2.30,1.40,2.29,1.30,2.26,3.00,3.43,2.07,2.70,2.33,1.57,1.37,1.47,1.81,2.80,1.60,1.73,2.87,2.00,1.63,4.03,3.10,3.70,2.33,2.40,2.73,6.27,,6.60,6.43,1.47,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
û,,,,,,,,,,,1.80,2.00,1.63,1.73,1.50,1.13,1.63,2.53,1.83,1.32,1.20,1.20,2.17,3.93,2.30,1.74,2.03,1.53,1.13,1.30,6.63,3.97,2.47,1.23,2.77,1.27,2.40,2.37,2.30,3.03,2.93,2.17,1.23,1.53,1.87,1.87,1.63,2.87,1.80,1.73,2.40,1.57,4.17,2.87,2.63,3.83,3.10,2.80,6.50,6.60,,5.97,1.33,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
ü,,,,,,,,,,,1.93,1.67,1.67,1.67,1.57,1.13,1.97,3.13,2.33,1.73,1.20,1.40,1.93,3.77,2.70,1.77,1.71,1.43,1.13,1.33,6.33,3.93,2.00,1.57,2.07,1.30,2.70,1.97,2.03,2.30,2.27,3.55,1.57,1.70,1.67,1.83,1.63,1.83,3.30,1.97,1.77,3.43,3.60,2.33,2.70,2.47,2.87,3.97,6.27,6.43,5.97,,1.23,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
œ,,,,,,,,,,,2.57,2.03,2.70,2.10,4.30,1.07,2.47,1.07,1.20,1.07,1.00,1.00,2.10,1.43,4.13,2.40,2.20,1.33,1.90,1.00,1.50,1.03,1.47,1.37,1.10,1.23,2.43,2.53,2.57,2.20,1.97,2.33,5.33,1.60,2.57,4.13,3.87,3.90,4.13,1.17,1.13,1.10,1.17,3.77,4.37,4.23,3.03,3.63,1.47,1.47,1.33,1.23,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,
A,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.50,1.17,1.57,1.50,1.90,1.60,2.63,1.33,1.40,2.03,1.50,2.40,2.20,1.43,2.13,1.20,2.47,1.43,1.33,1.43,3.97,2.50,1.83,2.03,1.87,6.50,6.67,6.43,6.57,6.67,3.77,1.20,1.63,1.67,1.53,1.63,1.30,1.53,1.73,1.77,1.23,1.33,1.37,1.27,1.13,1.43,1.23,1.43,1.23,1.47
B,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.50,,1.53,4.80,3.50,2.90,2.73,1.87,1.57,1.30,2.33,1.83,1.37,1.43,2.47,4.67,2.03,4.60,2.73,1.70,2.13,1.13,1.03,1.17,1.13,1.23,1.20,1.20,1.67,1.63,1.47,2.33,1.70,3.07,3.03,3.47,2.97,1.43,1.50,1.57,1.33,2.20,2.67,2.17,1.93,2.17,1.50,1.93,1.93,2.07,3.00
C,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.17,1.53,,3.33,1.77,1.45,5.30,1.30,1.13,1.83,1.53,1.80,1.20,1.10,5.23,2.10,4.43,1.67,2.43,1.13,2.77,1.50,1.10,1.33,1.10,1.13,1.57,1.17,1.06,1.17,1.50,1.13,6.60,1.60,1.67,1.90,1.43,1.10,1.30,1.23,1.07,4.77,5.00,4.90,4.53,4.17,2.30,2.53,2.00,2.47,3.03
D,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.57,4.80,3.33,,1.73,1.90,2.87,1.70,1.60,1.77,1.60,2.23,1.20,1.43,4.27,3.60,3.90,2.80,1.77,1.61,3.03,1.33,1.24,1.13,1.17,1.30,1.30,1.57,1.33,1.71,1.27,1.93,3.30,2.20,1.80,1.87,1.87,1.93,1.73,1.66,1.27,4.30,3.97,4.27,3.97,3.43,2.63,2.27,2.87,2.43,2.80
E,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.50,3.50,1.77,1.73,,5.20,1.70,2.90,1.80,1.30,2.17,3.50,1.47,2.07,1.17,2.30,1.10,2.03,1.73,2.33,1.27,1.33,2.17,1.37,1.40,1.70,1.50,1.20,1.40,1.27,1.50,4.90,1.50,6.37,6.53,6.53,6.60,1.53,1.83,1.93,1.77,1.23,1.13,1.27,1.20,1.27,1.33,1.43,1.47,1.23,4.67
F,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.90,2.90,1.45,1.90,5.20,,1.40,2.61,2.30,1.83,2.33,2.93,1.50,1.60,1.23,4.43,1.17,3.27,1.53,3.07,1.33,1.30,1.40,1.53,1.70,2.17,1.63,2.00,1.50,1.47,1.93,3.10,1.43,4.47,4.73,4.40,4.37,2.27,2.53,2.52,1.37,1.13,1.13,1.13,1.17,1.37,1.33,1.23,1.13,1.03,3.17
G,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.60,2.73,5.30,2.87,1.70,1.40,,1.30,1.07,1.53,1.47,1.37,1.10,1.10,4.33,1.73,4.10,1.97,2.17,1.37,2.43,1.13,1.23,1.20,1.10,1.37,1.10,1.37,1.26,1.27,1.30,1.50,4.57,2.07,1.57,1.77,1.83,1.27,1.30,1.07,1.32,4.20,3.67,4.13,4.10,4.26,2.30,1.80,1.80,1.80,2.87
H,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,2.63,1.87,1.30,1.70,2.90,2.61,1.30,,2.47,1.47,2.90,2.26,2.63,3.67,1.37,1.90,1.47,2.27,1.53,2.47,2.37,1.47,1.50,1.63,1.97,1.43,1.83,1.93,2.17,2.03,1.73,2.30,1.17,2.57,2.30,2.03,2.60,2.20,2.10,1.60,2.87,1.20,1.33,1.13,1.10,1.13,1.93,1.93,2.53,2.03,1.63
I,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.33,1.57,1.13,1.60,1.80,2.30,1.07,2.47,,4.10,2.13,3.53,1.52,2.00,1.07,1.83,1.27,1.70,1.03,4.20,2.03,1.87,1.47,1.80,2.70,1.40,1.47,1.33,1.37,1.27,1.30,1.80,1.10,1.71,1.93,1.73,1.83,6.27,6.07,6.10,1.70,1.30,1.10,1.20,1.07,1.03,1.58,1.67,1.50,1.40,1.30
J,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.40,1.30,1.83,1.77,1.30,1.83,1.53,1.47,4.10,,1.37,4.00,1.20,1.70,1.43,1.77,1.40,1.40,1.67,2.77,4.23,2.00,1.53,1.17,1.90,1.20,1.20,1.27,1.30,1.37,1.20,1.23,1.43,1.13,1.30,1.23,1.37,4.10,3.87,3.90,1.27,1.33,1.27,1.37,1.33,1.43,2.90,2.77,2.33,2.63,1.43
K,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,2.03,2.33,1.53,1.60,2.17,2.33,1.47,2.90,2.13,1.37,,2.00,2.13,2.53,1.23,1.57,1.23,3.30,1.60,2.00,1.30,2.03,2.40,4.20,3.23,1.47,1.90,2.07,1.70,1.93,1.80,2.47,1.40,1.70,1.80,1.70,1.70,2.07,2.13,1.93,2.30,1.10,1.27,1.13,1.07,1.20,1.57,1.27,1.27,1.17,1.53
L,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.50,1.83,1.80,2.23,3.50,2.93,1.37,2.26,3.53,4.00,2.00,,1.53,1.60,1.23,1.87,1.28,1.57,1.10,3.20,1.90,2.00,1.20,1.43,2.17,2.30,1.50,1.27,1.23,1.40,1.30,2.30,1.33,2.50,3.13,3.07,2.73,3.97,3.67,3.30,1.43,1.10,1.37,1.10,1.20,1.17,1.53,1.81,2.10,2.00,1.73
M,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,2.40,1.37,1.20,1.20,1.47,1.50,1.10,2.63,1.52,1.20,2.13,1.53,,4.73,1.30,1.33,1.30,1.29,1.17,1.58,1.60,3.00,5.27,2.07,2.55,2.03,1.87,2.03,1.87,2.00,1.87,2.63,1.13,2.00,1.93,1.70,2.10,1.63,1.80,1.90,3.50,1.10,1.10,1.20,1.20,1.10,1.43,1.33,1.57,1.33,1.50
N,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,2.20,1.43,1.10,1.43,2.07,1.60,1.10,3.67,2.00,1.70,2.53,1.60,4.73,,1.30,1.57,1.10,1.73,1.53,1.73,1.63,3.37,4.07,1.63,2.07,3.47,2.17,2.07,2.00,2.10,1.83,1.73,1.10,1.33,1.53,1.79,1.47,2.03,2.37,1.80,6.47,1.17,1.07,1.00,1.37,1.10,1.63,1.60,1.40,1.67,1.33
O,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.43,2.47,5.23,4.27,1.17,1.23,4.33,1.37,1.07,1.43,1.23,1.23,1.30,1.30,,2.60,6.00,2.07,2.23,1.20,4.00,1.13,1.20,1.27,1.23,1.10,1.43,1.13,1.07,1.50,1.23,1.10,4.73,1.07,1.30,1.17,1.03,1.03,1.00,1.13,1.16,6.57,6.43,6.43,6.53,6.57,3.33,3.60,3.03,3.20,4.10
P,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,2.13,4.67,2.10,3.60,2.30,4.43,1.73,1.90,1.83,1.77,1.57,1.87,1.33,1.57,2.60,,2.07,5.27,1.83,2.97,1.53,1.37,1.27,1.20,1.80,1.55,1.77,1.47,1.63,1.90,1.60,1.83,1.50,1.87,1.97,2.03,2.07,2.93,1.93,2.13,1.30,2.13,2.27,1.97,1.87,2.23,1.50,1.47,1.30,1.30,1.57
Q,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.20,2.03,4.43,3.90,1.10,1.17,4.10,1.47,1.27,1.40,1.23,1.28,1.30,1.10,6.00,2.07,,1.68,1.60,1.10,3.00,1.20,1.30,1.10,1.33,1.10,1.17,1.23,1.10,1.37,1.23,1.27,4.73,1.13,1.27,1.27,1.30,1.27,1.13,1.10,1.30,5.45,5.70,5.90,5.97,5.80,2.60,2.20,2.43,2.70,3.53
R,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,2.47,4.60,1.67,2.80,2.03,3.27,1.97,2.27,1.70,1.40,3.30,1.57,1.29,1.73,2.07,5.27,1.68,,2.27,1.58,1.37,1.13,1.50,1.37,1.30,1.37,2.03,2.07,2.03,1.60,2.20,2.30,1.33,1.83,2.20,1.80,1.73,1.83,1.53,1.23,1.33,1.60,1.30,1.57,1.70,1.30,1.34,1.27,1.20,1.33,1.97
S,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.43,2.73,2.43,1.77,1.73,1.53,2.17,1.53,1.03,1.67,1.60,1.10,1.17,1.53,2.23,1.83,1.60,2.27,,1.40,1.60,1.06,1.13,1.37,1.07,2.30,1.37,1.07,1.23,1.43,1.23,1.53,2.53,1.70,1.57,1.60,1.40,1.07,1.13,1.13,1.33,1.90,1.97,1.80,1.97,2.00,1.61,1.40,1.53,1.53,1.83
T,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.33,1.70,1.13,1.61,2.33,3.07,1.37,2.47,4.20,2.77,2.00,3.20,1.58,1.73,1.20,2.97,1.10,1.58,1.40,,1.40,1.43,1.20,1.40,2.50,2.33,1.55,1.50,1.37,1.30,1.40,2.20,1.23,2.20,2.20,1.71,1.83,3.57,4.27,4.17,1.37,1.03,1.37,1.03,1.23,1.07,1.17,1.20,1.23,1.37,1.23
U,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.43,2.13,2.77,3.03,1.27,1.33,2.43,2.37,2.03,4.23,1.30,1.90,1.60,1.63,4.00,1.53,3.00,1.37,1.60,1.40,,5.23,2.70,1.43,2.60,1.40,1.40,1.13,1.37,1.27,1.41,1.37,2.83,1.20,1.20,1.27,1.17,1.74,1.93,1.77,1.67,3.23,3.00,3.03,3.20,3.23,6.63,6.47,6.27,6.50,1.23
V,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,3.97,1.13,1.50,1.33,1.33,1.30,1.13,1.47,1.87,2.00,2.03,2.00,3.00,3.37,1.13,1.37,1.20,1.13,1.06,1.43,5.23,,4.72,2.67,4.63,2.10,3.60,4.07,3.20,2.67,3.93,1.73,1.43,1.40,1.23,1.10,1.10,1.73,2.03,1.87,2.50,1.37,1.43,1.43,1.17,1.20,4.77,4.33,3.83,3.53,1.07
W,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,2.50,1.03,1.10,1.24,2.17,1.40,1.23,1.50,1.47,1.53,2.40,1.20,5.27,4.07,1.20,1.27,1.30,1.50,1.13,1.20,2.70,4.72,,2.58,2.43,1.90,2.00,2.00,2.73,2.03,2.20,2.50,1.10,1.60,1.33,1.67,1.50,1.40,1.53,1.17,2.73,1.07,1.13,1.13,1.10,1.03,2.45,2.27,2.40,2.30,1.57
X,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.83,1.17,1.33,1.13,1.37,1.53,1.20,1.63,1.80,1.17,4.20,1.43,2.07,1.63,1.27,1.20,1.10,1.37,1.37,1.40,1.43,2.67,2.58,,3.77,2.17,1.87,1.57,1.57,1.20,1.57,1.87,1.23,1.30,1.43,1.53,1.07,1.63,1.70,1.33,1.30,1.16,1.20,1.03,1.26,1.16,1.26,1.30,1.10,1.33,1.53
Y,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,2.03,1.13,1.10,1.17,1.40,1.70,1.10,1.97,2.70,1.90,3.23,2.17,2.55,2.07,1.23,1.80,1.33,1.30,1.07,2.50,2.60,4.63,2.43,3.77,,2.00,2.23,1.93,2.47,1.93,1.80,1.43,1.17,1.13,1.27,1.35,1.33,2.61,2.53,2.48,1.50,1.37,1.10,1.37,1.07,1.10,2.40,2.20,2.07,2.13,1.13
Z,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.87,1.23,1.13,1.30,1.70,2.17,1.37,1.43,1.40,1.20,1.47,2.30,2.03,3.47,1.10,1.55,1.10,1.37,2.30,2.33,1.40,2.10,1.90,2.17,2.00,,1.37,1.40,1.47,1.45,1.40,1.80,1.53,1.73,1.67,1.83,2.20,1.37,1.23,1.10,2.77,1.20,1.17,1.03,1.23,1.13,1.13,1.13,1.07,1.20,1.14
À,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,6.50,1.20,1.57,1.30,1.50,1.63,1.10,1.83,1.47,1.20,1.90,1.50,1.87,2.17,1.43,1.77,1.17,2.03,1.37,1.55,1.40,3.60,2.00,1.87,2.23,1.37,,6.70,6.53,6.50,6.57,3.87,1.10,2.43,2.07,1.90,1.47,2.23,1.80,1.50,1.53,1.87,1.40,1.48,1.40,1.27,2.60,1.83,1.58,1.50,1.17
Á,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,6.67,1.20,1.17,1.57,1.20,2.00,1.37,1.93,1.33,1.27,2.07,1.27,2.03,2.07,1.13,1.47,1.23,2.07,1.07,1.50,1.13,4.07,2.00,1.57,1.93,1.40,6.70,,6.40,6.40,6.47,3.83,1.33,2.13,2.13,1.66,1.30,2.07,1.62,1.97,1.77,1.70,2.03,1.47,1.30,1.40,1.97,2.20,1.70,1.30,1.50
Â,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,6.43,1.67,1.06,1.33,1.40,1.50,1.26,2.17,1.37,1.30,1.70,1.23,1.87,2.00,1.07,1.63,1.10,2.03,1.23,1.37,1.37,3.20,2.73,1.57,2.47,1.47,6.53,6.40,,6.53,6.27,4.40,1.10,2.03,1.47,2.40,1.53,1.97,2.23,1.60,2.33,1.50,1.47,2.03,1.57,1.23,1.83,1.90,2.37,1.73,1.23
Ã,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,6.57,1.63,1.17,1.71,1.27,1.47,1.27,2.03,1.27,1.37,1.93,1.40,2.00,2.10,1.50,1.90,1.37,1.60,1.43,1.30,1.27,2.67,2.03,1.20,1.93,1.45,6.50,6.40,6.53,,6.55,4.57,1.50,1.47,1.67,1.97,1.87,1.70,1.63,1.40,3.33,1.23,1.27,1.97,2.47,1.30,1.53,1.63,1.90,1.40,1.17
Ä,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,6.67,1.47,1.50,1.27,1.50,1.93,1.30,1.73,1.30,1.20,1.80,1.30,1.87,1.83,1.23,1.60,1.23,2.20,1.23,1.40,1.41,3.93,2.20,1.57,1.80,1.40,6.57,6.47,6.27,6.55,,3.87,1.17,1.40,1.53,1.70,2.37,1.50,1.67,2.83,1.87,1.27,1.53,1.23,1.20,2.43,1.33,1.57,1.63,2.57,1.43
Æ,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,3.77,2.33,1.13,1.93,4.90,3.10,1.50,2.30,1.80,1.23,2.47,2.30,2.63,1.73,1.10,1.83,1.27,2.30,1.53,2.20,1.37,1.73,2.50,1.87,1.43,1.80,3.87,3.83,4.40,4.57,3.87,,1.20,4.63,4.80,4.70,3.87,1.77,1.37,1.23,1.55,1.13,1.20,1.17,1.30,1.07,1.13,1.03,1.33,1.23,4.53
Ç,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.20,1.70,6.60,3.30,1.50,1.43,4.57,1.17,1.10,1.43,1.40,1.33,1.13,1.10,4.73,1.50,4.73,1.33,2.53,1.23,2.83,1.43,1.10,1.23,1.17,1.53,1.10,1.33,1.10,1.50,1.17,1.20,,1.40,1.77,1.67,1.55,1.13,1.07,1.10,1.53,4.27,4.47,4.37,4.70,5.03,2.00,2.10,2.37,1.73,2.83
È,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.63,3.07,1.60,2.20,6.37,4.47,2.07,2.57,1.71,1.13,1.70,2.50,2.00,1.33,1.07,1.87,1.13,1.83,1.70,2.20,1.20,1.40,1.60,1.30,1.13,1.73,2.43,2.13,2.03,1.47,1.40,4.63,1.40,,6.47,6.60,6.33,1.83,1.87,2.00,1.53,1.93,1.53,1.63,1.17,1.23,1.93,1.87,1.37,1.70,4.13
É,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.67,3.03,1.67,1.80,6.53,4.73,1.57,2.30,1.93,1.30,1.80,3.13,1.93,1.53,1.30,1.97,1.27,2.20,1.57,2.20,1.20,1.23,1.33,1.43,1.27,1.67,2.07,2.13,1.47,1.67,1.53,4.80,1.77,6.47,,6.40,6.27,2.60,2.00,1.53,1.53,1.70,1.77,1.40,1.23,1.50,1.87,1.93,1.77,1.30,4.17
Ê,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.53,3.47,1.90,1.87,6.53,4.40,1.77,2.03,1.73,1.23,1.70,3.07,1.70,1.79,1.17,2.03,1.27,1.80,1.60,1.71,1.27,1.10,1.67,1.53,1.35,1.83,1.90,1.66,2.40,1.97,1.70,4.70,1.67,6.60,6.40,,6.57,1.93,2.23,1.83,1.90,1.47,1.40,2.17,1.67,1.50,1.67,1.37,2.20,1.30,4.30
Ë,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.63,2.97,1.43,1.87,6.60,4.37,1.83,2.60,1.83,1.37,1.70,2.73,2.10,1.47,1.03,2.07,1.30,1.73,1.40,1.83,1.17,1.10,1.50,1.07,1.33,2.20,1.47,1.30,1.53,1.87,2.37,3.87,1.55,6.33,6.27,6.57,,1.90,1.53,2.83,1.58,1.20,1.33,1.43,1.17,2.90,1.57,1.47,1.67,2.80,4.13
Í,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.30,1.43,1.10,1.93,1.53,2.27,1.27,2.20,6.27,4.10,2.07,3.97,1.63,2.03,1.03,2.93,1.27,1.83,1.07,3.57,1.74,1.73,1.40,1.63,2.61,1.37,2.23,2.07,1.97,1.70,1.50,1.77,1.13,1.83,2.60,1.93,1.90,,6.67,6.20,1.93,1.57,1.60,1.33,1.30,1.13,2.27,2.20,1.70,1.87,1.30
Î,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.53,1.50,1.30,1.73,1.83,2.53,1.30,2.10,6.07,3.87,2.13,3.67,1.80,2.37,1.00,1.93,1.13,1.53,1.13,4.27,1.93,2.03,1.53,1.70,2.53,1.23,1.80,1.62,2.23,1.63,1.67,1.37,1.07,1.87,2.00,2.23,1.53,6.67,,6.17,1.93,1.30,1.30,1.87,1.33,1.17,1.93,1.83,2.43,1.53,1.10
Ï,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.73,1.57,1.23,1.66,1.93,2.52,1.07,1.60,6.10,3.90,1.93,3.30,1.90,1.80,1.13,2.13,1.10,1.23,1.13,4.17,1.77,1.87,1.17,1.33,2.48,1.10,1.50,1.97,1.60,1.40,2.83,1.23,1.10,2.00,1.53,1.83,2.83,6.20,6.17,,1.83,1.10,1.20,1.13,1.10,2.13,1.50,1.50,1.60,2.87,1.10
Ñ,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.77,1.33,1.07,1.27,1.77,1.37,1.32,2.87,1.70,1.27,2.30,1.43,3.50,6.47,1.16,1.30,1.30,1.33,1.33,1.37,1.67,2.50,2.73,1.30,1.50,2.77,1.53,1.77,2.33,3.33,1.87,1.55,1.53,1.53,1.53,1.90,1.58,1.93,1.93,1.83,,1.20,1.07,1.33,2.37,1.27,2.00,1.63,2.00,2.03,1.23
Ò,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.23,2.20,4.77,4.30,1.23,1.13,4.20,1.20,1.30,1.33,1.10,1.10,1.10,1.17,6.57,2.13,5.45,1.60,1.90,1.03,3.23,1.37,1.07,1.16,1.37,1.20,1.87,1.70,1.50,1.23,1.27,1.13,4.27,1.93,1.70,1.47,1.20,1.57,1.30,1.10,1.20,,6.67,6.50,6.40,6.30,4.20,3.31,3.33,3.50,4.27
Ó,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.33,2.67,5.00,3.97,1.13,1.13,3.67,1.33,1.10,1.27,1.27,1.37,1.10,1.07,6.43,2.27,5.70,1.30,1.97,1.37,3.00,1.43,1.13,1.20,1.10,1.17,1.40,2.03,1.47,1.27,1.53,1.20,4.47,1.53,1.77,1.40,1.33,1.60,1.30,1.20,1.07,6.67,,6.58,6.45,6.58,3.57,4.20,3.20,2.90,4.33
Ô,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.37,2.17,4.90,4.27,1.27,1.13,4.13,1.13,1.20,1.37,1.13,1.10,1.20,1.00,6.43,1.97,5.90,1.57,1.80,1.03,3.03,1.43,1.13,1.03,1.37,1.03,1.48,1.47,2.03,1.97,1.23,1.17,4.37,1.63,1.40,2.17,1.43,1.33,1.87,1.13,1.33,6.50,6.58,,6.37,6.47,3.20,3.33,4.27,3.23,4.07
Õ,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.27,1.93,4.53,3.97,1.20,1.17,4.10,1.10,1.07,1.33,1.07,1.20,1.20,1.37,6.53,1.87,5.97,1.70,1.97,1.23,3.20,1.17,1.10,1.26,1.07,1.23,1.40,1.30,1.57,2.47,1.20,1.30,4.70,1.17,1.23,1.67,1.17,1.30,1.33,1.10,2.37,6.40,6.45,6.37,,6.07,2.93,3.27,3.90,2.97,4.00
Ö,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.13,2.17,4.17,3.43,1.27,1.37,4.26,1.13,1.03,1.43,1.20,1.17,1.10,1.10,6.57,2.23,5.80,1.30,2.00,1.07,3.23,1.20,1.03,1.16,1.10,1.13,1.27,1.40,1.23,1.30,2.43,1.07,5.03,1.23,1.50,1.50,2.90,1.13,1.17,2.13,1.27,6.30,6.58,6.47,6.07,,3.33,2.70,3.47,4.37,3.53
Ù,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.43,1.50,2.30,2.63,1.33,1.33,2.30,1.93,1.58,2.90,1.57,1.53,1.43,1.63,3.33,1.50,2.60,1.34,1.61,1.17,6.63,4.77,2.45,1.26,2.40,1.13,2.60,1.97,1.83,1.53,1.33,1.13,2.00,1.93,1.87,1.67,1.57,2.27,1.93,1.50,2.00,4.20,3.57,3.20,2.93,3.33,,6.63,6.70,6.27,1.30
Ú,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.23,1.93,2.53,2.27,1.43,1.23,1.80,1.93,1.67,2.77,1.27,1.81,1.33,1.60,3.60,1.47,2.20,1.27,1.40,1.20,6.47,4.33,2.27,1.30,2.20,1.13,1.83,2.20,1.90,1.63,1.57,1.03,2.10,1.87,1.93,1.37,1.47,2.20,1.83,1.50,1.63,3.31,4.20,3.33,3.27,2.70,6.63,,6.30,6.60,1.52
Û,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.43,1.93,2.00,2.87,1.47,1.13,1.80,2.53,1.50,2.33,1.27,2.10,1.57,1.40,3.03,1.30,2.43,1.20,1.53,1.23,6.27,3.83,2.40,1.10,2.07,1.07,1.58,1.70,2.37,1.90,1.63,1.33,2.37,1.37,1.77,2.20,1.67,1.70,2.43,1.60,2.00,3.33,3.20,4.27,3.90,3.47,6.70,6.30,,6.40,1.60
Ü,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.23,2.07,2.47,2.43,1.23,1.03,1.80,2.03,1.40,2.63,1.17,2.00,1.33,1.67,3.20,1.30,2.70,1.33,1.53,1.37,6.50,3.53,2.30,1.33,2.13,1.20,1.50,1.30,1.73,1.40,2.57,1.23,1.73,1.70,1.30,1.30,2.80,1.87,1.53,2.87,2.03,3.50,2.90,3.23,2.97,4.37,6.27,6.60,6.40,,1.47
Œ,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,1.47,3.00,3.03,2.80,4.67,3.17,2.87,1.63,1.30,1.43,1.53,1.73,1.50,1.33,4.10,1.57,3.53,1.97,1.83,1.23,1.23,1.07,1.57,1.53,1.13,1.14,1.17,1.50,1.23,1.17,1.43,4.53,2.83,4.13,4.17,4.30,4.13,1.30,1.10,1.10,1.23,4.27,4.33,4.07,4.00,3.53,1.30,1.52,1.60,1.47,";
        private static Dictionary<(char, char), double> VisualAcuitySimilarityCharacterMap = null; // char[2] ordered

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static int VisualAcuitySimilarity(string s1, string s2) {
        //    VisualAcuitySimilarity(s1[0], s2[0]);
        //
        //    return 0;
        //}
        /// <summary>
        ///     O(1)   (hash lookup)
        ///     Returns 1 if perfect match, 0 if worst match.
        ///     Returns null if similarity is not known.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double? VisualAcuitySimilarity(char c1, char c2) {
            // ranks closer similarly looking characters

            // uses a weird font, but could maybe be of use
            // https://link.springer.com/article/10.3758%2FBF03210580
            // An upper- and lowercase alphabetic similarity matrix, with derived generation similarity values
            // https://link.springer.com/content/pdf/10.3758%2FBF03210580.pdf
            //
            // also see for potential ranking hints https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0035829
            // Learning to Identify Near-Acuity Letters, either with or without Flankers, Results in Improved Letter Size and Spacing Limits in Adults with Amblyopia

            if(c1 == c2)
                return 1;

            // lazy-loaded the cheap way
            if(VisualAcuitySimilarityCharacterMap == null)
                LoadVisualAcuitySimilarityCharacterMap();
            
            (char, char) key;
            if(c1 < c2)
                key = (c1, c2);
            else 
                key = (c2, c1);

            if(VisualAcuitySimilarityCharacterMap.TryGetValue(key, out var ranking))
                return ranking;

            return null;
        }
        private static void LoadVisualAcuitySimilarityCharacterMap() {
            var lines = VisualAcuitySimilarityCharacterMatrix_Source
                .Split(new[] {'\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var characters = lines[0].Replace(",", string.Empty).ToCharArray();
            double[][] matrix = new double[characters.Length][];
            var emptyValues = new HashSet<(char, char)>((characters.Length - 1) / 2);
            for(int i = 1; i < lines.Length; i++) {
                var line      = lines[i].Split(',');
                var vector    = new double[characters.Length];
                matrix[i - 1] = vector;
                if(line[0][0] != characters[i - 1])
                    throw new FormatException($"{nameof(VisualAcuitySimilarityCharacterMatrix_Source)} is invalid. Columns headers dont match the row headers.");
                for(int c = 1; c < line.Length; c++) {
                    if(!string.IsNullOrEmpty(line[c]))
                        vector[c - 1] = double.Parse(line[c], System.Globalization.CultureInfo.InvariantCulture);
                    else
                        emptyValues.Add((characters[c - 1], characters[i - 1]));
                }
            }
            
            MeanCentering(matrix);
            
            // rescale 1 -> SCALE_FACTOR to reserve '1' to be exact matches
            const double SCALE_FACTOR = 0.95;
            for(int i = 0; i < matrix.Length; i++) {
                var vector = matrix[i];
                for(int j = 0; j < vector.Length; j++)
                    vector[j] *= SCALE_FACTOR;
            }

            var dict = new Dictionary<(char, char), double>((characters.Length * characters.Length - emptyValues.Count) / 2);
            
            for(int i = 0; i < characters.Length; i++) {
                for(int j = 0; j < i; j++) {
                    var c1 = characters[i];
                    var c2 = characters[j];
                    (char, char) key;
                    if(c1 < c2)
                        key = (c1, c2);
                    else 
                        key = (c2, c1);
                    if(!emptyValues.Contains(key))
                        dict.Add(key, matrix[i][j]);
                }
            }

            VisualAcuitySimilarityCharacterMap = dict;
        }
        #endregion
        #region static IsVisuallySimilar()
        private const string VisuallySimilar_Source = @"0OQ,LI1Jilj|,NM,8B,G6,FE,S5,2Z,PFR,9P,VWU,vwu,:;,`'"",~-"; // ',.' added separately
        private static HashSet<(char, char)> VisuallySimilarCharacterMap = null; // char[2] ordered
        
        /// <summary>
        ///     O(1)   (hash lookup)
        ///     Returns true if the 2 characters are in the same 'group' of visually similar characters, false if not.
        ///     This is basically the very very cheap version of VisualAcuitySimilarity(), without requiring a matrix.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsVisuallySimilar(char c1, char c2) {
            if(c1 == c2)
                return true;
        
            // lazy-loaded the cheap way
            if(VisuallySimilarCharacterMap == null)
                LoadVisuallySimilarCharacterMap();
            
            (char, char) key;
            if(c1 < c2)
                key = (c1, c2);
            else 
                key = (c2, c1);
        
            return VisuallySimilarCharacterMap.Contains(key);
        }
        private static void LoadVisuallySimilarCharacterMap() {
            var groups = VisuallySimilar_Source
                .Split(',')
                .Concat(new []{ ".," }); // ',.' added separately
        
            var hashset = new HashSet<(char, char)>();

            foreach(var group in groups) {
                for(int i = 0; i < group.Length; i++) {
                    for(int j = i + 1; j < group.Length; j++) {
                        var c1 = group[i];
                        var c2 = group[j];
                        (char, char) key;
                        if(c1 < c2)
                            key = (c1, c2);
                        else
                            key = (c2, c1);
                        hashset.Add(key);
                    }
                }
            }
        
            VisuallySimilarCharacterMap = hashset;
        }
        #endregion
        #region static KeyboardDistance()
        /// <summary>
        ///     Returns the input distance between value1 and value2.
        ///     Returns 0 if perfect match.
        /// </summary>
        /// <param name="unknownCharacterDistance">Assuming the 2 characters dont match, and they arent base keys on the keyboard, how much distance is given. ex: distance between 'é' and 'e', or distance between 'emoji smiley' and 'a'.</param>
        /// <param name="missingCharacterDistance">Default: -1. Cost = (value1.Length - value2.Length) * missingCharacterDistance.  -1 signals to count this as the worst possible match.</param>
        /// <param name="extraCharacterDistance">Default: -1. Cost = (value2.Length - value1.Length) * extraCharacterDistance.  -1 signals to count this as the worst possible match.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double KeyboardDistance(string value1, string value2, double unknownCharacterDistance = 1, double missingCharacterDistance = -1, double extraCharacterDistance = -1) {
            var real_value1 = value1.ToLowerInvariant();
            var real_value2 = value2.ToLowerInvariant();

            double sum_distances = 0;
            var max              = Math.Min(real_value1.Length, real_value2.Length);

            for(int i = 0; i < max; i++) {
                var c1 = real_value1[i];
                var c2 = real_value2[i];

                if(c1 != c2) {
                    (char, char) real_key;
                    if(c1 <= c2)
                        real_key = (c1, c2);
                    else
                        real_key = (c2, c1);

                    if(!KeyboardDistances.TryGetValue(real_key, out var dist))
                        dist = unknownCharacterDistance;

                    sum_distances += dist;
                }
            }

            var diff = real_value1.Length - real_value2.Length;
            if(diff < 0)
                sum_distances += -diff * (extraCharacterDistance < 0 ? KeyboardDistanceMax : extraCharacterDistance);
            else if(diff > 0)
                sum_distances += diff * (missingCharacterDistance < 0 ? KeyboardDistanceMax : missingCharacterDistance);

            return sum_distances;
        }
        /// <summary>
        ///     Returns the input distance between value1 and value2.
        ///     Returns 0 for same character (ex: aA) and 1 for lowest distance possible (ex: 12).
        ///     All returned values are scaled versus the distance between characters.
        /// </summary>
        /// <param name="unknownCharacterDistance">Assuming the 2 characters dont match, and they arent base keys on the keyboard, how much distance is given. ex: distance between 'é' and 'e', or distance between 'emoji smiley' and 'a'.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double KeyboardDistance(char c1, char c2, double unknownCharacterDistance = 1) {
            c1 = char.ToLowerInvariant(c1);
            c2 = char.ToLowerInvariant(c2);
            
            if(c1 == c2)
                return 0;

            (char, char) real_key;
            if(c1 <= c2)
                real_key = (c1, c2);
            else 
                real_key = (c2, c1);

            if(KeyboardDistances.TryGetValue(real_key, out var res))
                return res;
            return unknownCharacterDistance;
        }
        // note: this doesnt include space or tab
        // also might want to consider keypad number distances
        private const string ANSI_KEYBOARD_LAYOUT = 
// "|" to be added with '\' entry
@"~`|1!|2@|3#|4$|5%|6^|7&|8*|9(|0)|-_|=+
q|w|e|r|t|y|u|i|o|p|[{|]}|\
a|s|d|f|g|h|j|k|l|;:|'""
z|x|c|v|b|n|m|,<|.>|?/";
        private static Dictionary<(char, char), double> m_keyboardDistancesInternal = null;
        private static double m_keyboardDistanceMaxInternal = -1;
        private static double KeyboardDistanceMax {
            get {
                if(m_keyboardDistancesInternal == null)
                    LoadKeyboardDistances();

                return m_keyboardDistanceMaxInternal;
            }
        }
        private static Dictionary<(char, char), double> KeyboardDistances {
            get {
                if(m_keyboardDistancesInternal == null)
                    LoadKeyboardDistances();
                
                return m_keyboardDistancesInternal;
            }
        }
        private static void LoadKeyboardDistances() {
            // calculated in pixels, taken from reference ANSI keyboard layout
            const int KEY_WIDTH        = 63;
            const int KEY_HEIGHT       = 63;
            const int BETWEEN_KEY_DIST = 8;
            int[] ROW_OFFSETS         = new []{ 0, 102, 133, 164 };

            var matrix = ANSI_KEYBOARD_LAYOUT
                .Split(new char[]{ '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('|'))
                .ToList();
                
            // add missing '|'
            for(int i = 0; i < matrix.Count; i++) {
                for(int j = 0; j < matrix[i].Length; j++)
                    if(matrix[i][j].Contains('\\'))
                        matrix[i][j] += "|";
            }

            // not necessary, but makes modification easier
            var key_layout = new List<KeyboardKey>();

            int x = 0;
            int y = 0;
            for(int i = 0; i < matrix.Count; i++) {
                x = ROW_OFFSETS[i];
                for(int j = 0; j < matrix[i].Length; j++) {
                    foreach(var k in matrix[i][j]) {
                        key_layout.Add(new KeyboardKey() {
                            Key    = k,
                            Left   = x,
                            Top    = y,
                            Width  = KEY_WIDTH,
                            Height = KEY_HEIGHT,
                        });
                    }
                    x += KEY_WIDTH + BETWEEN_KEY_DIST;
                }
                y += KEY_HEIGHT + BETWEEN_KEY_DIST;
            }

            double max_dist          = 0;
            double min_dist          = key_layout.First(o => o.Key == '1').Distance(key_layout.First(o => o.Key == '2'));
            var referenceOneDistance = key_layout.First(o => o.Key == '1').CenterDistance(key_layout.First(o => o.Key == '2'));
            var min_offset           = referenceOneDistance - min_dist;
            var keyboardDistances    = new Dictionary<(char, char), double>(key_layout.Count * (key_layout.Count - 1));

            // actually important
            key_layout = key_layout.OrderBy(o => o.Key).ToList();

            for(int i = 0; i < key_layout.Count - 1; i++) {
                for(int j = i + 1; j < key_layout.Count; j++) {
                    var key1 = key_layout[i];
                    var key2 = key_layout[j];
                    var dist = key1.Distance(key2);

                    if(dist > 0) // dont add min_dist if 2 characters are on the same key (ie: !1)
                        dist = (dist + min_offset) / referenceOneDistance;
                            
                    max_dist = Math.Max(max_dist, dist);

                    keyboardDistances.Add((key1.Key, key2.Key), dist);
                }
            }

            m_keyboardDistanceMaxInternal = max_dist;
            m_keyboardDistancesInternal   = keyboardDistances;
        }
        private sealed class KeyboardKey {
            public char Key;
            public int Left;
            public int Top;
            public int Width;
            public int Height;
            public (double x, double y) Center => (Left + Width / 2d, Top + Height / 2d);
            public double Distance(KeyboardKey key) {
                // set custom distance function here

                //return this.CenterDistance(key); // horizontal/vertical bias
                return this.RectangleDistance(key); // no horizontal/vertical bias
            }
            /// <summary>
            ///     Suffers from a horizontal/vertical bias.
            /// </summary>
            public double CenterDistance(KeyboardKey key) {
                return EuclidanDistance(this.Center.x, this.Center.y, key.Center.x, key.Center.y);
            }
            /// <summary>
            ///     Lowest distance between the 2 key rectangles.
            ///     Does not suffer from horizontal/vertical bias.
            /// </summary>
            public double RectangleDistance(KeyboardKey key) {
                var x1  = this.Left;
                var y1  = this.Top;
                var x1b = this.Left + this.Width;
                var y1b = this.Top + this.Height;
                var x2  = key.Left;
                var y2  = key.Top;
                var x2b = key.Left + key.Width;
                var y2b = key.Top + key.Height;

                var left   = x2b < x1;
                var right  = x1b < x2;
                var bottom = y2b < y1;
                var top    = y1b < y2;

                if(top && left)
                    return EuclidanDistance(x1, y1b, x2b, y2);
                if(left && bottom)
                    return EuclidanDistance(x1, y1, x2b, y2b);
                if(bottom && right)
                    return EuclidanDistance(x1b, y1, x2, y2b);
                if(right && top)
                    return EuclidanDistance(x1b, y1b, x2, y2);
                if(left)
                    return x1 - x2b;
                if(right)
                    return x2 - x1b;
                if(bottom)
                    return y1 - y2b;
                if(top)
                    return y2 - y1b;
                // intersect
                return 0;
            }
            private static double EuclidanDistance(double x1, double y1, double x2, double y2) {
                var x = x2 - x1;
                var y = y2 - y1;
                return Math.Sqrt(x * x + y * y);
            }
        }
        #endregion
        #region static KeyboardSimilarity()
        /// <summary>
        ///     Returns the similarity between value1 and value2 in terms of keyboard distance.
        ///     Returns 1 if perfect match, 0 if worst match.
        /// </summary>
        /// <param name="unknownCharacterDistance">Assuming the 2 characters dont match, and they arent base keys on the keyboard, how much distance is given. ex: distance between 'é' and 'e', or distance between 'emoji smiley' and 'a'.</param>
        /// <param name="missingCharacterDistance">Default: -1. Cost = (value1.Length - value2.Length) * missingCharacterDistance.  -1 signals to count this as the worst possible match.</param>
        /// <param name="extraCharacterDistance">Default: -1. Cost = (value2.Length - value1.Length) * extraCharacterDistance.  -1 signals to count this as the worst possible match.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double KeyboardSimilarity(string value1, string value2, double unknownCharacterDistance = 1, double missingCharacterDistance = -1, double extraCharacterDistance = -1) {
            var max_len       = Math.Max(value1.Length, value2.Length);
            var sum_distances = KeyboardDistance(value1, value2, unknownCharacterDistance, missingCharacterDistance, extraCharacterDistance);

            return 1d - (sum_distances / (max_len * KeyboardDistanceMax));
        }
        /// <summary>
        ///     Returns the similarity between value1 and value2 in terms of keyboard distance.
        ///     Returns 1 if perfect match, 0 if worst match.
        /// </summary>
        /// <param name="unknownCharacterDistance">Assuming the 2 characters dont match, and they arent base keys on the keyboard, how much distance is given. ex: distance between 'é' and 'e', or distance between 'emoji smiley' and 'a'.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double KeyboardSimilarity(char c1, char c2, double unknownCharacterDistance = 1) {
            var distance = KeyboardDistance(c1, c2, unknownCharacterDistance);

            return 1d - (distance / KeyboardDistanceMax);
        }
        #endregion

        #region static CombinedWordEditDistance()
        /// <summary>
        ///     O(m * n)
        ///     Returns the combined edit distance between value1 and value2 using a combination of multiple metrics.
        ///     Both values are assumed to be single words.
        ///     Returned value is meant to give an idea of how close 2 words are taking into account both keyboard distance and visual similarity of characters.
        ///     
        ///     0 = perfect match
        ///     1+ = 1 remove/add
        ///     fractions indicate how close the character substitutions are.
        /// </summary>
        public static double CombinedWordEditDistance(string value1, string value2, double visually_dissimilar_cost = 0.20) {
            return FuzzyStringMatch.WeightedLevenshteinDistance(value1, value2, 1, 1, SubstitutionCost, 1);
        
            double SubstitutionCost(char from, char to){
                var visualAcuity       = FuzzyStringMatch.VisualAcuitySimilarity(from, to);
                var keyboardSimilarity = FuzzyStringMatch.KeyboardSimilarity(from, to);
                var visuallySimilar    = FuzzyStringMatch.IsVisuallySimilar(from, to);
                
                double result = keyboardSimilarity;
                
                if(visualAcuity.HasValue)
                    result *= visualAcuity.Value;
                //else
                //    result *= 1 - visually_dissimilar_cost;

                if(visuallySimilar)
                    result *= 1 - visually_dissimilar_cost;

                return result;
            }
        }
        #endregion
        #region static CombinedWordSimilarity()
        /// <summary>
        ///     O(m * n + n log n)
        ///     Returns the similarity between value1 and value2 in terms of a combination of multiple metrics.
        ///     Both values are assumed to be single words.
        ///     Returned value is meant to rank results, not to be shown to the user as a match%.
        ///     
        ///     0 = no match
        ///     1 = perfect match
        /// </summary>
        public static double CombinedWordSimilarity(string value1, string value2, double visually_dissimilar_cost = 0.20) {
            // sorensendice basically ranks by # of bigram matches, but doesnt care about ordering
            var sorensenDiceCoefficient     = FuzzyStringMatch.SorensenDiceCoefficient(value1, value2);
            var weightedLevenshteinDistance = FuzzyStringMatch.CombinedWordEditDistance(value1, value2, visually_dissimilar_cost);
            
            // this line only serves to return results within 0-1 range
            weightedLevenshteinDistance     = FuzzyStringMatch.ConvertHammingDistanceToSimilarity(weightedLevenshteinDistance);

            return sorensenDiceCoefficient * weightedLevenshteinDistance;
        }
        #endregion

        // todo: soundex, double metaphone
        // Eudex preDict/Eudex.java

        #region static RemoveDiacritics()
        private static readonly Encoding m_diacriticsEncoding = Encoding.GetEncoding("ISO-8859-8");
        /// <summary>
        ///     Removes diacritics within the string (ex: éèê -> e).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string RemoveDiacritics(string value) {
            return Encoding.UTF8.GetString(m_diacriticsEncoding.GetBytes(value));
        }
        /// <summary>
        ///     Removes diacritics within the string (ex: éèê -> e).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string RemoveDiacritics(string value, int index, int len) {
            var chars = value.ToCharArray(index, len);
            var bytes = new byte[m_diacriticsEncoding.GetByteCount(chars)];
            m_diacriticsEncoding.GetBytes(chars, 0, len, bytes, 0);
            return Encoding.UTF8.GetString(bytes);
        }
        /// <summary>
        ///     Removes diacritics within the string (ex: éèê -> e).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveDiacritics(char[] value, int index, int len) {
            var bytes = new byte[m_diacriticsEncoding.GetByteCount(value, index, len)];
            m_diacriticsEncoding.GetBytes(value, index, len, bytes, 0);
            Encoding.UTF8.GetChars(bytes, 0, bytes.Length, value, index);
        }
        /// <summary>
        ///     Removes diacritics within the value (ex: éèê -> e).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char RemoveDiacritics(char c) {
            var chars = new char[]{ c };
            var bytes = new byte[m_diacriticsEncoding.GetByteCount(chars)];
            m_diacriticsEncoding.GetBytes(chars, 0, 1, bytes, 0);
            return Encoding.UTF8.GetChars(bytes)[0];
        }
        #endregion
        #region static Normalize()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Normalize(string value) {
            return RemoveDiacritics(value.Trim()).ToLowerInvariant();
        }
        #endregion
        #region static GetStableHashCode()
        /// <summary>
        ///     FNV-1a (Fowler-Noll-Vo) non-secure, non-cryptographic hash.
        ///     https://en.wikipedia.org/wiki/Fowler%E2%80%93Noll%E2%80%93Vo_hash_function
        ///     
        ///     This is a fast hash function suitable for hashtables, checksums and bloom filters.
        ///     
        ///     This function main purpose is to provide a stable string.GetHashCode() implementation that will return a deterministic output, 
        ///     unlike the default string.GetHashCode() implementation that will return a different hash every new app start.
        /// </summary>
        /// <remarks>
        ///     FNV-1a is sensible (less secure) to the character '\0'.
        ///     Since strings typically do not contain it, it is intentionally ignored.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetStableHashCode(string value) {
            unchecked {
                //         PRIME           OFFSET_BASIS
                // 32bits: 16777619        2166136261
                // 64bits: 1099511628211   0xcbf29ce484222325 (14695981039346656037)
                const uint PRIME        = 16777619;
                const uint OFFSET_BASIS = 2166136261;

                var hash = OFFSET_BASIS;
                int len  = value.Length;
                for(int i = 0; i < len; i++) {
                    hash ^= value[i];
                    hash *= PRIME;
                }
                return (int)hash;
            }
        }
        #endregion

        // basic data manipulation methods
        #region static Softmax()
        /// <summary>
        ///     Changes the values to comply with a softmax() function.
        ///     The softmax makes sure that the sum of all values equals to 1, where the higher values are given exponentially more weight.
        ///     ie: each element receive a proportional weight equal to its value exponentiated.
        /// </summary>
        public static void Softmax(double[] values) {
            if(values.Length == 0)
                return;
            if(values.Length == 1) {
                values[0] = 1;
                return;
            }

            //var max = values.Max(o => o);
            //var scaling_factor = values.Sum(o => Math.Exp(o - max));

            // determine max output sum
            // does all output nodes at once so scale doesn't have to be re-computed each time
            var max = values [ 0 ];
            for(int i = 0; i < values.Length; i++)
                if(values[i] > max)
                    max = values[i];

            // determine scaling factor -- sum of exp(each val - max)
            var scaling_factor = 0.0;
            for(int i = 0; i < values.Length; i++)
                scaling_factor += Math.Exp(values[i] - max);

            for(int i = 0; i < values.Length; i++)
                values[i] = Math.Exp(values[i] - max) / scaling_factor;

            // now scaled so that values sum to 1.0
        }
        #endregion
        #region static ConvertHammingDistanceToSimilarity()
        /// <summary>
        ///     Convert a hamming distance (0 = perfect, 1 to int.MaxValue to indicate mismatches)
        ///     into a ranking (1 = perfect, 0 = no match).
        /// </summary>
        public static double ConvertHammingDistanceToSimilarity(double value) {
            // scores go from 0 to [hamming distance]
            // 0 indicates perfect

            // use a sigmoid to restrict results between [0.5 and 1]
            var x = Sigmoid(value);
            // then convert into 1=perfect, 0=no match
            return -x * 2.0 + 2.0;
        }
        #endregion
        #region static Sigmoid()
        /// <summary>
        ///     Restrict value within -1 and 1 using the sigmoid function.
        /// </summary>
        public static double Sigmoid(double value) {
            return 1.0 / (1.0 + Math.Exp(-value));
        }
        #endregion
        #region static MeanCentering()
        /// <summary>
        ///     Recenters the data based on the mean.
        ///     (x - min) / (max - min)
        /// </summary>
        public static void MeanCentering(double[] vector) {
            var min = double.MaxValue;
            var max = double.MinValue;
            
            for(int i = 0; i < vector.Length; i++) {
                var value = vector[i];
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            for(int i = 0; i < vector.Length; i++)
                vector[i] = (vector[i] - min) / (max - min);
        }
        /// <summary>
        ///     Recenters the data based on the mean.
        ///     (x - min) / (max - min)
        /// </summary>
        public static void MeanCentering(double[][] vectors) {
            var min = double.MaxValue;
            var max = double.MinValue;

            for(int i = 0; i < vectors.Length; i++) {
                var vector = vectors[i];
                for(int j = 0; j < vector.Length; j++) {
                    var value = vector[j];
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
            }

            for(int i = 0; i < vectors.Length; i++) {
                var vector = vectors[i];
                for(int j = 0; j < vector.Length; j++)
                    vector[j] = (vector[j] - min) / (max - min);
            }
        }
        #endregion

        public class CharacterFilter {
            public static readonly CharacterFilter Default = new CharacterFilter();
            public readonly BitArray Filter = new BitArray(char.MaxValue);
            #region constructors
            public CharacterFilter(string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789") {
                foreach(var c in alphabet)
                    this.Filter[c] = true;
            }
            #endregion
            #region implicit operators
            public static implicit operator CharacterFilter(string alphabet) {
                return new CharacterFilter(alphabet);
            }
            #endregion
        }
    }
}
