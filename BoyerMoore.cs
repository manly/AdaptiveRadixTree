namespace System.Text
{
    public interface IPrebuiltSearchAlgorithm<T> {
        int IndexOf(T data, int startIndex, int count);
        int LastIndexOf(T data, int startIndex, int count);
    }

    /// <summary>
    ///     Provides an efficient Boyer-Moore string search implementation.
    ///     
    ///                           search          pre-processing       space
    ///                         =================================================
    ///     Naive                 O(nm)           -                    -
    ///     Boyer-Moore           best: O(n/m)    O(m + k)             O(m + k)
    ///                           worst: O(mn)
    ///     Knuth-Morris-Pratt    O(n)            O(m)                 O(m)
    ///     Bitap                 O(mn)           O(m + k)
    ///     BNDM                  O(m)            O(n)
    ///     
    ///     m = pattern length
    ///     n = data length
    ///     k = alphabet size (string = 65536, byte[] = 256, DNA = 4)
    ///     
    ///     Boyer-Moore is the standard benchmark for practical string-searching and typically recommended when searching either [long text] or [long search patterns].
    ///     Knuth-Morris-Pratt is better suited for [shorter search patterns] or [small alphabet sizes].
    ///     Bitap (shift-or, shift-and, Baeza-Yates-Gonnet) for regex search. Used by grep.
    ///     BNDM (Backward Non-Deterministic DAWG (Directed Acyclic Word Graph) Matching) for replacement-wildcard searches.
    /// </summary>
    public static class BoyerMoore {
        #region static Build()
        public static IPrebuiltSearchAlgorithm<string> Build(string pattern) {
            if(pattern == null)
                throw new ArgumentNullException(nameof(pattern));

            if(pattern.Length == 0)
                return new BoyerMooreImplementation_EmptyPattern();

            // optimize cache locality by using the smallest possible storage

            if(pattern.Length <= byte.MaxValue)
                return new BoyerMooreImplementation_StringUInt8(pattern);
            if(pattern.Length <= ushort.MaxValue)
                return new BoyerMooreImplementation_StringUInt16(pattern);

            return new BoyerMooreImplementation_StringInt32(pattern);
        }
        public static IPrebuiltSearchAlgorithm<byte[]> Build(byte[] pattern) {
            if(pattern == null)
                throw new ArgumentNullException(nameof(pattern));

            if(pattern.Length == 0)
                return new BoyerMooreImplementation_EmptyPattern();

            // optimize cache locality by using the smallest possible storage

            if(pattern.Length <= byte.MaxValue)
                return new BoyerMooreImplementation_ByteArrayUInt8(pattern);
            if(pattern.Length <= ushort.MaxValue)
                return new BoyerMooreImplementation_ByteArrayUInt16(pattern);

            return new BoyerMooreImplementation_ByteArrayInt32(pattern);
        }
        #endregion

        #region IndexOf() extensions
        public static int IndexOf(this IPrebuiltSearchAlgorithm<string> searchAlgorithm, string data) {
            return searchAlgorithm.IndexOf(data, 0, data.Length);
        }
        public static int IndexOf(this IPrebuiltSearchAlgorithm<string> searchAlgorithm, string data, int startIndex) {
            return searchAlgorithm.IndexOf(data, startIndex, data.Length - startIndex);
        }
        public static int IndexOf(this IPrebuiltSearchAlgorithm<byte[]> searchAlgorithm, byte[] data) {
            return searchAlgorithm.IndexOf(data, 0, data.Length);
        }
        public static int IndexOf(this IPrebuiltSearchAlgorithm<byte[]> searchAlgorithm, byte[] data, int startIndex) {
            return searchAlgorithm.IndexOf(data, startIndex, data.Length - startIndex);
        }
        #endregion
        #region LastIndexOf() extensions
        public static int LastIndexOf(this IPrebuiltSearchAlgorithm<string> searchAlgorithm, string data) {
            return searchAlgorithm.LastIndexOf(data, data.Length - 1, data.Length);
        }
        public static int LastIndexOf(this IPrebuiltSearchAlgorithm<string> searchAlgorithm, string data, int startIndex) {
            return searchAlgorithm.LastIndexOf(data, startIndex, startIndex + 1);
        }
        public static int LastIndexOf(this IPrebuiltSearchAlgorithm<byte[]> searchAlgorithm, byte[] data) {
            return searchAlgorithm.LastIndexOf(data, data.Length - 1, data.Length);
        }
        public static int LastIndexOf(this IPrebuiltSearchAlgorithm<byte[]> searchAlgorithm, byte[] data, int startIndex) {
            return searchAlgorithm.LastIndexOf(data, startIndex, startIndex + 1);
        }
        #endregion

        #region private static IsPrefix()
        /// <summary>
        ///     Returns true if the suffix (value.SubString(startIndex)) matches the start of the string.
        /// </summary>
        private static bool IsPrefix(string value, int startIndex) {
            int max = value.Length - startIndex;
            for(int i = 0; i < max; i++) {
                if(value[i] != value[startIndex + i])
                    return false;
            }
            return true;
        }
        /// <summary>
        ///     Returns true if the suffix (value.SubString(startIndex)) matches the start of the string.
        /// </summary>
        private static bool IsPrefix(byte[] value, int startIndex) {
            int max = value.Length - startIndex;
            for(int i = 0; i < max; i++) {
                if(value[i] != value[startIndex + i])
                    return false;
            }
            return true;
        }
        #endregion
        #region private static SuffixLength()
        /// <summary>
        ///     Returns the length of the longest suffix ending on value[startIndex].
        ///     ex: ("---ab-ab", 4) = 2
        /// </summary>
        private static int SuffixLength(string value, int startIndex) {
            int len = 0;

            // increment suffix length i to the first mismatch or beginning
            while(value[startIndex - len] == value[value.Length - 1 - len] && len < startIndex)
                len++;
                
            return len;
        }
        /// <summary>
        ///     Returns the length of the longest suffix ending on value[startIndex].
        ///     ex: ("---ab-ab", 4) = 2
        /// </summary>
        private static int SuffixLength(byte[] value, int startIndex) {
            int len = 0;

            // increment suffix length i to the first mismatch or beginning
            while(value[startIndex - len] == value[value.Length - 1 - len] && len < startIndex)
                len++;
                
            return len;
        }
        #endregion

        // implementations
        #region private struct BoyerMooreImplementation_StringInt32
        private readonly struct BoyerMooreImplementation_StringInt32 : IPrebuiltSearchAlgorithm<string> {
            private const int ALPHABET_SIZE = 65536;

            private readonly int[] m_delta1; // size = ALPHABET_SIZE
            private readonly int[] m_delta2; // size = m_pattern.Length
            private readonly string m_pattern;

            public BoyerMooreImplementation_StringInt32(string pattern) {
                if(pattern.Length > int.MaxValue)
                    throw new ArgumentOutOfRangeException($"{nameof(pattern)}.Length > {int.MaxValue}. Use another implementation that supports longer strings.", nameof(pattern));

                m_pattern = pattern;
                m_delta1  = MakeDelta1(pattern);
                m_delta2  = MakeDelta2(pattern);
            }

            public int IndexOf(string data, int startIndex, int count) {
                int end           = startIndex + count;
                int patternLength = m_pattern.Length;
                int i             = startIndex + patternLength - 1;
                while(i < end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i--;
                        j--;
                    }
                    if(j < 0)
                        return i + 1;

                    i += Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }
            public int LastIndexOf(string data, int startIndex, int count) {
                int end           = startIndex - count;
                int patternLength = m_pattern.Length;
                int i             = startIndex - patternLength + 1;
                while(i > end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i++;
                        j--;
                    }
                    if(j < 0)
                        return i - 1;

                    i -= Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }

            /// <summary>
            ///     delta1[c] contains the distance between the last character of "value" and the rightmost occurrence of c in "value".
            ///     If c does not occur in "value", then delta1[c] = value.Length.
            ///     If c is at string[i] and c != value[value.Length - 1], we can safely shift i over by delta1[c], which is the minimum distance
            ///     needed to shift "value" forward to get string[i] lined up with some character in "value".
            /// </summary>
            private static int[] MakeDelta1(string value) {
                var res = new int[ALPHABET_SIZE];

                int len = value.Length;
                for(int i = 0; i < ALPHABET_SIZE; i++)
                    res[i] = len;

                for(int i = 0; i < len - 1; i++)
                    res[value[i]] = len - 1 - i;

                return res;
            }

            /// <summary>
            ///     Given a mismatch at value[pos], we want to align with the next possible full match could be based on what we
            ///     know about value[pos + 1] to value[value.Length - 1].
            ///     
            ///     In case 1:
            ///     value[pos + 1] to value[patlen - 1] does not occur elsewhere in "value", the next plausible match starts at or after the mismatch.
            ///     If, within the substring value[pos+1 .. value.Length - 1], lies a prefix of "value", the next plausible match is here 
            ///     (if there are multiple prefixes in the substring, pick the longest). 
            ///     Otherwise, the next plausible match starts past the character aligned with value[value.Length - 1].
            ///     
            ///     In case 2:
            ///     value[pos + 1] to value[value.Length - 1] does occur elsewhere in "value". 
            ///     The mismatch tells us that we are not looking at the end of a match.
            ///     We may, however, be looking at the middle of a match.
            ///     
            ///     The first loop, which takes care of case 1, is analogous to the KMP (Knuth-Morris-Pratt) table, 
            ///     adapted for a 'backwards' scan order with the additional restriction that the substrings it considers as potential prefixes are all suffixes.
            ///     In the worst case scenario "value" consists of the same letter repeated, so every suffix is a prefix. 
            ///     This loop alone is not sufficient, however:
            ///     Suppose that "value" is "ABYXCDBYX", and text is ".....ABYXCDEYX".
            ///     We will match X, Y, and find B != E. There is no prefix of "value" in the suffix "YX", 
            ///     so the first loop tells us to skip forward by 9 characters.
            ///     Although superficially similar to the KMP table, the KMP table relies on information about the beginning of 
            ///     the partial match that the BM (Boyer-Moore) algorithm does not have.
            ///     
            ///     The second loop addresses case 2. Since suffix_length may not be unique, we want to take the minimum value, 
            ///     which will tell us how far away the closest potential match is.
            /// </summary>
            private static int[] MakeDelta2(string value) {
                int len               = value.Length;
                var res               = new int[len];
                int last_prefix_index = len - 1;

                for(int i = len - 1; i >= 0; i--) {
                    if(IsPrefix(value, i + 1))
                        last_prefix_index = i + 1;

                    res[i] = last_prefix_index + (len - 1 - i);
                }

                for(int i = 0; i < len - 1; i++) {
                    int suffix_len = SuffixLength(value, i);
                    if(value[i - suffix_len] != value[len - 1 - suffix_len])
                        res[len - 1 - suffix_len] = len - 1 - i + suffix_len;
                }

                return res;
            }
        }
        #endregion
        #region private struct BoyerMooreImplementation_StringUInt16
        private readonly struct BoyerMooreImplementation_StringUInt16 : IPrebuiltSearchAlgorithm<string> {
            private const int ALPHABET_SIZE = 65536;

            private readonly ushort[] m_delta1; // size = ALPHABET_SIZE
            private readonly ushort[] m_delta2; // size = m_pattern.Length
            private readonly string m_pattern;

            public BoyerMooreImplementation_StringUInt16(string pattern) {
                if(pattern.Length > ushort.MaxValue)
                    throw new ArgumentOutOfRangeException($"{nameof(pattern)}.Length > {ushort.MaxValue}. Use another implementation that supports longer strings.", nameof(pattern));

                m_pattern = pattern;
                m_delta1  = MakeDelta1(pattern);
                m_delta2  = MakeDelta2(pattern);
            }

            public int IndexOf(string data, int startIndex, int count) {
                int end           = startIndex + count;
                int patternLength = m_pattern.Length;
                int i             = startIndex + patternLength - 1;
                while(i < end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i--;
                        j--;
                    }
                    if(j < 0)
                        return i + 1;

                    i += Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }
            public int LastIndexOf(string data, int startIndex, int count) {
                int end           = startIndex - count;
                int patternLength = m_pattern.Length;
                int i             = startIndex - patternLength + 1;
                while(i > end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i++;
                        j--;
                    }
                    if(j < 0)
                        return i - 1;

                    i -= Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static ushort[] MakeDelta1(string value) {
                var res = new ushort[ALPHABET_SIZE];

                ushort len = unchecked((ushort)value.Length);
                for(int i = 0; i < ALPHABET_SIZE; i++)
                    res[i] = len;

                for(int i = 0; i < len - 1; i++)
                    res[value[i]] = unchecked((ushort)(len - 1 - i));

                return res;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static ushort[] MakeDelta2(string value) {
                int len               = value.Length;
                var res               = new ushort[len];
                int last_prefix_index = len - 1;

                for(int i = len - 1; i >= 0; i--) {
                    if(IsPrefix(value, i + 1))
                        last_prefix_index = i + 1;

                    res[i] = unchecked((ushort)(last_prefix_index + (len - 1 - i)));
                }

                for(int i = 0; i < len - 1; i++) {
                    int suffix_len = SuffixLength(value, i);
                    if(value[i - suffix_len] != value[len - 1 - suffix_len])
                        res[len - 1 - suffix_len] = unchecked((ushort)(len - 1 - i + suffix_len));
                }

                return res;
            }
        }
        #endregion
        #region private struct BoyerMooreImplementation_StringUInt8
        private readonly struct BoyerMooreImplementation_StringUInt8 : IPrebuiltSearchAlgorithm<string> {
            private const int ALPHABET_SIZE = 65536;

            private readonly byte[] m_delta1; // size = ALPHABET_SIZE
            private readonly byte[] m_delta2; // size = m_pattern.Length
            private readonly string m_pattern;

            public BoyerMooreImplementation_StringUInt8(string pattern) {
                if(pattern.Length > byte.MaxValue)
                    throw new ArgumentOutOfRangeException($"{nameof(pattern)}.Length > {byte.MaxValue}. Use another implementation that supports longer strings.", nameof(pattern));

                m_pattern = pattern;
                m_delta1  = MakeDelta1(pattern);
                m_delta2  = MakeDelta2(pattern);
            }

            public int IndexOf(string data, int startIndex, int count) {
                int end           = startIndex + count;
                int patternLength = m_pattern.Length;
                int i             = startIndex + patternLength - 1;
                while(i < end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i--;
                        j--;
                    }
                    if(j < 0)
                        return i + 1;

                    i += Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }
            public int LastIndexOf(string data, int startIndex, int count) {
                int end           = startIndex - count;
                int patternLength = m_pattern.Length;
                int i             = startIndex - patternLength + 1;
                while(i > end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i++;
                        j--;
                    }
                    if(j < 0)
                        return i - 1;

                    i -= Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static byte[] MakeDelta1(string value) {
                var res = new byte[ALPHABET_SIZE];

                byte len = unchecked((byte)value.Length);
                for(int i = 0; i < ALPHABET_SIZE; i++)
                    res[i] = len;

                for(int i = 0; i < len - 1; i++)
                    res[value[i]] = unchecked((byte)(len - 1 - i));

                return res;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static byte[] MakeDelta2(string value) {
                int len               = value.Length;
                var res               = new byte[len];
                int last_prefix_index = len - 1;

                for(int i = len - 1; i >= 0; i--) {
                    if(IsPrefix(value, i + 1))
                        last_prefix_index = i + 1;

                    res[i] = unchecked((byte)(last_prefix_index + (len - 1 - i)));
                }

                for(int i = 0; i < len - 1; i++) {
                    int suffix_len = SuffixLength(value, i);
                    if(value[i - suffix_len] != value[len - 1 - suffix_len])
                        res[len - 1 - suffix_len] = unchecked((byte)(len - 1 - i + suffix_len));
                }

                return res;
            }
        }
        #endregion

        #region private struct BoyerMooreImplementation_ByteArrayInt32
        private readonly struct BoyerMooreImplementation_ByteArrayInt32 : IPrebuiltSearchAlgorithm<byte[]> {
            private const int ALPHABET_SIZE = 256;

            private readonly int[] m_delta1; // size = ALPHABET_SIZE
            private readonly int[] m_delta2; // size = m_pattern.Length
            private readonly byte[] m_pattern;

            public BoyerMooreImplementation_ByteArrayInt32(byte[] pattern) {
                if(pattern.Length > int.MaxValue)
                    throw new ArgumentOutOfRangeException($"{nameof(pattern)}.Length > {int.MaxValue}. Use another implementation that supports longer strings.", nameof(pattern));

                m_pattern = pattern;
                m_delta1  = MakeDelta1(pattern);
                m_delta2  = MakeDelta2(pattern);
            }

            public int IndexOf(byte[] data, int startIndex, int count) {
                int end           = startIndex + count;
                int patternLength = m_pattern.Length;
                int i             = startIndex + patternLength - 1;
                while(i < end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i--;
                        j--;
                    }
                    if(j < 0)
                        return i + 1;

                    i += Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }
            public int LastIndexOf(byte[] data, int startIndex, int count) {
                int end           = startIndex - count;
                int patternLength = m_pattern.Length;
                int i             = startIndex - patternLength + 1;
                while(i > end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i++;
                        j--;
                    }
                    if(j < 0)
                        return i - 1;

                    i -= Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static int[] MakeDelta1(byte[] value) {
                var res = new int[ALPHABET_SIZE];

                int len = value.Length;
                for(int i = 0; i < ALPHABET_SIZE; i++)
                    res[i] = len;

                for(int i = 0; i < len - 1; i++)
                    res[value[i]] = len - 1 - i;

                return res;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static int[] MakeDelta2(byte[] value) {
                int len               = value.Length;
                var res               = new int[len];
                int last_prefix_index = len - 1;

                for(int i = len - 1; i >= 0; i--) {
                    if(IsPrefix(value, i + 1))
                        last_prefix_index = i + 1;

                    res[i] = last_prefix_index + (len - 1 - i);
                }

                for(int i = 0; i < len - 1; i++) {
                    int suffix_len = SuffixLength(value, i);
                    if(value[i - suffix_len] != value[len - 1 - suffix_len])
                        res[len - 1 - suffix_len] = len - 1 - i + suffix_len;
                }

                return res;
            }
        }
        #endregion
        #region private struct BoyerMooreImplementation_ByteArrayUInt16
        private readonly struct BoyerMooreImplementation_ByteArrayUInt16 : IPrebuiltSearchAlgorithm<byte[]> {
            private const int ALPHABET_SIZE = 256;

            private readonly ushort[] m_delta1; // size = ALPHABET_SIZE
            private readonly ushort[] m_delta2; // size = m_pattern.Length
            private readonly byte[] m_pattern;

            public BoyerMooreImplementation_ByteArrayUInt16(byte[] pattern) {
                if(pattern.Length > ushort.MaxValue)
                    throw new ArgumentOutOfRangeException($"{nameof(pattern)}.Length > {ushort.MaxValue}. Use another implementation that supports longer strings.", nameof(pattern));

                m_pattern = pattern;
                m_delta1  = MakeDelta1(pattern);
                m_delta2  = MakeDelta2(pattern);
            }

            public int IndexOf(byte[] data, int startIndex, int count) {
                int end           = startIndex + count;
                int patternLength = m_pattern.Length;
                int i             = startIndex + patternLength - 1;
                while(i < end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i--;
                        j--;
                    }
                    if(j < 0)
                        return i + 1;

                    i += Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }
            public int LastIndexOf(byte[] data, int startIndex, int count) {
                int end           = startIndex - count;
                int patternLength = m_pattern.Length;
                int i             = startIndex - patternLength + 1;
                while(i > end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i++;
                        j--;
                    }
                    if(j < 0)
                        return i - 1;

                    i -= Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static ushort[] MakeDelta1(byte[] value) {
                var res = new ushort[ALPHABET_SIZE];

                ushort len = unchecked((ushort)value.Length);
                for(int i = 0; i < ALPHABET_SIZE; i++)
                    res[i] = len;

                for(int i = 0; i < len - 1; i++)
                    res[value[i]] = unchecked((ushort)(len - 1 - i));

                return res;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static ushort[] MakeDelta2(byte[] value) {
                int len               = value.Length;
                var res               = new ushort[len];
                int last_prefix_index = len - 1;

                for(int i = len - 1; i >= 0; i--) {
                    if(IsPrefix(value, i + 1))
                        last_prefix_index = i + 1;

                    res[i] = unchecked((ushort)(last_prefix_index + (len - 1 - i)));
                }

                for(int i = 0; i < len - 1; i++) {
                    int suffix_len = SuffixLength(value, i);
                    if(value[i - suffix_len] != value[len - 1 - suffix_len])
                        res[len - 1 - suffix_len] = unchecked((ushort)(len - 1 - i + suffix_len));
                }

                return res;
            }
        }
        #endregion
        #region private struct BoyerMooreImplementation_ByteArrayUInt8
        private readonly struct BoyerMooreImplementation_ByteArrayUInt8 : IPrebuiltSearchAlgorithm<byte[]> {
            private const int ALPHABET_SIZE = 256;

            private readonly byte[] m_delta1; // size = ALPHABET_SIZE
            private readonly byte[] m_delta2; // size = m_pattern.Length
            private readonly byte[] m_pattern;

            public BoyerMooreImplementation_ByteArrayUInt8(byte[] pattern) {
                if(pattern.Length > byte.MaxValue)
                    throw new ArgumentOutOfRangeException($"{nameof(pattern)}.Length > {byte.MaxValue}. Use another implementation that supports longer strings.", nameof(pattern));

                m_pattern = pattern;
                m_delta1  = MakeDelta1(pattern);
                m_delta2  = MakeDelta2(pattern);
            }

            public int IndexOf(byte[] data, int startIndex, int count) {
                int end           = startIndex + count;
                int patternLength = m_pattern.Length;
                int i             = startIndex + patternLength - 1;
                while(i < end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i--;
                        j--;
                    }
                    if(j < 0)
                        return i + 1;

                    i += Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }
            public int LastIndexOf(byte[] data, int startIndex, int count) {
                int end           = startIndex - count;
                int patternLength = m_pattern.Length;
                int i             = startIndex - patternLength + 1;
                while(i > end) {
                    int j = patternLength - 1;
                    while(j >= 0 && data[i] == m_pattern[j]) {
                        i++;
                        j--;
                    }
                    if(j < 0)
                        return i - 1;

                    i -= Math.Max(m_delta1[data[i]], m_delta2[j]);
                }
                return -1;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static byte[] MakeDelta1(byte[] value) {
                var res = new byte[ALPHABET_SIZE];

                byte len = unchecked((byte)value.Length);
                for(int i = 0; i < ALPHABET_SIZE; i++)
                    res[i] = len;

                for(int i = 0; i < len - 1; i++)
                    res[value[i]] = unchecked((byte)(len - 1 - i));

                return res;
            }

            /// <summary>
            ///     See Int32 version for comments.
            /// </summary>
            private static byte[] MakeDelta2(byte[] value) {
                int len               = value.Length;
                var res               = new byte[len];
                int last_prefix_index = len - 1;

                for(int i = len - 1; i >= 0; i--) {
                    if(IsPrefix(value, i + 1))
                        last_prefix_index = i + 1;

                    res[i] = unchecked((byte)(last_prefix_index + (len - 1 - i)));
                }

                for(int i = 0; i < len - 1; i++) {
                    int suffix_len = SuffixLength(value, i);
                    if(value[i - suffix_len] != value[len - 1 - suffix_len])
                        res[len - 1 - suffix_len] = unchecked((byte)(len - 1 - i + suffix_len));
                }

                return res;
            }
        }
        #endregion

        #region private struct BoyerMooreImplementation_EmptyPattern
        private readonly struct BoyerMooreImplementation_EmptyPattern : IPrebuiltSearchAlgorithm<string>, IPrebuiltSearchAlgorithm<byte[]> {
            public int IndexOf(string data, int startIndex, int count) {
                return startIndex;
            }
            public int IndexOf(byte[] data, int startIndex, int count) {
                return startIndex;
            }
            public int LastIndexOf(string data, int startIndex, int count) {
                return startIndex;
            }
            public int LastIndexOf(byte[] data, int startIndex, int count) {
                return startIndex;
            }
        }
        #endregion
    }
}