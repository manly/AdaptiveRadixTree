# AdaptiveRadixTree
C# AdaptiveRadixTree implementation + FuzzyStringMatch stuff + LicensePlateIndex

For now this is a placeholder for bits of code written on my own time for fun. 
Most of the code is the AdaptiveRadixTree implementation, which includes multiple nifty search options.


The FuzzyStringMatch class is part of what I intended to use the AdaptiveRadixTree for, only to realize I wouldn't need it in the end.
It still provides efficient implementation of various fuzzy string matching algorithms with multiple micro-optimisations.
DynamicMemoryStream was taken directly from TimeSeriesDB, another personal project I haven't made public yet.
SpellingSuggestor is meant to be an efficient Hamming distance near-neighboor search, running in O(1) on average.
LicensePlateIndex is just a specialized use of SpellingSuggestor.

This will likely be split into multiple repositories later and have proper code coverage. Until then, this is just a code dump.