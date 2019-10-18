Adaptive Radix Tree
=========

This "library" provides a C# implementation of the Adaptive Radix Tree or ART over a stream.
The ART operates similar to a traditional radix tree but avoids the wasted space of internal
nodes by changing the node size. It makes use of different node sizes (4, 8, 16, 32, 64, 128, 256)
in order to save space and maximize cache locality. This isn't a strict implementation as per the 
paper, as C# does not have access to the required CPU intrinsics justifying 48 items per node.

As a radix tree, it provides the following:
 * O(k) operations. In many cases, this can be faster than a hash table since
   the hash function is an O(k) operation, and hash tables have very poor cache locality.
 * Minimum / Maximum value lookups
 * Prefix compression
 * Ordered iteration
 * Prefix based iteration

As for this specific implementation, it additionally provides:
 * Hamming distance searching (near-neighboor search)
 * Regular expression indexed searching
 * Regular expression indexed searching with Hamming distance
 * StartsWith iterator
 * Range iterator
 * Optimize() to ensure optimal data locality (for faster read-only usage)
 * CalculateShortestUniqueKey()
 * Advanced tree statistics for parameter fine-tuning

Usage
-------

Simply use the relevant files and change the constants to optimize your usage.


Note
-------

C# Adaptive Radix Tree implementation + Fuzzy String Match stuff + License Plate Index + NGram Index

For now this is a placeholder for bits of code written on my own time for fun. 
Most of the code is the AdaptiveRadixTree implementation, which includes multiple nifty search options.

The FuzzyStringMatch class is part of what I intended to use the AdaptiveRadixTree for, only to realize I wouldn't need it in the end.
It still provides efficient implementation of various fuzzy string matching algorithms with multiple micro-optimisations.
DynamicMemoryStream was taken directly from TimeSeriesDB, another personal project I haven't made public yet.
SpellingSuggestor is meant to be an efficient Hamming distance near-neighboor search, running in O(1) on average.
LicensePlateIndex is just a specialized use of SpellingSuggestor.

This will likely be split into multiple repositories later and have proper code coverage. Until then, this is just a code dump.


References
----------

Related works:

* [The Adaptive Radix Tree: ARTful Indexing for Main-Memory Databases](http://www-db.in.tum.de/~leis/papers/ART.pdf)
