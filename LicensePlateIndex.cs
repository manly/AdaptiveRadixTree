using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;


namespace System.Collections.Specialized
{

    public sealed class LicensePlateIndex {
        private readonly SpellingSuggestor m_spellingSuggestor;

        #region constructors
        public LicensePlateIndex(int delete_distance = SpellingSuggestor.DEFAULT_DELETE_DISTANCE) {
            m_spellingSuggestor = new SpellingSuggestor(delete_distance);
        }
        #endregion

        #region Items
        /// <summary>
        ///     O(n) + number of permutations not matching any item.
        /// </summary>
        public IEnumerable<string> Items => m_spellingSuggestor.Items;
        #endregion

        #region Add()
        /// <summary>
        ///     Average: O(1) search + O(log c) binary search    (c=permutations sharing value)
        /// </summary>
        public void Add(string value) {
            value = NormalizeLicensePlate(value);
            m_spellingSuggestor.Add(value);
        }
        #endregion
        #region AddRange()
        public void AddRange(IEnumerable<string> items) {
            foreach(var item in items)
                this.Add(item);
        }
        #endregion
        #region Remove()
        /// <summary>
        ///     Average: O(1) search + O(log c) binary search    (c=permutations sharing value)
        /// </summary>
        public bool Remove(string value) {
            value = NormalizeLicensePlate(value);
            return m_spellingSuggestor.Remove(value);
        }
        #endregion
        #region RemoveRange()
        public void RemoveRange(IEnumerable<string> items) {
            foreach(var item in items)
                this.Remove(item);
        }
        #endregion
        #region Contains()
        /// <summary>
        ///     Average: O(1) search + O(log c) binary search    (c=permutations sharing value)
        /// </summary>
        public bool Contains(string value) {
            value = NormalizeLicensePlate(value);
            return m_spellingSuggestor.Contains(value);
        }
        #endregion

        #region Lookup()
        /// <summary>
        ///     Average: O(1) * number_of_permutation
        ///     Returns suggested spellings that match value within a given true Damereau-Levenshtein distance.
        /// </summary>
        /// <param name="delete_distance">Default: -1. -1 returns everything.</param>
        public List<SpellingSuggestor.Result> Lookup(string value, int delete_distance = -1, SpellingSuggestor.Verbosity verbosity = SpellingSuggestor.Verbosity.Top) {
            value = NormalizeLicensePlate(value);
            var matches = m_spellingSuggestor.Lookup(value, delete_distance, verbosity);

            int max = matches.Count;
            for(int i = 0; i < max; i++)
                matches[i].Similarity = CalculateLicensePlateSimilarity(value, matches[i].Value);

            // now that similarities are calculated, sort results
            matches.Sort();

            return matches;
        }
        #endregion
        #region TrimExcess()
        /// <summary>
        ///     O(n)
        /// </summary>
        public void TrimExcess() {
            m_spellingSuggestor.TrimExcess();
        }
        #endregion

        #region private static NormalizeLicensePlate()
        private static string NormalizeLicensePlate(string value) {
            //value = value.Replace(" ", string.Empty);
            // note: UPPER case is relevant because license plate are printed in upper cases, and visual similarity is taken into account
            return FuzzyStringMatch.RemoveDiacritics(value.Trim()).ToUpperInvariant();
        }
        #endregion
        #region private static CalculateLicensePlateSimilarity()
        private static double CalculateLicensePlateSimilarity(string normalized_input, string normalized_match) {
            //return FuzzyStringMatch.CombinedWordSimilarity(normalized_input, normalized_match);
            return FuzzyStringMatch.CombinedWordEditDistance(normalized_input, normalized_match);
        }
        #endregion
    }
}
