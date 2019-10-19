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
    /// </summary>
    public sealed class WildcardRegex {
        private const char DEFAULT_WILDCARD_UNKNOWN  = '?';
        private const char DEFAULT_WILDCARD_ANYTHING = '*';

        public readonly string Format;
        public readonly SearchOption Option;
        
        /// <summary>?</summary>
        private readonly char WildcardUnknown;
        /// <summary>*</summary>
        private readonly char WildcardAnything;

            
        private readonly bool m_resultMustMatchAtStart; // result/match must start at 0.
        private readonly bool m_resultMustMatchAtEnd;   // result/match must end at comparand.Length.
        private readonly int m_totalCharacters;
        private readonly ConsecutiveParseSection[] m_sections; // if Length==0, means format = '*'

        #region constructors
        /// <param name="format">The wildcard pattern. ex: '20??-01-01*'</param>
        public WildcardRegex(string format, SearchOption option = SearchOption.ExactMatch, char wildcard_unknown_character = DEFAULT_WILDCARD_UNKNOWN, char wildcard_anything_character = DEFAULT_WILDCARD_ANYTHING) {
            if(string.IsNullOrEmpty(format))
                throw new FormatException(nameof(format));

            this.Format           = format;
            this.Option           = option;
            this.WildcardUnknown  = wildcard_unknown_character;
            this.WildcardAnything = wildcard_anything_character;

            m_sections        = this.ParseSearchFormat(format);
            m_totalCharacters = m_sections.Sum(section => section.Length + section.WildcardUnknownBefore + section.WildcardUnknownAfter);

            if(option == SearchOption.ExactMatch) {
                m_resultMustMatchAtStart = format[0] != this.WildcardAnything;
                m_resultMustMatchAtEnd   = format[format.Length - 1] != this.WildcardAnything;
            }
        }
        #endregion

        #region IsMatch()
        public bool IsMatch(string value) {
            return this.Match(value, 0, value.Length).length >= 0;
        }
        public bool IsMatch(string value, int startIndex) {
            return this.Match(value, startIndex, value.Length - startIndex).length >= 0;
        }
        public bool IsMatch(string value, int startIndex, int length) {
            return this.Match(value, startIndex, length).length >= 0;
        }
        #endregion
        #region Match()
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public (int start, int length) Match(string value) {
            return this.Match(value, 0, value.Length);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public (int start, int length) Match(string value, int startIndex) {
            return this.Match(value, startIndex, value.Length - startIndex);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public (int start, int length) Match(string value, int startIndex, int length) {
            if(length < m_totalCharacters)
                return (startIndex, -1);


        }
        #endregion
        #region Matches()
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public IEnumerable<(int start, int length)> Matches(string value) {
            return this.Matches(value, 0, value.Length);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public IEnumerable<(int start, int length)> Matches(string value, int startIndex) {
            return this.Matches(value, startIndex, value.Length - startIndex);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public IEnumerable<(int start, int length)> Matches(string value, int startIndex, int length) {
            // special case, if format = '*', then return only one match instead of infinity match
            if(m_sections.Length == 0) {
                yield return (startIndex, 0);
                yield break;
            }

            int end = startIndex + length;
            while(true) {
                var res = this.Match(value, startIndex, length);

                if(res.length >= 0) {
                    yield return res;
                    // dont allow overlapping results
                    startIndex = res.start + res.length;
                    length     = end - startIndex;
                } else
                    yield break;
            }
        }
        #endregion
        #region ToRegex()
        public string ToRegex(RegexFormat encoding = RegexFormat.DotNet) {
            int capacity = this.Format.Length;
            if(capacity <= 4096)
                capacity *= 2;
            else {
                // fast approximate count - not meant to be an exact count
                capacity += 2;
                for(int i = 0; i < this.Format.Length; i++) {
                    var c = this.Format[i];
                    if(c == this.WildcardAnything)
                        capacity += 2;
                    else if(!IsAlphaNumeric(c))
                        capacity++;
                }
            }

            var sb = new StringBuilder(capacity);

            if(encoding == RegexFormat.SQL)
                sb.Append('\'');
            if(m_resultMustMatchAtStart)
                sb.Append('^');

            for(int j = 0; j < m_sections.Length; j++) {
                var section = m_sections[j];

                if(j > 0)
                    sb.Append(".*");

                for(int i = 0; i < section.WildcardUnknownBefore; i++)
                    sb.Append('.');

                for(int i = 0; i < section.Length; i++) {
                    var c = this.Format[section.Start + i];

                    if(c == this.WildcardUnknown)
                        sb.Append('.');
                    else if(encoding == RegexFormat.SQL && c == '\'')
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
            if(encoding == RegexFormat.SQL)
                sb.Append('\'');

            return sb.ToString();

            bool IsAlphaNumeric(char c) {
                //char.IsLetterOrDigit(c)
                return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            }
        }
        #endregion

        #region private ParseSearchFormat()
        private ConsecutiveParseSection[] ParseSearchFormat(string format) {
            var sections = this.ParseSearchFormatSections(format)
                .Where(o => o.len > 0) // avoids empty sections in cases such as 'aa**aa', '*aa' and 'aa*'
                .Select(o => new ParsingSection(){ Start = o.start, Length = o.len })
                .ToList();

            for(int i = 0; i < m_sections.Length; i++) {
                var section = sections[i];
                
                // TrimStart(this.WildcardUnknown)
                while(section.Length > 0 && format[section.Start] == this.WildcardUnknown) {
                    section.Length--;
                    section.Start++;
                    section.WildcardUnknownBefore++;
                }
                // TrimEnd(this.WildcardUnknown)
                while(section.Length > 0 && format[section.Start + section.Length - 1] == this.WildcardUnknown) {
                    section.Length--;
                    section.WildcardUnknownAfter++;
                }
            }

            var res = new ConsecutiveParseSection[sections.Count];
            for(int i = 0; i < res.Length; i++) {
                var section = sections[i];
                res[i] = new ConsecutiveParseSection(section.Start, section.Length, section.WildcardUnknownBefore, section.WildcardUnknownAfter);
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
        #endregion


        public enum SearchOption {
            /// <summary>
            ///     equivalent to "value = 'searchstring'"
            /// </summary>
            ExactMatch,
            /// <summary>
            ///     equivalent to "value LIKE '%searchstring%'"
            /// </summary>
            Partial,
        }
        public enum RegexFormat {
            DotNet,
            /// <summary>
            ///     The string to be passed in sql, ie: "where searchcolumn ~ value"
            /// </summary>
            SQL,
        }

        /// <summary>
        ///     Represents a section of consecutive characters without any WILDCARD_ANYTHING in it.
        ///     This may include multiple WILDCARD_UNKNOWN.
        /// </summary>
        private readonly struct ConsecutiveParseSection {
            public readonly int Start;
            public readonly int Length;
            public readonly int WildcardUnknownBefore; // how many WILDCARD_UNKNOWN are at the start of the section.
            public readonly int WildcardUnknownAfter;  // how many WILDCARD_UNKNOWN are at the end of the section.

            public ConsecutiveParseSection(int start, int length, int wildcardBefore, int wildcardAfter) {
                this.Start                 = start;
                this.Length                = length;
                this.WildcardUnknownBefore = wildcardBefore;
                this.WildcardUnknownAfter  = wildcardAfter;
            }
        }
    }
}
