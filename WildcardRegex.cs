using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace System.Collections.Specialized
{
    /// <summary>
    ///     Simplified wildcard search comparer.
    ///     Works like a regex, but with a wildcard syntax (ie: ?* characters).
    ///     Uses non-greedy matching for '*' wildcard.
    ///     Usually 2x faster than using a compiled System.Text.RegularExpressions.Regex.
    /// </summary>
    /// <remarks>
    ///     If pure performance is needed, long search patterns could be replaced with a pre-computed Boyer-Moore string search,
    ///     thus avoiding rebuilding the array. 
    /// </remarks>
    public sealed class WildcardRegex {
        private const char DEFAULT_WILDCARD_UNKNOWN  = '?';
        private const char DEFAULT_WILDCARD_ANYTHING = '*';

        public readonly string RegexWildcardFormat;
        public readonly SearchOption Option;
        
        /// <summary>?</summary>
        private readonly char m_wildcardUnknown;
        /// <summary>*, Assumes non-greedy matching (least characters)</summary>
        private readonly char m_wildcardAnything;

        
        private readonly bool m_resultMustMatchAtStart; // result/match must start at 0.
        private readonly bool m_resultMustMatchAtEnd;   // result/match must end at {start+length}.
        private readonly int m_totalCharacters;
        private readonly ConsecutiveParseSection[] m_sections; 
        private readonly bool m_isMatchAll;             // if(m_sections.Length==0 && true) format = '*', if(m_sections.Length==0 && false) format = ''
        private readonly bool m_simpleEquals;           // if(m_sections.Length==1 && true) search has no wildcards (ie: run string.CompareOrdinal() instead)
        private readonly bool m_simpleContains;         // if(m_sections.Length==1 && true) search is '*searchterms*' without any '?' (ie: this search can be done with just one indexof())

        #region constructors
        /// <param name="regex_wildcard_format">The wildcard pattern. ex: '20??-01-01*'</param>
        /// <param name="wildcard_anything_character">Non-greedy matching (least characters)</param>
        public WildcardRegex(string regex_wildcard_format, SearchOption option = SearchOption.ExactMatch, char wildcard_unknown_character = DEFAULT_WILDCARD_UNKNOWN, char wildcard_anything_character = DEFAULT_WILDCARD_ANYTHING) {
            if(string.IsNullOrEmpty(regex_wildcard_format))
                throw new FormatException(nameof(regex_wildcard_format));

            this.RegexWildcardFormat = regex_wildcard_format;
            this.Option              = option;
            m_wildcardUnknown        = wildcard_unknown_character;
            m_wildcardAnything       = wildcard_anything_character;

            if(!string.IsNullOrEmpty(regex_wildcard_format)) {
                m_sections        = this.ParseSearchFormat(regex_wildcard_format);
                m_totalCharacters = m_sections.Sum(section => section.Length + section.WildcardUnknownBefore + section.WildcardUnknownAfter);
                m_isMatchAll      = true; // only applies if section.Length==0, which would mean format='*'

                if(option == SearchOption.ExactMatch || option == SearchOption.StartsWith)
                    m_resultMustMatchAtStart = regex_wildcard_format[0] != m_wildcardAnything;
                if(option == SearchOption.ExactMatch || option == SearchOption.EndsWith)
                    m_resultMustMatchAtEnd = regex_wildcard_format[regex_wildcard_format.Length - 1] != m_wildcardAnything;
                
                // if theres no wildcards
                m_simpleEquals = m_sections.Length == 1 && 
                    m_resultMustMatchAtStart && 
                    // check if theres no '?' within the section besides the initial/ending ?s
                    m_sections[0].SearchIndex + m_sections[0].Search.Length == m_sections[0].Length;

                m_simpleContains = m_sections.Length == 1 && 
                    !m_resultMustMatchAtStart && 
                    !m_resultMustMatchAtEnd &&
                    // check if theres no '?' within the section besides the initial/ending ?s
                    m_sections[0].SearchIndex + m_sections[0].Search.Length == m_sections[0].Length;
            } else {
                m_sections               = new ConsecutiveParseSection[0];
                m_totalCharacters        = 0;
                m_isMatchAll             = false;
                m_resultMustMatchAtStart = false;
                m_resultMustMatchAtEnd   = false;
                m_simpleEquals            = false;
                m_simpleContains         = false;
            }
        }
        #endregion

        #region IsMatch()
        public bool IsMatch(string value) {
            return this.Match(value, 0, value.Length).Length >= 0;
        }
        public bool IsMatch(string value, int startIndex) {
            return this.Match(value, startIndex, value.Length - startIndex).Length >= 0;
        }
        public bool IsMatch(string value, int startIndex, int length) {
            return this.Match(value, startIndex, length).Length >= 0;
        }
        #endregion
        #region Match()
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public Result Match(string value) {
            return this.Match(value, 0, value.Length);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public Result Match(string value, int startIndex) {
            return this.Match(value, startIndex, value.Length - startIndex);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public Result Match(string value, int startIndex, int length) {
            // algorithm explanation
            // format = '123*456*?678'
            // sections = {123, 456, ?678}
            // first makes sure the string starts with '123' (if beginswith)
            // then makes sure the string ends with '?678' (if endswith)
            // then make sure every other section from section 1+ are found in order and ends before the last one
            // if any section is not found, then there is no match

            if(length < m_totalCharacters)
                return new Result(startIndex, -1);
            // special case if format = '*' or ''
            if(m_sections.Length == 0)
                return new Result(startIndex, m_isMatchAll ? 0 : -1);
            // special case: no wildcards, use quick comparison
            if(m_simpleEquals)
                return this.SimpleEquals(value, startIndex, length);
            // special case: searching in a way that can be done with just one IndexOf()
            if(m_simpleContains)
                return this.SimpleContains(value, startIndex, length);

            int index          = startIndex;
            int originalLength = length;
            int sectionIndex   = 0;
            int firstIndex     = -1; // first match
            int lastIndex      = -1; // last match end pos

            if(m_resultMustMatchAtStart) {
                var section = m_sections[0];
                index      += section.WildcardUnknownBefore;
                if(!this.StringEqualWithUnknownCharacters(value, index, this.RegexWildcardFormat, section.Start, section.Length))
                    return new Result(startIndex, -1);
                index  += section.Length + section.WildcardUnknownAfter;
                length -= section.WildcardUnknownBefore + section.Length + section.WildcardUnknownAfter;

                if(m_resultMustMatchAtEnd && m_sections.Length == 1)
                    return new Result(startIndex, length == 0 ? originalLength : -1);
                
                firstIndex   = 0;
                sectionIndex = 1;
            }

            if(m_resultMustMatchAtEnd) {
                var section = m_sections[m_sections.Length - 1];
                int pos     = startIndex + originalLength - section.WildcardUnknownAfter - section.Length;
                if(pos - section.WildcardUnknownBefore < index || !this.StringEqualWithUnknownCharacters(value, pos, this.RegexWildcardFormat, section.Start, section.Length))
                    return new Result(startIndex, -1);
                lastIndex = startIndex + originalLength;
                length   -= section.WildcardUnknownBefore + section.Length + section.WildcardUnknownAfter;
            }

            int last        = -1;
            int lastSection = m_sections.Length - (m_resultMustMatchAtEnd ? 1 : 0);
            
            while(sectionIndex < lastSection && length > 0) {
                var section = m_sections[sectionIndex];
                if(section.Length > 0) {
                    last = this.StringIndexOfWithUnknownCharacters(value, index, length, in section);
                    if(last < 0)
                        return new Result(startIndex, -1);
                    if(sectionIndex <= 1 && firstIndex < 0)
                        firstIndex = last;
                    var new_index = last + section.WildcardUnknownBefore + section.Length + section.WildcardUnknownAfter;
                    length       -= new_index - index;
                    index         = new_index;
                } else {
                    // case where format='??'  or  'aa*?'
                    last    = index;
                    index  += section.WildcardUnknownBefore + section.WildcardUnknownAfter;
                    length -= section.WildcardUnknownBefore + section.WildcardUnknownAfter;
                }
                sectionIndex++;
            }

            if(sectionIndex != lastSection || length < 0)
                return new Result(startIndex, -1);

            if(!m_resultMustMatchAtEnd) {
                var section = m_sections[m_sections.Length - 1];
                lastIndex   = last + section.WildcardUnknownBefore + section.Length + section.WildcardUnknownAfter;
            }

            return new Result(firstIndex, lastIndex - firstIndex);
        }
        #endregion
        #region Matches()
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public IEnumerable<Result> Matches(string value) {
            return this.Matches(value, 0, value.Length);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public IEnumerable<Result> Matches(string value, int startIndex) {
            return this.Matches(value, startIndex, value.Length - startIndex);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public IEnumerable<Result> Matches(string value, int startIndex, int length) {
            // special case, if format = '*' or ''
            if(m_sections.Length == 0) {
                if(m_isMatchAll)
                    // if format='*' return only one match instead of infinity match
                    yield return new Result(startIndex, 0);
                yield break;
            }

            int end = startIndex + length;
            while(true) {
                var res = this.Match(value, startIndex, length);

                if(res.Length >= 0) {
                    yield return res;
                    if(m_resultMustMatchAtStart || m_resultMustMatchAtEnd)
                        yield break;
                    // dont allow overlapping results
                    startIndex = res.Index + res.Length;
                    length     = end - startIndex;
                } else
                    yield break;
            }
        }
        #endregion
        #region static ToRegex()
        public enum RegexFormat {
            DotNet,
            /// <summary>
            ///     The string to be passed in sql, ie: "where searchcolumn ~ value"
            /// </summary>
            SQL,
        }
        public string ToRegex(RegexFormat regex_format = RegexFormat.DotNet) {
            int capacity = this.RegexWildcardFormat.Length;
            if(capacity <= 4096)
                capacity *= 2;
            else {
                // fast approximate count - not meant to be an exact count
                capacity += 2;
                for(int i = 0; i < this.RegexWildcardFormat.Length; i++) {
                    var c = this.RegexWildcardFormat[i];
                    if(c == m_wildcardAnything)
                        capacity += 2;
                    else if(!IsAlphaNumeric(c))
                        capacity++;
                }
            }

            var sb = new StringBuilder(capacity);

            if(regex_format == RegexFormat.SQL)
                sb.Append('\'');
            if(m_resultMustMatchAtStart)
                sb.Append('^');

            // special case: means the format = '*'
            if(m_sections.Length == 0 && m_isMatchAll)
                sb.Append(".*?"); // ? for non-greedy matching

            for(int j = 0; j < m_sections.Length; j++) {
                var section = m_sections[j];

                if(j > 0)
                    sb.Append(".*?"); // ? for non-greedy matching

                for(int i = 0; i < section.WildcardUnknownBefore; i++)
                    sb.Append('.');

                for(int i = 0; i < section.Length; i++) {
                    var c = this.RegexWildcardFormat[section.Start + i];

                    if(c == m_wildcardUnknown)
                        sb.Append('.');
                    else if(regex_format == RegexFormat.SQL && c == '\'')
                        sb.Append("''");
                    else {
                        if(!IsAlphaNumeric(c))
                            sb.Append('\\');
                        sb.Append(c);
                    }
                }

                for(int i = 0; i < section.WildcardUnknownAfter; i++)
                    sb.Append('.');
            }

            if(m_resultMustMatchAtEnd)
                sb.Append('$');
            if(regex_format == RegexFormat.SQL)
                sb.Append('\'');

            return sb.ToString();

            bool IsAlphaNumeric(char c) {
                //char.IsLetterOrDigit(c)
                return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            }
        }
        /// <summary>
        ///     Converts to a RegularExpression.
        ///     Typically it will run at least 3x slower than this implementation.
        /// </summary>
        public System.Text.RegularExpressions.Regex ToRegex(System.Text.RegularExpressions.RegexOptions regex_options) {
            // this will actually rewrite the regex to be more optimal
            return new Text.RegularExpressions.Regex(this.ToRegex(RegexFormat.DotNet), regex_options);
        }
        /// <param name="wildcard_format">The wildcard pattern. ex: '20??-01-01*'</param>
        /// <param name="wildcard_anything_character">Non-greedy matching (least characters)</param>
        public static string ToRegex(string wildcard_format, SearchOption option = SearchOption.ExactMatch, char wildcard_unknown_character = DEFAULT_WILDCARD_UNKNOWN, char wildcard_anything_character = DEFAULT_WILDCARD_ANYTHING, RegexFormat regex_format = RegexFormat.DotNet) {
            return new WildcardRegex(wildcard_format, option, wildcard_unknown_character, wildcard_anything_character)
                .ToRegex(regex_format);
        }
        /// <summary>
        ///     Converts to a RegularExpression.
        ///     Typically it will run at least 3x slower than this implementation.
        /// </summary>
        /// <param name="wildcard_format">The wildcard pattern. ex: '20??-01-01*'</param>
        /// <param name="wildcard_anything_character">Non-greedy matching (least characters)</param>
        public static System.Text.RegularExpressions.Regex ToRegex(string wildcard_format, System.Text.RegularExpressions.RegexOptions regex_options, SearchOption option = SearchOption.ExactMatch, char wildcard_unknown_character = DEFAULT_WILDCARD_UNKNOWN, char wildcard_anything_character = DEFAULT_WILDCARD_ANYTHING) {
            // rewrites the regex to be more efficient, at the cost of using our own parser
            return new WildcardRegex(wildcard_format, option, wildcard_unknown_character, wildcard_anything_character)
                .ToRegex(regex_options);


            // if you just want a simple version that converts to a .net regex with no rewriting
            //
            //var sb = new StringBuilder(wildcard_format.Length * 2);
            //
            //if(option == SearchOption.ExactMatch || option == SearchOption.StartsWith)
            //    sb.Append('^');
            //
            //for(int i = 0; i < wildcard_format.Length; i++) {
            //    var c = wildcard_format[i];
            //
            //    if(c == wildcard_unknown_character)
            //        sb.Append('.');
            //    else if(c == wildcard_anything_character)
            //        sb.Append(".*?"); // ? for non-greedy matching
            //    else {
            //        if(!IsAlphaNumeric(c))
            //            sb.Append('\\');
            //        sb.Append(c);
            //    }
            //}
            //
            //if(option == SearchOption.ExactMatch || option == SearchOption.EndsWith)
            //    sb.Append('$');
            //
            //return new System.Text.RegularExpressions.Regex(sb.ToString());
            //
            //bool IsAlphaNumeric(char c) {
            //    //char.IsLetterOrDigit(c)
            //    return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            //}
        }
        #endregion

        // todo: should have pre-built matchers for cases: exact match, partial match without ?/*, startswith with only ending ?, endswith with only starting ?
        // this should massively speed up those cases
        // potential conceptual flaw: all matches must be greedy because searching *.exe for files should return abc.exe and not .exe
        // unclear whether that means only return results expanded to include the end of string or not.

        #region ToString()
        public override string ToString() {
            return $"{this.RegexWildcardFormat} - {this.Option.ToString()}";
        }
        #endregion

        #region private ParseSearchFormat()
        private ConsecutiveParseSection[] ParseSearchFormat(string format) {
            var sections = this.ParseSearchFormatSections(format)
                .Where(o => o.Length > 0) // avoids empty sections in cases such as 'aa**aa', '*aa' and 'aa*'
                .Select(o => new ParsingSection(){ Start = o.Index, Length = o.Length })
                .ToList();

            for(int i = 0; i < sections.Count; i++) {
                var section = sections[i];
                
                // TrimStart(this.WildcardUnknown)
                while(section.Length > 0 && format[section.Start] == m_wildcardUnknown) {
                    section.Length--;
                    section.Start++;
                    section.WildcardUnknownBefore++;
                }
                // TrimEnd(this.WildcardUnknown)
                while(section.Length > 0 && format[section.Start + section.Length - 1] == m_wildcardUnknown) {
                    section.Length--;
                    section.WildcardUnknownAfter++;
                }
            }

            // merge '??' section with prev
            // ex: 'abc*??*456' -> 'abc??*456'
            int index = 1;
            while(index < sections.Count - (m_resultMustMatchAtEnd ? 1 : 0)) {
                var section = sections[index];
                if(section.Length == 0 && (section.WildcardUnknownBefore > 0 || section.WildcardUnknownAfter > 0)) {
                    sections[index - 1].WildcardUnknownAfter += section.WildcardUnknownBefore + section.WildcardUnknownAfter;
                    sections.RemoveAt(index);
                } else 
                    index++;
            }
            // note: can't merge a starting '??' section with the following one, because the returned matches would be different

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

            var res = new ConsecutiveParseSection[sections.Count];
            for(int i = 0; i < res.Length; i++) {
                var section = sections[i];

                // find longest stretch of non-WildcardUnknown characters
                // could also look at the stretch with the most repeated characters, which should speed up the search
                var best_sub_section = SplitPosition(format, section.Start, section.Length, m_wildcardUnknown)
                    .Select(o => {
                        int duplicates   = 0;
                        int consecutives = 0;
                        // very common case: the entire section does not contain any ?, which means we have only one section
                        if(o.Length != section.Length){
                            var visitedChars = new HashSet<char>(o.Length);
                            char prev = '\0';
                            for(int j = 0; j < o.Length; j++) {
                                var c = format[o.Index + j];
                                if(!visitedChars.Add(c))
                                    duplicates++;
                                if(j > 0 && c == prev)
                                    consecutives++;
                                prev = c;
                            }
                            duplicates -= consecutives;
                        }
                        return new { o.Index, o.Length, duplicates, consecutives };
                    })
                    // trying to figure out if searching '00' over '12345' is better
                    .OrderByDescending(o => (o.consecutives * 2 + 1) * (o.duplicates * 1.5 + 1)) // * o.Length
                    .ThenByDescending(o => o.consecutives)
                    .ThenByDescending(o => o.duplicates)
                    .ThenByDescending(o => o.Length)
                    .First();

                res[i] = new ConsecutiveParseSection(
                    section.Start, 
                    section.Length, 
                    section.WildcardUnknownBefore, 
                    section.WildcardUnknownAfter, 
                    format.Substring(best_sub_section.Index, best_sub_section.Length), //format.Substring(section.Start, section.Length),
                    best_sub_section.Index - section.Start);
            }
            return res;
        }
        private sealed class ParsingSection {
            public int Start;
            public int Length;
            public int WildcardUnknownBefore;
            public int WildcardUnknownAfter;
        }
        #endregion
        #region private ParseSearchFormatSections()
        /// <summary>
        ///     basically does format.Split(WildcardAnything)
        /// </summary>
        private IEnumerable<SplitPositionResult> ParseSearchFormatSections(string format) {
            int start = 0;
            int len   = 0;

            for(int i = 0; i < format.Length; i++) {
                var c = format[i];
                len++;

                if(c == m_wildcardAnything) {
                    if(len > 1)
                        yield return new SplitPositionResult(start, len - 1);

                    start = i + 1;
                    len   = 0;
                }
            }
            if(len > 0)
                yield return new SplitPositionResult(start, len);
        }
        private readonly struct SplitPositionResult {
            public readonly int Index;
            public readonly int Length;
            public SplitPositionResult(int index, int length) {
                this.Index  = index;
                this.Length = length;
            }
        }
        #endregion

        #region private SimpleEquals()
        /// <summary>
        ///     Direct string.Equals()
        /// </summary>
        private Result SimpleEquals(string value, int startIndex, int length) {
            var section    = m_sections[0];
            var search     = section.Search;
            var extraChars = section.WildcardUnknownBefore + section.WildcardUnknownAfter;
            var diff       = length - search.Length - extraChars;

            if(diff < 0 || (m_resultMustMatchAtEnd && diff != 0))
                return new Result(startIndex, -1);

            // ideally would use string.Equals(), but need start/len
            int cmp = string.CompareOrdinal(
                value,
                startIndex + section.WildcardUnknownBefore,
                search,
                0,
                search.Length - section.WildcardUnknownAfter);

            return new Result(startIndex, cmp != 0 ? -1 : search.Length + extraChars);
        }
        #endregion
        #region private SimpleContains()
        /// <summary>
        ///     Searching in a way that can be done with just one IndexOf()
        ///     this may include patterns such as: '*??aaaaa?*??*' but not '*aaaa?bbb*'
        /// </summary>
        private Result SimpleContains(string value, int startIndex, int length) {
            var section    = m_sections[0];
            var search     = section.Search;
            var extraChars = section.WildcardUnknownBefore + section.WildcardUnknownAfter;
            var diff       = length - search.Length - extraChars;

            if(diff < 0)
                return new Result(startIndex, -1);

            var compareInfo = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;

            //value.IndexOf(section.Search, startIndex, length, StringComparison.Ordinal);
            int pos = compareInfo.IndexOf(
                value,
                section.Search,
                startIndex + section.WildcardUnknownBefore,
                length - extraChars,
                System.Globalization.CompareOptions.Ordinal);
            
            if(pos < 0)
                return new Result(startIndex, -1);
            else
                return new Result(pos - section.WildcardUnknownBefore, search.Length + extraChars);
        }
        #endregion
        #region private StringEqualWithUnknownCharacters()
        /// <summary>
        ///     Returns true if the strings are equal, assuming search may contain WildcardUnknown '?'.
        ///     Search may not contain any WildcardAnything '*'.
        /// </summary>
        private bool StringEqualWithUnknownCharacters(string source, int sourceIndex, string search, int searchIndex, int count) {
            // ideally this would be a string.Equals() for faster speed, but there is no overload to specify start/length
            //string.CompareOrdinal(value, index, section.Search, 0, section.Length);

            for(int i = 0; i < count; i++) {
                var d = search[searchIndex + i];

                if(d != this.m_wildcardUnknown && source[sourceIndex + i] != d)
                    return false;
            }
            return true;
        }
        #endregion
        #region private StringIndexOfWithUnknownCharacters()
        /// <summary>
        ///     Returns the index of section, assuming the section may contain WildcardUnknown '?'.
        ///     Search may not contain any WildcardAnything '*'.
        /// </summary>
        private int StringIndexOfWithUnknownCharacters(string source, int index, int length, in ConsecutiveParseSection section) {
            int charsBeforeSearch = section.WildcardUnknownBefore + section.SearchIndex;
            int charsAfterSearch  = section.Length - section.Search.Length - section.SearchIndex;
            
            index          += charsBeforeSearch;
            length         -= charsBeforeSearch + charsAfterSearch + section.WildcardUnknownAfter;
            var compareInfo = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;

            while(length > 0) {
                //value.IndexOf(section.Search, startIndex, length, StringComparison.Ordinal);
                int pos = compareInfo.IndexOf(
                    source,
                    section.Search,
                    index,
                    length,
                    System.Globalization.CompareOptions.Ordinal);
                if(pos < 0)
                    return -1;

                bool startMatches = !section.ContainsCharsBeforeSearchIndex || this.StringEqualWithUnknownCharacters(source, pos - section.SearchIndex, this.RegexWildcardFormat, section.Start, section.SearchIndex);
                if(!startMatches) {
                    var diff = (pos + 1) - index;
                    index  += diff;
                    length -= diff;
                    continue;
                }
                bool endMatches = !section.ContainsCharsAfterSearchIndex || this.StringEqualWithUnknownCharacters(source, pos + section.Search.Length, this.RegexWildcardFormat, section.Start + section.SearchIndex + section.Search.Length, charsAfterSearch);
                if(!endMatches) {
                    var diff = (pos + 1) - index;
                    index  += diff;
                    length -= diff;
                    continue;
                }

                return pos - section.SearchIndex - section.WildcardUnknownBefore;
            }

            return -1;
        }
        #endregion
        #region private static SplitPosition()
        /// <summary>
        ///     Same as string.Split(), but for returns positions instead.
        ///     ex: "abcde".SplitPosition(1, 4, new []{'b'}) = {(2,3)}
        /// </summary>
        private static IEnumerable<Result> SplitPosition(string source, int startIndex, int length, char separator) {
            int start = startIndex;
            int max   = startIndex + length;
            int i     = startIndex;
            for(; i < max; i++) {
                if(source[i] == separator) {
                    yield return new Result(start, i - start);
                    start = i + 1;
                }
            }
            yield return new Result(start, i - start);
        }
        #endregion

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
            Partial,
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
        #region private readonly struct ConsecutiveParseSection
        /// <summary>
        ///     Represents a section of consecutive characters without any WILDCARD_ANYTHING * in it.
        ///     This may include multiple WILDCARD_UNKNOWN ?.
        /// </summary>
        private readonly struct ConsecutiveParseSection {
            // example:         search_format = '???ABC?1234?????*ZZ'
            //          current parse section = '???ABC?1234?????'
            public readonly int Start;                           // 3
            public readonly int Length;                          // 8
            public readonly int WildcardUnknownBefore;           // 3, how many WILDCARD_UNKNOWN ? precede the start of the section.
            public readonly int WildcardUnknownAfter;            // 5, how many WILDCARD_UNKNOWN ? follow the end of the section.
            public readonly string Search;                       // '1234', the optimal stretch of characters without WILDCARD_UNKNOWN ? to search for (takes into account length and # consecutive/repeated chars)
            public readonly int SearchIndex;                     // 4, the index starting from this.Start
            public readonly bool ContainsCharsBeforeSearchIndex; // true (true if theres any characters preceding '1234' and after the initial '???')
            public readonly bool ContainsCharsAfterSearchIndex;  // false (true if theres any characters following '1234' and before the final '?????')

            public ConsecutiveParseSection(int start, int length, int wildcardBefore, int wildcardAfter, string search, int searchIndex) {
                this.Start                          = start;
                this.Length                         = length;
                this.WildcardUnknownBefore          = wildcardBefore;
                this.WildcardUnknownAfter           = wildcardAfter;
                this.Search                         = search;
                this.SearchIndex                    = searchIndex;
                this.ContainsCharsBeforeSearchIndex = searchIndex > 0;
                this.ContainsCharsAfterSearchIndex  = searchIndex + search.Length < length;
            }
        }
        #endregion
        public readonly struct Result {
            public readonly int Index;
            public readonly int Length;
            public Result(int index, int length) {
                this.Index  = index;
                this.Length = length;
            }
        }
    }
}