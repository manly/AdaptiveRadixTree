//#define IMPLEMENT_DICTIONARY_INTERFACES // might want to disable due to System.Linq.Enumerable extensions clutter
#define USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE // if you dont want any external dependencies, comment this. this is only used to avoid needless casts

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static System.Runtime.CompilerServices.MethodImplOptions;


namespace System.Collections.Specialized 
{
    /// <summary>
    ///     Adaptive Radix Tree. (space-optimized Trie)
    ///     
    ///     O(k) operations. (k = # of characters)
    ///     In many cases, this can be faster than a hash table since the hash function is an O(k) operation, as hash tables have very poor cache locality.
    ///     If you can fit everything in memory, you should just use a regular Radix Tree.
    ///     
    ///     Trie based data structure, achieves its performance, and space efficiency, by compressing the tree both vertically, 
    ///     i.e., if a node has no siblings it is merged with its parent, and horizontally, 
    ///     i.e., uses an array which grows as the number of children increases. 
    ///     Vertical compression reduces the tree height and horizontal compression decreases a node's size.
    ///     
    ///     This implementation is meant to handle billion+ entries and use Virtual Memory if need be. It stores keys efficiently.
    ///     This implementation does not contain any duplicated key information on nodes/leafs, and is meant for efficient memory usage.
    ///     This implementation has no unsafe code and is made purposefully to take advantage of CPU cache locality.
    ///     This implementation assumes the data cannot all fit in memory, hence the reliance on Stream for all storage.
    ///     This implementation is meant to run in a realtime environment, with no bad worst-case scenarios and no amortised costs. 
    ///     Nothing is recursive, memory footprint grows and shrinks with usage, and avoids growing beyond initial buffers.
    ///     This implementation is inspired from the paper noted in the remarks, but due to safe context and lack of CPU intrinsics access, 
    ///     uses a more dynamic approach which takes advantage of whats possible in .NET.
    ///     For technical reasons, this can only store immutable keys/values. If you require live/non-immutable values, use instead int/long/identifier to your data.
    ///     With all this said, C# just isn't the right language to implement this efficiently, even with unsafe context enabled. C/C++ would run this 2-3x faster due to direct read/write data structures, CPU intrinsics, and malloc() speed.
    /// </summary>
    /// <example>
    ///   banana                 [b]
    ///   bandana               / | \
    ///   bank                 /  |  \
    ///   beer                /   |   \
    ///   brooklyn        [an] [eer\0] [rooklyn\0]
    ///                  /  | \
    ///                 /   |  \
    ///                /    |   \
    ///         [ana\0] [dana\0] [k\0]
    /// </example>
    /// <remarks>
    ///     The Adaptive Radix Tree: ARTful Indexing for Main-Memory Databases
    ///     http://www-db.in.tum.de/~leis/papers/ART.pdf
    /// </remarks>
    public class AdaptiveRadixTree<TKey, TValue> : ICollection
#if IMPLEMENT_DICTIONARY_INTERFACES
        , IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
#endif
    {
        #region NOTES - INTERNAL DATA FORMAT
        // node4 & node8                         |  node16 & node32                       |  node64 & node128                      |  node256
        // **********************************************************************************************************************************************************
        // byte                  node_type       |  byte                  node_type       |  byte                  node_type       |  byte                  node_type
        // byte                  num_children    |  byte                  num_children    |  byte                  num_children    |  byte                  num_children
        // byte                  partial_length  |  byte                  partial_length  |  byte                  partial_length  |  byte                  partial_length
        // char[MAX_PREFIX_LEN]  partial         |  char[MAX_PREFIX_LEN]  partial         |  char[MAX_PREFIX_LEN]  partial         |  char[MAX_PREFIX_LEN]  partial
        // char[n]               keys (no-order) |  char[n]               keys (ordered)  |  char[256]             keys (index+1)  |  ---
        // NodePointer[n]        children        |  NodePointer[n]        children        |  NodePointer[n]        children        |  NodePointer[n]        children
        //
        // leaf
        // **************************************
        // byte                  node_type
        // var long              partial_length (1-9 bytes)
        // var long              value_length (1-9 bytes)
        // char[partial_length]  partial
        // byte[value_length]    value
        //
        //
        // NOTES
        // ******************************
        // The entire algorithm is based upon a "cache-agnostic B-tree" that is made in order to maximize CPU cache hits 
        // rather than assume all memory access are equal (ie; regular B-Tree).
        // As such, data locality is an absolute must, and consequently, not something that is prone to be implemented in C# in a safe context
        // Additionally, (static/unchanging) pointers are *required* to even make this work, 
        // which means we have to resort to manually manage our own memory without relying on memory pinning.
        // Since this code runs in a safe context (ie; without fixed sized buffers), we can't map the raw binary encoding into a struct.
        // Additionally, since the entire code doesnt use any struct/class internally to store data, the keys/values are required to be immutable.
        // If you do need to store live data, then make TValue==int/long which points to your live data.
        // Also, Adaptive Radix Tree encodes a final character at the end of keys (LEAF_NODE_KEY_TERMINATOR) in order to encode efficiently children branches.
        // As a consequence of this, all keys must go through EscapeLeafKeyTerminator() to 'silently' escape out LEAF_NODE_KEY_TERMINATOR
        // As a result of all this, the code runs entirely "devoid" of structs and reads/writes directly to binary data
        //
        // All operations follow an MVCC (multiversion concurrency control) approach, where changes are done on a copy of the node.
        //
        // No node may contain "partial_length==0" except for the root node.
        #endregion
    
        protected const int  MAX_PREFIX_LEN                = 8;  // the max number of key characters per non-leaf node.
        protected const int  NODE_POINTER_BYTE_SIZE        = 5;  // increase this value if you need more than 1.1 TB
        protected const byte LEAF_NODE_KEY_TERMINATOR      = 0;  // terminate keys of leafs with this value
        protected const byte LEAF_NODE_KEY_ESCAPE_CHAR     = LEAF_NODE_KEY_TERMINATOR == 0 ? 255 : 0;
        protected const byte LEAF_NODE_KEY_ESCAPE_CHAR2    = LEAF_NODE_KEY_TERMINATOR != 0 && LEAF_NODE_KEY_ESCAPE_CHAR != 0 ? 0 : (LEAF_NODE_KEY_TERMINATOR != 1 && LEAF_NODE_KEY_ESCAPE_CHAR != 1 ? 1 : 2); // terminator
        protected const int  LEAF_NODE_PREFETCH_SIZE       = 64; // guesstimated, must be <= m_buffer.Length and >= MAX_VARINT64_ENCODED_SIZE * 2 + 1
        protected const int  LEAF_NODE_VALUE_PREFETCH_SIZE = 32; // guesstimated, must be <= m_buffer.Length, includes only data
        protected const int  MAX_VARINT64_ENCODED_SIZE     = 9;
        protected const int  BUFFER_SIZE                   = 4096;
    
    
        protected long m_rootPointer = 0; // root pointer address is always zero
        protected readonly MemoryManager m_memoryManager;
    
        // performance boost over memorymanager, reducing alloc()/free() to O(1)
        private readonly FixedSizeMemoryManager m_memoryManagerNode4;
        private readonly FixedSizeMemoryManager m_memoryManagerNode8;
        private readonly FixedSizeMemoryManager m_memoryManagerNode16;
        private readonly FixedSizeMemoryManager m_memoryManagerNode32;
        private readonly FixedSizeMemoryManager m_memoryManagerNode64;
        private readonly FixedSizeMemoryManager m_memoryManagerNode128;
        private readonly FixedSizeMemoryManager m_memoryManagerNode256;
    
        /// <summary>
        ///     The storage medium used.
        ///     By default will use a MemoryStream designed for efficient resizes.
        ///     Make sure that the implementation used supports efficient appends.
        ///     
        ///     Potential alternatives: use virtual alloc wrappers (https://github.com/71/ExpandableAllocator) to go beyond memory limit
        ///     Potential alternatives: wrap this for permanent storage.
        /// </summary>
        public readonly Stream Stream;
        protected readonly byte[] m_buffer = new byte[BUFFER_SIZE];
        public int Count => unchecked((int)Math.Min(this.LongCount, int.MaxValue));
        public long LongCount { get; private set; }
    
        protected readonly Buffer m_keyBuffer;
        protected readonly Buffer m_valueBuffer;
    
        // todo: add byte adjustment to leaves that says how many bytes to skip for partial key, thus avoiding regenerating leaves constantly
    
#if USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE
        protected readonly Action<TKey, Buffer>           m_keyEncoder;   // see GetDefaultEncoder(), can return LEAF_NODE_KEY_TERMINATOR
        protected readonly Action<TValue, Buffer>         m_valueEncoder; // see GetDefaultEncoder()
        protected readonly Func<byte[], int, int, TKey>   m_keyDecoder;   // see GetDefaultDecoder()
        protected readonly Func<byte[], int, int, TValue> m_valueDecoder; // see GetDefaultDecoder()
#else
        protected readonly Action<object, Buffer>         m_keyEncoder;   // see GetDefaultEncoder(), can return LEAF_NODE_KEY_TERMINATOR
        protected readonly Action<object, Buffer>         m_valueEncoder; // see GetDefaultEncoder()
        protected readonly Func<byte[], int, int, object> m_keyDecoder;   // see GetDefaultDecoder()
        protected readonly Func<byte[], int, int, object> m_valueDecoder; // see GetDefaultDecoder()
#endif
    
        #region constructors
        static AdaptiveRadixTree() {
            // sanity check constants
            if(MAX_PREFIX_LEN <= 1 || MAX_PREFIX_LEN >= 256)
                throw new ArgumentOutOfRangeException(nameof(MAX_PREFIX_LEN));
            if(NODE_POINTER_BYTE_SIZE < 1 || NODE_POINTER_BYTE_SIZE > 8)
                throw new ArgumentOutOfRangeException(nameof(NODE_POINTER_BYTE_SIZE));
            if(LEAF_NODE_PREFETCH_SIZE < 0 || LEAF_NODE_PREFETCH_SIZE > BUFFER_SIZE || LEAF_NODE_PREFETCH_SIZE < MAX_VARINT64_ENCODED_SIZE * 2 + 1)
                throw new ArgumentOutOfRangeException(nameof(LEAF_NODE_PREFETCH_SIZE));
            if(LEAF_NODE_VALUE_PREFETCH_SIZE < 0 || LEAF_NODE_VALUE_PREFETCH_SIZE > BUFFER_SIZE)
                throw new ArgumentOutOfRangeException(nameof(LEAF_NODE_VALUE_PREFETCH_SIZE));
            if(LEAF_NODE_KEY_ESCAPE_CHAR == LEAF_NODE_KEY_TERMINATOR)
                throw new ArgumentOutOfRangeException(nameof(LEAF_NODE_KEY_ESCAPE_CHAR));
            if(LEAF_NODE_KEY_ESCAPE_CHAR2 == LEAF_NODE_KEY_TERMINATOR || LEAF_NODE_KEY_ESCAPE_CHAR2 == LEAF_NODE_KEY_ESCAPE_CHAR)
                throw new ArgumentOutOfRangeException(nameof(LEAF_NODE_KEY_ESCAPE_CHAR2));
            if(MAX_VARINT64_ENCODED_SIZE < 1 || MAX_VARINT64_ENCODED_SIZE > 9)
                throw new ArgumentOutOfRangeException(nameof(MAX_VARINT64_ENCODED_SIZE));
            if(BUFFER_SIZE < CalculateNodeSize(NodeType.Node256) * 2)
                throw new ArgumentOutOfRangeException(nameof(m_buffer), "Invalid size");
        }
        /// <param name="storageStream">
        /// The stream used to store the data.
        /// The stream must be empty.
        /// If using memory backing of any kind, consider using a minimum capacity at or above 85000 bytes to avoid GC.Collect() calls on data that is likely to be long-lived.
        /// Also, make sure the stream is efficient at appending.
        /// </param>
        public AdaptiveRadixTree(Stream storageStream = null) 
            : this(storageStream, null, null, null, null) {}
        /// <summary>
        ///     Create an Adaptive Radix Tree with custom encoders/recoders.
        /// </summary>
        /// <param name="storageStream">
        /// The stream used to store the data.
        /// The stream must be empty.
        /// If using memory backing of any kind, consider using a minimum capacity at or above 85000 bytes to avoid GC.Collect() calls on data that is likely to be long-lived.
        /// Also, make sure the stream is efficient at appending.
        /// </param>
        /// <param name="keyEncoder">Default: null. Will use the default encoder if null. See GetDefaultEncoder().</param>
        /// <param name="valueEncoder">Default: null. Will use the default encoder if null. See GetDefaultEncoder().</param>
        /// <param name="keyDecoder">Default: null. Will use the default decoder if null. See GetDefaultDecoder().</param>
        /// <param name="valueDecoder">Default: null. Will use the default decoder if null. See GetDefaultDecoder().</param>
#if USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE
        public AdaptiveRadixTree(Stream storageStream, Action<TKey, Buffer> keyEncoder, Action<TValue, Buffer> valueEncoder, Func<byte[], int, int, TKey> keyDecoder, Func<byte[], int, int, TValue> valueDecoder) 
            : this(new MemoryManager(), storageStream, keyEncoder, valueEncoder, keyDecoder, valueDecoder) {
#else
        public AdaptiveRadixTree(Stream storageStream, Action<object, Buffer> keyEncoder, Action<object, Buffer> valueEncoder, Func<byte[], int, int, object> keyDecoder, Func<byte[], int, int, object> valueDecoder)
            : this(new MemoryManager(), storageStream, keyEncoder, valueEncoder, keyDecoder, valueDecoder) {
#endif
            if(storageStream != null && storageStream.Length != 0)
                throw new ArgumentException("Stream must be empty.", nameof(storageStream));
    
            this.Clear();
        }
        /// <summary>
        ///     Create an Adaptive Radix Tree with custom encoders/recoders.
        /// </summary>
        /// <param name="storageStream">
        /// The stream used to store the data.
        /// The stream must be empty.
        /// If using memory backing of any kind, consider using a minimum capacity at or above 85000 bytes to avoid GC.Collect() calls on data that is likely to be long-lived.
        /// Also, make sure the stream is efficient at appending.
        /// </param>
        /// <param name="keyEncoder">Default: null. Will use the default encoder if null. See GetDefaultEncoder().</param>
        /// <param name="valueEncoder">Default: null. Will use the default encoder if null. See GetDefaultEncoder().</param>
        /// <param name="keyDecoder">Default: null. Will use the default decoder if null. See GetDefaultDecoder().</param>
        /// <param name="valueDecoder">Default: null. Will use the default decoder if null. See GetDefaultDecoder().</param>
#if USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE
        protected AdaptiveRadixTree(MemoryManager memoryManager, Stream storageStream, Action<TKey, Buffer> keyEncoder, Action<TValue, Buffer> valueEncoder, Func<byte[], int, int, TKey> keyDecoder, Func<byte[], int, int, TValue> valueDecoder) {
#else
        protected AdaptiveRadixTree(MemoryManager memoryManager, Stream storageStream, Action<object, Buffer> keyEncoder, Action<object, Buffer> valueEncoder, Func<byte[], int, int, object> keyDecoder, Func<byte[], int, int, object> valueDecoder) {
#endif
            if(m_buffer.Length != BUFFER_SIZE)
                throw new ArgumentOutOfRangeException(nameof(BUFFER_SIZE), $"{nameof(m_buffer)}.Length must equal {nameof(BUFFER_SIZE)}");
    
            m_memoryManager = memoryManager;
    
            // allows O(1) alloc speed
            m_memoryManagerNode4   = new FixedSizeMemoryManager(CalculateNodeSize(NodeType.Node4),   size => this.Alloc(size), (address, len) => this.Free(address, len));
            m_memoryManagerNode8   = new FixedSizeMemoryManager(CalculateNodeSize(NodeType.Node8),   size => this.Alloc(size), (address, len) => this.Free(address, len));
            m_memoryManagerNode16  = new FixedSizeMemoryManager(CalculateNodeSize(NodeType.Node16),  size => this.Alloc(size), (address, len) => this.Free(address, len));
            m_memoryManagerNode32  = new FixedSizeMemoryManager(CalculateNodeSize(NodeType.Node32),  size => this.Alloc(size), (address, len) => this.Free(address, len));
            m_memoryManagerNode64  = new FixedSizeMemoryManager(CalculateNodeSize(NodeType.Node64),  size => this.Alloc(size), (address, len) => this.Free(address, len));
            m_memoryManagerNode128 = new FixedSizeMemoryManager(CalculateNodeSize(NodeType.Node128), size => this.Alloc(size), (address, len) => this.Free(address, len));
            m_memoryManagerNode256 = new FixedSizeMemoryManager(CalculateNodeSize(NodeType.Node256), size => this.Alloc(size), (address, len) => this.Free(address, len));
    
            // use default capacity >= 85k to avoid GC.Collect() since this memory is meant to be long-lived
            this.Stream = storageStream ?? new TimeSeriesDB.IO.DynamicMemoryStream(131072);
    
            m_keyEncoder   = keyEncoder   ?? GetDefaultEncoder<TKey>()   ?? throw new ArgumentNullException(nameof(keyEncoder),   $"The key encoder {nameof(TKey)}={typeof(TKey).Name} has no encoder specified. You must provide one or use a primitive/string/byte[].");
            m_valueEncoder = valueEncoder ?? GetDefaultEncoder<TValue>() ?? throw new ArgumentNullException(nameof(valueEncoder), $"The value encoder {nameof(TValue)}={typeof(TValue).Name} has no encoder specified. You must provide one or use a primitive/string/byte[]. If you require storing live/non-immutable data, store instead an int/long/identifier.");
            m_keyDecoder   = keyDecoder   ?? GetDefaultDecoder<TKey>()   ?? throw new ArgumentNullException(nameof(keyDecoder),   $"The key decoder {nameof(TKey)}={typeof(TKey).Name} has no decoder specified. You must provide one or use a primitive/string/byte[].");
            m_valueDecoder = valueDecoder ?? GetDefaultDecoder<TValue>() ?? throw new ArgumentNullException(nameof(valueDecoder), $"The value decoder {nameof(TValue)}={typeof(TValue).Name} has no decoder specified. You must provide one or use a primitive/string/byte[]. If you require storing live/non-immutable data, store instead an int/long/identifier.");
                
            m_keyBuffer   = new Buffer();
            m_valueBuffer = new Buffer();
        }
        #endregion
    
        #region static Load()
        public static AdaptiveRadixTree<TKey, TValue> Load(Stream storageStream) {
            return Load(storageStream, null, null, null, null);
        }
        /// <param name="keyEncoder">Default: null. Will use the default encoder if null. See GetDefaultEncoder().</param>
        /// <param name="valueEncoder">Default: null. Will use the default encoder if null. See GetDefaultEncoder().</param>
        /// <param name="keyDecoder">Default: null. Will use the default decoder if null. See GetDefaultDecoder().</param>
        /// <param name="valueDecoder">Default: null. Will use the default decoder if null. See GetDefaultDecoder().</param>
#if USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE
        public static AdaptiveRadixTree<TKey, TValue> Load(Stream storageStream, Action<TKey, Buffer> keyEncoder, Action<TValue, Buffer> valueEncoder, Func<byte[], int, int, TKey> keyDecoder, Func<byte[], int, int, TValue> valueDecoder) {
#else
        public static AdaptiveRadixTree<TKey, TValue> Load(Stream storageStream, Action<object, Buffer> keyEncoder, Action<object, Buffer> valueEncoder, Func<byte[], int, int, object> keyDecoder, Func<byte[], int, int, object> valueDecoder) {
#endif
            if(storageStream == null)
                throw new ArgumentNullException(nameof(storageStream));
    
            var memoryManager   = new MemoryManager();
            var allocatedMemory = InferMemoryUsage(storageStream, out long itemCount);
    
            memoryManager.Load(allocatedMemory);
    
            var res = new AdaptiveRadixTree<TKey, TValue>(memoryManager, storageStream, keyEncoder, valueEncoder, keyDecoder, valueDecoder);
    
            res.LongCount = itemCount;
                
            var raw = new byte[NODE_POINTER_BYTE_SIZE];
            storageStream.Position = 0;
            storageStream.Read(raw, 0, NODE_POINTER_BYTE_SIZE);
            res.m_rootPointer = ReadNodePointer(raw, 0);
    
            return res;
        }
        /// <summary>
        ///     Calculates the used memory by reading the entire tree.
        ///     Returns the memory usage in order, with pre-combined ranges.
        /// </summary>
        protected static List<(long start, long len)> InferMemoryUsage(Stream stream, out long itemCount) {
            var buffer = new byte[NODE_POINTER_BYTE_SIZE];
            stream.Position = 0;
            stream.Read(buffer, 0, NODE_POINTER_BYTE_SIZE);
            var rootPointer = ReadNodePointer(buffer, 0);
    
            itemCount = 0;
    
            var reservedMemory = new Dictionary<long, InternalReservedMemory> {
                { 0, new InternalReservedMemory(NODE_POINTER_BYTE_SIZE, true) },
                { NODE_POINTER_BYTE_SIZE, new InternalReservedMemory(NODE_POINTER_BYTE_SIZE, false) }
            };
    
            // while this code may look inefficient, 
            // keep in mind dictionary.add()/remove()/lookup() are on average O(1)
    
            foreach(var path in new PathEnumerator().Run(new NodePointer(0, rootPointer), stream, true, false)) {
                var last = path.Trail[path.Trail.Count - 1];
                var size = last.CalculateNodeSize();
                long pos = last.Pointer.Target;
                    
                if(last.Type == NodeType.Leaf)
                    itemCount++;
     
                var is_pre  = reservedMemory.TryGetValue(pos, out var pre);
                var is_post = reservedMemory.TryGetValue(pos + size, out var post);
    
                if(!is_pre && !is_post) {
                    reservedMemory.Add(pos, new InternalReservedMemory(size, true));
                    reservedMemory.Add(pos + size, new InternalReservedMemory(size, false));
                } else if(is_pre && !is_post) {
                    System.Diagnostics.Debug.Assert(!pre.IsPre && reservedMemory[pos - pre.Length].IsPre);
                    reservedMemory.Remove(pos);
                    reservedMemory[pos - pre.Length].Length = pre.Length + size;
                    reservedMemory.Add(pos + size, new InternalReservedMemory(pre.Length + size, false));
                } else if(!is_pre && is_post) {
                    System.Diagnostics.Debug.Assert(post.IsPre);
                    reservedMemory.Remove(pos + size);
                    post.Length += size;
                    reservedMemory.Add(pos, new InternalReservedMemory(post.Length, true));
                    reservedMemory[pos + post.Length].Length += size;
                } else { // is_pre && is_post
                    // merge 2
                    System.Diagnostics.Debug.Assert(!pre.IsPre && post.IsPre);
                    reservedMemory[pos - pre.Length].Length = pre.Length + size + post.Length;
                    reservedMemory[pos + post.Length + size].Length = pre.Length + size + post.Length;
                    reservedMemory.Remove(pos);
                    reservedMemory.Remove(pos + size);
                }
            }
    
            var res = new List<(long start, long len)>(reservedMemory.Count);
            foreach(var kv in reservedMemory) {
                if(!kv.Value.IsPre)
                    continue;
                res.Add((kv.Key, kv.Value.Length));
            }
            reservedMemory.Clear();
            reservedMemory = null; // intentionally allow garbage collection since this may grow large
    
            res.Sort();
    
            // verify no alloc overlap
            int max = res.Count;
            long prev = -1;
            for(int i = 0; i < max; i++) {
                var (start, len) = res[i];
                if(prev >= start) // if(prev==item.start) it means you messed up the algorithm as it shouldnt be possible
                    throw new FormatException("The stream contains duplicate allocations.");
                prev = start + len;
            }
    
            return res;
        }
        private class InternalReservedMemory {
            public long Length;
            public bool IsPre;
            public InternalReservedMemory(long length, bool is_pre) {
                this.Length = length;
                this.IsPre  = is_pre;
            }
        }
        #endregion
    
        #region Keys
        /// <summary>
        ///     O(n)
        ///     Returns keys in order.
        ///     Depth-First pre-order tree traversal.
        /// </summary>
        public IEnumerable<TKey> Keys {
            get {
                // consider caching the enumerator if you call this often
                var enumerator = new ChildrenKeyEnumerator();
                foreach(var item in enumerator.Run(m_rootPointer, this.Stream))
                    yield return item.GetKey(this);
            }
        }
        #endregion
        #region Values
        /// <summary>
        ///     O(n)
        ///     Returns values in key order.
        ///     Depth-First pre-order tree traversal.
        /// </summary>
        public IEnumerable<TValue> Values {
            get {
                // consider caching the enumerator if you call this often
                var enumerator = new ChildrenValueEnumerator();
                foreach(var item in enumerator.Run(m_rootPointer, this.Stream))
                    yield return item.GetValue(this);
            }
        }
        #endregion
        #region Items
        /// <summary>
        ///     O(n)
        ///     Returns items in key order.
        ///     Depth-First pre-order tree traversal.
        /// </summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> Items {
            get {
                // consider caching the enumerator if you call this often
                var enumerator = new ChildrenEnumerator();
                foreach(var item in enumerator.Run(m_rootPointer, this.Stream))
                    yield return item.GetItem(this);
            }
        }
        #endregion
        #region this[]
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    
        ///    Throws KeyNotFoundException.
        ///    Throws ArgumentException on empty key.
        /// </summary>
        public TValue this[in TKey key] {
            get{
                if(!this.TryGetValue(in key, out var value))
                    throw new KeyNotFoundException();
    
                return value;
            }
            set {
                var path               = this.TryGetPath(in key, false, true);
                var key_already_exists = !this.TryAddItem(path, in key, in value);
    
                if(key_already_exists) {
                    // if the leaf exists, make a new leaf, point to it, and unalloc old leaf
                    var last = path.Trail[path.Trail.Count - 1];
    
                    var valueBuffer = m_valueBuffer;
                    m_valueEncoder(value, valueBuffer);
                        
                    path.ReadPathEntireKey(this, path.EncodedSearchKey);
    
                    var remainingEncodedKey = new ReadOnlySpan<byte>(path.EncodedSearchKey.Content, path.EncodedSearchKey.Length - (last.PartialKeyLength - 1), last.PartialKeyLength - 1);
                    var address             = this.CreateLeafNode(in remainingEncodedKey, valueBuffer);
                    WriteNodePointer(m_buffer, 0, address);
                    this.Stream.Position = last.ParentPointerAddress;
                    this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
                    this.Free(last.Address, CalculateLeafNodeSize(last.PartialKeyLength, last.ValueLength));
                    this.LongCount++;
                }
            }
        }
        #endregion
    
        #region MinimumKey
        /// <summary>
        ///    O(k)    (k = # of characters of minimal key)
        ///    Could be used as a pseudo priority queue.
        ///    Throws KeyNotFoundException when empty.
        /// </summary>
        public TKey MinimumKey {
            get {
                if(!this.TryGetMinimumLeaf(out var res))
                    throw new KeyNotFoundException();
                return res;
            }
        }
        #endregion
        #region MaximumKey
        /// <summary>
        ///    O(k)    (k = # of characters of maximal key)
        ///    Could be used as a pseudo priority queue.
        ///    Throws KeyNotFoundException when empty.
        /// </summary>
        public TKey MaximumKey {
            get {
                if(!this.TryGetMaximumLeaf(out var res))
                    throw new KeyNotFoundException();
                return res;
            }
        }
        #endregion
    
        #region Add()
        /// <summary>
        ///     O(k)    (k = # of characters)
        ///     
        ///     Throws ArgumentException on empty/duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public void Add(in TKey key, in TValue value) {
            var path = this.TryGetPath(in key, false, true);
    
            if(!this.TryAddItem(path, in key, in value))
                throw new ArgumentException($"The key ({key}) already exists.", nameof(key));
        }
        #endregion
        #region AddRange()
        /// <summary>
        ///     O(m k)    (k = # of characters)
        ///     
        ///     Throws ArgumentException on empty/duplicate key.
        /// </summary>
        /// <exception cref="ArgumentException" />
        public void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> values) {
            foreach(var value in values)
                this.Add(value.Key, value.Value);
        }
        #endregion
        #region Remove()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    
        ///    Throws ArgumentException on empty key.
        /// </summary>
        public bool Remove(in TKey key) {
            var path = this.TryGetPath(in key, false, true);
    
            return this.TryRemoveItem(path);
        }
        #endregion
        #region RemoveRange()
        /// <summary>
        ///     O(m k)    (k = # of characters)
        ///     
        ///     Throws ArgumentException on empty key.
        /// </summary>
        public void RemoveRange(IEnumerable<TKey> keys) {
            foreach(var key in keys)
                this.Remove(key);
        }
        #endregion
        #region Clear()
        /// <summary>
        ///     O(1)
        /// </summary>
        public void Clear() {
            m_memoryManager.Clear();
            // pre-reserve the root pointer memory at address 0
            m_memoryManager.Load(new[] { ((long)0, (long)NODE_POINTER_BYTE_SIZE) });
            m_memoryManagerNode4.Clear();
            m_memoryManagerNode8.Clear();
            m_memoryManagerNode16.Clear();
            m_memoryManagerNode32.Clear();
            m_memoryManagerNode64.Clear();
            m_memoryManagerNode128.Clear();
            m_memoryManagerNode256.Clear();
    
            m_rootPointer  = 0;
            this.LongCount = 0;
            this.Stream.SetLength(NODE_POINTER_BYTE_SIZE);
    
            Array.Clear(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
            this.Stream.Position = 0;
            this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
        }
        #endregion
    
        #region TryAdd()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    Returns true if the item was added, false if existing.
        ///    Throws ArgumentException on empty key.
        /// </summary>
        public bool TryAdd(TKey key, TValue value) {
            var path = this.TryGetPath(in key, false, true);
            return !this.TryAddItem(path, in key, in value);
        }
        #endregion
        #region TryGetValue()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    
        ///    Throws ArgumentException on empty key.
        /// </summary>
        public bool TryGetValue(in TKey key, out TValue value) {
            return this.TryGetLeaf(in key, false, out value);
        }
        #endregion
        #region ContainsKey()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    
        ///    Throws ArgumentException on empty key.
        /// </summary>
        public bool ContainsKey(in TKey key) {
            return this.TryGetLeaf(in key, false, out _);
        }
        #endregion
        #region Contains()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    Returns true if the combination of {key, value} is found.
        ///     
        ///    Throws ArgumentException on empty key.
        /// </summary>
        public bool Contains(in TKey key, in TValue value) {
            if(!this.TryGetValue(key, out var read_value))
                return false;
            //return value.Equals(read_value);
            return EqualityComparer<TValue>.Default.Equals(value, read_value);
        }
        #endregion
    
        // StartsWith()
        #region StartsWith()
        /// <summary>
        ///     O(k)    (k = # of characters)
        ///     Returns all leafs starting with the given key.
        ///     
        ///     Throws ArgumentException on empty key.
        /// </summary>
        public IEnumerable<TKey> StartsWith(TKey key) {
            var path = this.TryGetPath(in key, false, false);
    
            var resultType = this.StartsWithImplementation(path, out long pointer);
    
            switch(resultType) {
                case StartsWithResult.NoResults: 
                    break;
                case StartsWithResult.AllItems:
                    foreach(var item in this.Keys)
                        yield return item;
                    break;
                case StartsWithResult.OneResult:
                    yield return path.GetKey(this);
                    break;
                case StartsWithResult.Pointer:
                    // if constantly calling this method, might want to cache the enumerator
                    var enumerator = new ChildrenKeyEnumerator();
                    foreach(var item in enumerator.Run(pointer, this.Stream, path.EncodedSearchKey.Content, path.EncodedSearchKey.Length))
                        yield return item.GetKey(this);
                    break;
            }
        }
        private enum StartsWithResult {
            NoResults,
            AllItems,
            /// <summary>
            ///     ie: startswith(search key)
            ///     The leaf key may be longer than the search key, but in this scenario there are no other results.
            /// </summary>
            OneResult,
            Pointer,
        }
        private StartsWithResult StartsWithImplementation(Path path, out long pointer) {
            pointer = -1;
    
            // if theres no root
            if(path == null)
                return StartsWithResult.NoResults;
    
            // if no node match, or the key passed cant be encoded (ie: StartsWith(string.Empty)), or path.startswith(key) == false
            var no_results = 
                path.Trail.Count == 0 || 
                (path.Trail.Count == 1 && path.Trail[0].PartialKeyLength == 0) ||
                path.CalculateKeyMatchLength() < path.EncodedSearchKey.Length;
                
            if(no_results) {
                if(path.EncodedSearchKey.Length == 0)
                    // if key cant be encoded, then return everything
                    return StartsWithResult.AllItems;
                    
                return StartsWithResult.NoResults;
            }
    
            var last = path.Trail[path.Trail.Count - 1];
    
            // note that path assumes were trying to find a leaf, and as such, will consider the [key + LEAF_NODE_KEY_TERMINATOR] to be what to search for
            // in our case, we were to return such result regardless, but the 'real path' we care about ignores the terminator character
            if(last.Type == NodeType.Leaf) {
                if(last.PartialKeyMatchLength > 0) {
                    // if we have an exact match (path.success) and that the last leaf matches more than LEAF_NODE_KEY_TERMINATOR
                    // then it means theres no child/branches under it
                    return StartsWithResult.OneResult;
                } else { // last.PartialKeyMatchLength == 0
                    // if we have an exact match, but we matched on LEAF_NODE_KEY_TERMINATOR exclusively for the last node
                    // then it means we need to list everything from the parent
                    pointer = path.Trail[path.Trail.Count - 2].Address;
                }
            } else {
                pointer = last.Address;
                path.EncodedSearchKey.Length -= last.PartialKeyMatchLength;
            }
    
            return StartsWithResult.Pointer;
        }
        #endregion
        #region StartsWithValues()
        /// <summary>
        ///     O(k)    (k = # of characters)
        ///     Returns all leafs starting with the given key.
        ///     
        ///     Throws ArgumentException on empty key.
        /// </summary>
        public IEnumerable<TValue> StartsWithValues(TKey key) {
            var path = this.TryGetPath(in key, true, false);
    
            var resultType = this.StartsWithImplementation(path, out long pointer);
    
            switch(resultType) {
                case StartsWithResult.NoResults: 
                    break;
                case StartsWithResult.AllItems:
                    foreach(var item in this.Values)
                        yield return item;
                    break;
                case StartsWithResult.OneResult:
                    yield return path.Value;
                    break;
                case StartsWithResult.Pointer:
                    // if constantly calling this method, might want to cache the enumerator
                    var enumerator = new ChildrenValueEnumerator();
                    foreach(var item in enumerator.Run(pointer, this.Stream))
                        yield return item.GetValue(this);
                    break;
            }
        }
        #endregion
        #region StartsWithItems()
        /// <summary>
        ///     O(k)    (k = # of characters)
        ///     Returns all leafs starting with the given key.
        ///     
        ///     Throws ArgumentException on empty key.
        /// </summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> StartsWithItems(TKey key) {
            var path = this.TryGetPath(in key, true, false);
    
            var resultType = this.StartsWithImplementation(path, out long pointer);
    
            switch(resultType) {
                case StartsWithResult.NoResults: 
                    break;
                case StartsWithResult.AllItems:
                    foreach(var item in this.Items)
                        yield return item;
                    break;
                case StartsWithResult.OneResult:
                    yield return path.GetItem(this);
                    break;
                case StartsWithResult.Pointer:
                    // consider caching the enumerator if you call this often
                    var enumerator = new ChildrenEnumerator();
                    foreach(var item in enumerator.Run(pointer, this.Stream, path.EncodedSearchKey.Content, path.EncodedSearchKey.Length)) 
                        yield return item.GetItem(this);
                    break;
            }
        }
        #endregion
    
        // PartialMatch()
        #region PartialMatch()
        /// <summary>
        ///     O(k)    (k = # of characters, + wildcards creating sub-branches running in O(k_remaining))
        ///     
        ///     Search with wildcards.
        ///     The wildcards are only for single character replacements.
        /// </summary>
        public IEnumerable<TKey> PartialMatch(string pattern, char wildcard, SearchOption match = SearchOption.ExactMatch) {
            var bitArray = ParsePartialMatchFormat(pattern, wildcard, match);
            if(bitArray == null) {
                foreach(var item in this.Keys)
                    yield return item;
                yield break;
            }
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                ExtractValue    = false,
                HammingDistance = 0,
            };
            var searchOptions = new InternalSearchOptions(){
                HammingCostPerMissingCharacter = int.MaxValue,
                HammingCostPerExtraCharacter   = match == SearchOption.ExactMatch ? int.MaxValue : 0,
            };
    
            foreach(var item in this.RegExpMatchImplementation(bitArray, searchOptions, filterOptions))
                yield return item.GetKey(this);
        }
        private static BitArray ParsePartialMatchFormat(string pattern, char wildcard, SearchOption match) {
            if(match == SearchOption.StartsWith) {
                pattern = pattern.TrimEnd(wildcard);
                    
                if(pattern.Length == 0)
                    return null;
            }
    
            //if(!pattern.Contains(wildcard)) {
            //    foreach(var item in this.StartsWithKeys(m_keyEncoder(...)))
            //        yield return item;
            //    yield break;
            //}
    
            var bitArray = new BitArray(pattern.Length * 256);
            for(int i = 0; i < pattern.Length; i++){
                var c = pattern[i];
    
                if(c != wildcard) 
                    bitArray.Set(i * 256 + c, true);
                else {
                    for(int j = 0; j < 256; j++)
                        bitArray.Set(i * 256 + j, true);
                }
            }
            return bitArray;
        }
        #endregion
        #region PartialMatchValues()
        /// <summary>
        ///     O(k)    (k = # of characters, + wildcards creating sub-branches running in O(k_remaining))
        ///     
        ///     Search with wildcards.
        ///     The wildcards are only for single character replacements.
        /// </summary>
        public IEnumerable<TValue> PartialMatchValues(string pattern, char wildcard, SearchOption match = SearchOption.ExactMatch) {
            var bitArray = ParsePartialMatchFormat(pattern, wildcard, match);
            if(bitArray == null) {
                foreach(var item in this.Values)
                    yield return item;
                yield break;
            }
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                ExtractValue    = true,
                HammingDistance = 0,
            };
            var searchOptions = new InternalSearchOptions(){
                HammingCostPerMissingCharacter = int.MaxValue,
                HammingCostPerExtraCharacter   = match == SearchOption.ExactMatch ? int.MaxValue : 0,
            };
    
            foreach(var item in this.RegExpMatchImplementation(bitArray, searchOptions, filterOptions))
                yield return item.GetValue(this);
        }
        #endregion
        #region PartialMatchItems()
        /// <summary>
        ///     O(k)    (k = # of characters, + wildcards creating sub-branches running in O(k_remaining))
        ///     Search with wildcards.
        ///     The wildcards are only for single character replacements.
        /// </summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> PartialMatchItems(string pattern, char wildcard, SearchOption match = SearchOption.ExactMatch) {
            var bitArray = ParsePartialMatchFormat(pattern, wildcard, match);
            if(bitArray == null) {
                foreach(var item in this.Items)
                    yield return item;
                yield break;
            }
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                ExtractValue    = true,
                HammingDistance = 0,
            };
            var searchOptions = new InternalSearchOptions(){
                HammingCostPerMissingCharacter = int.MaxValue,
                HammingCostPerExtraCharacter   = match == SearchOption.ExactMatch ? int.MaxValue : 0,
            };
    
            foreach(var item in this.RegExpMatchImplementation(bitArray, searchOptions, filterOptions))
                yield return item.GetItem(this);
        }
        #endregion
    
        // RegExpMatch()
        #region RegExpMatch()
        /// <summary>
        ///     O(k)    (k = # of characters, + siblings sub-branches running in O(k_remaining))
        ///     
        ///     Search with a simplified regular-expression-like syntax.
        ///     
        ///     Pattern format:  '[a-zA-Z0-9-]abcdef'   (similar to regex)
        ///     complex example: '[a-z\\-\]-]aaaaa[*]'
        ///     
        ///                      '[]' signify anything match for these character ranges
        ///                           since '-' is used for ranges, if you want to match '-', put it at the start or end.
        ///                      '[', ']' and '\' need to be backspaced (ie '\[', '\]', '\\').
        ///                      '[*]' is special and signifies a wildcard (all characters)
        ///                      '[*a]' is not special and the characters '*' and 'a' will be searched for
        ///     
        ///     Throws FormatException.
        /// </summary>
        public IEnumerable<TKey> RegExpMatch(string regexpPattern, SearchOption match = SearchOption.ExactMatch) {
            var bitArray = ParseRegExpFormat(regexpPattern);
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                ExtractValue    = false,
                HammingDistance = 0,
            };
            var searchOptions = new InternalSearchOptions(){
                HammingCostPerMissingCharacter = int.MaxValue,
                HammingCostPerExtraCharacter   = match == SearchOption.ExactMatch ? int.MaxValue : 0,
            };
    
            foreach(var item in this.RegExpMatchImplementation(bitArray, searchOptions, filterOptions))
                yield return item.GetKey(this);
        }
        private class InternalSearchOptions {
            public int HammingCostPerMissingCharacter;
            public int HammingCostPerExtraCharacter;
        }
        private IEnumerable<FilterablePathEnumerator.Node> RegExpMatchImplementation(BitArray bitArray, InternalSearchOptions searchOptions, FilterablePathEnumerator.Options filterOptions) {
            var realCharacters = bitArray.Length / 256;
    
            filterOptions.Owner = this;
    
            if(filterOptions.HammingDistance == 0) {
                filterOptions.CalculateHammingDistance = new Func<FilterablePathEnumerator.FilterItem, int>(o => {
                    if(o.KeyLength > realCharacters)
                        return searchOptions.HammingCostPerExtraCharacter > 0 ? 1 : 0;
    
                    int max = Math.Min(realCharacters, o.KeyLength);
                    for(int i = o.LastAcceptedLength; i < max; i++) {
                        if(!bitArray[i * 256 + o.EncodedKey[i]])
                            return 1;
                    }
                    return 0;
                });
            } else {
                filterOptions.CalculateHammingDistance = new Func<FilterablePathEnumerator.FilterItem, int>(o => {
                    if(o.KeyLength > realCharacters)
                        return searchOptions.HammingCostPerExtraCharacter > 0 ?
                            unchecked((int)Math.Min((o.KeyLength - realCharacters) * (long)searchOptions.HammingCostPerExtraCharacter, int.MaxValue)) : 
                            0;
                        
                    int max = Math.Min(realCharacters, o.KeyLength);
                    int dist = 0;
                    for(int i = o.LastAcceptedLength; i < max; i++){
                        if(!bitArray[i * 256 + o.EncodedKey[i]])
                            dist++;
                    }
                    return dist;
                });
            }
    
            // note:
            // CalculateHammingDistance() cannot check for HammingCostPerMissingCharacter because we are drilling down the tree, 
            // and dont always know if were dealing with a leaf or not
            // and since the tree may contain [aaa] and [aaaaa] as entries, we have to let 'aaa' pass everytime and then check back on the results
            // to see if we filter or not
    
            // consider caching the enumerator if you call this often
            var enumerator = new FilterablePathEnumerator();
            foreach(var item in enumerator.Run(filterOptions)) {
                var keySize  = item.KeyLength;
                long hamming = item.HammingDistance;
                    
                if(keySize < realCharacters) {
                    hamming -= (realCharacters - keySize) * (long)searchOptions.HammingCostPerMissingCharacter;
                    if(hamming < 0)
                        continue;
                }
                // this is intentionally not checked, as doing so here would mean there were no pruning done during the enumeration
                // instead we adjust the extra_char hamming cost in CalculateHammingDistance(), meaning doing so here would double count them
                //else if(keySize > realCharacters) {
                //    hamming -= (keySize - realCharacters) * (long)searchOptions.HammingCostPerExtraCharacter;
                //    if(hamming < 0)
                //        continue;
                //}
            
                yield return item;
            }
        }
        /// <summary>
        ///     Parses a simplified regexp.
        ///     This is meant to map easily to byte[] which is used internally to compare with the encoded key
        ///     (ie: after EscapeLeafKeyTerminator()). 
        ///     For simplicity's sake, we are assuming the input maps directly into encoded key.
        ///     Only [byte] characters out of [char] are considered.
        ///     
        ///     Pattern format:  '[a-zA-Z0-9-]abcdef'   (similar to regex)
        ///     complex example: '[a-z\\-\]-]aaaaa[*]'
        ///     
        ///                      '[]' signify anything match for these character ranges
        ///                           since '-' is used for ranges, if you want to match '-', put it at the start or end.
        ///                      '[', ']' and '\' need to be backspaced (ie '\[', '\]', '\\').
        ///                      '[*]' is special and signifies a wildcard (all characters)
        ///                      '[*a]' is not special and the characters '*' and 'a' will be searched for
        /// </summary>
        private static BitArray ParseRegExpFormat(string format) {
            var res            = new BitArray(format.Length * 256);
            int i              = 0;
            int realCharacters = 0;
    
            // known misbehavior: [A-B-D] is supported, and gives the range [A-D] instead of throwing a FormatException
            // technically it should be written [A-BB-D] in regexes, but for simplicitys sake, I leave unfixed as a hidden feature
                
            while(i < format.Length) {
                var c = format[i];
                if(c == '\\') {
                    if(i + 1 == format.Length)
                        throw new FormatException("Invalid '\\' at end of format.");
                    c = format[i + 1];
                    if(c != '\\' && c != '[' && c != ']')
                        throw new FormatException($"Invalid '\\' followed by {c}.");
                    res[realCharacters++ * 256 + c] = true;
                    i++;
                } else if(c == '[') {
                    var start = i;
                    i++;
    
                    if(i + 1 < format.Length && format[i] == '*' && format[i + 1] == ']') {
                        for(int j = 0; j < 256; j++)
                            res[realCharacters * 256 + j] = true;
                        realCharacters++;
                        i += 2;
                        continue;
                    }
                    char prevC     = i < format.Length ? format[i] : '\0';
                    bool endFound  = false;
                    while(i < format.Length) {
                        c = format[i];
                        if(c == ']') {
                            endFound = true;
                            i++;
                            break;
                        } else if(c == '[')
                            throw new FormatException($"Extraneous '[' found.");
                        else if(c == '\\') {
                            c = format[i + 1];
                            if(c != '\\' && c != '[' && c != ']')
                                throw new FormatException($"Invalid '\\' followed by {c}.");
                            i++;
                        } else if(c == '-') {
                            if(i == start + 1) {
                                // intentionally empty
                            } else if(i + 1 < format.Length) {
                                if(format[i + 1] == ']') {
                                    // intentionally empty
                                } else if(format[i + 1] == '\\') {
                                    if(i + 2 >= format.Length)
                                        throw new FormatException();
                                    c = format[i + 2];
                                    if(c != '\\' && c != '[' && c != ']')
                                        throw new FormatException($"Invalid '\\' followed by {c}.");
                                    if(prevC >= c)
                                        throw new FormatException($"Invalid range {prevC}-{c}; range seems inverted.");
                                    for(int j = prevC + 1; j <= c; j++)
                                        res[realCharacters * 256 + j] = true;
                                    i += 2;
                                } else {
                                    c = format[i + 1];
                                    if(prevC >= c)
                                        throw new FormatException($"Invalid range {prevC}-{c}; range seems inverted.");
                                    for(int j = prevC + 1; j <= c; j++)
                                        res[realCharacters * 256 + j] = true;
                                    i++;
                                }
                            }
                        }
                        res[realCharacters * 256 + c] = true;
                        prevC = c;
                        i++;
                    }
                    if(!endFound)
                        throw new FormatException($"'[' missing matching ']'.");
                    realCharacters++;
                    continue;
                } else if(c == ']') 
                    throw new FormatException($"']' missing matching '['.");
                res[realCharacters++ * 256 + c] = true;
                i++;
            }
            res.Length = realCharacters * 256;
            return res;
        }
        #endregion
        #region RegExpMatchValues()
        /// <summary>
        ///     O(k)    (k = # of characters, + siblings sub-branches running in O(k_remaining))
        ///     
        ///     Search with a simplified regular-expression-like syntax.
        ///     
        ///     Pattern format:  '[a-zA-Z0-9-]abcdef'   (similar to regex)
        ///     complex example: '[a-z\\-\]-]aaaaa[*]'
        ///     
        ///                      '[]' signify anything match for these character ranges
        ///                           since '-' is used for ranges, if you want to match '-', put it at the start or end.
        ///                      '[', ']' and '\' need to be backspaced (ie '\[', '\]', '\\').
        ///                      '[*]' is special and signifies a wildcard (all characters)
        ///                      '[*a]' is not special and the characters '*' and 'a' will be searched for
        ///     
        ///     Throws FormatException.
        /// </summary>
        public IEnumerable<TValue> RegExpMatchValues(string regexpPattern, SearchOption match = SearchOption.ExactMatch) {
            var bitArray = ParseRegExpFormat(regexpPattern);
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                ExtractValue    = true,
                HammingDistance = 0,
            };
            var searchOptions = new InternalSearchOptions(){
                HammingCostPerMissingCharacter = int.MaxValue,
                HammingCostPerExtraCharacter   = match == SearchOption.ExactMatch ? int.MaxValue : 0,
            };
    
            foreach(var item in this.RegExpMatchImplementation(bitArray, searchOptions, filterOptions))
                yield return item.GetValue(this);
        }
        #endregion
        #region RegExpMatchItems()
        /// <summary>
        ///     O(k)    (k = # of characters, + siblings sub-branches running in O(k_remaining))
        ///     
        ///     Search with a simplified regular-expression-like syntax.
        ///     
        ///     Pattern format:  '[a-zA-Z0-9-]abcdef'   (similar to regex)
        ///     complex example: '[a-z\\-\]-]aaaaa[*]'
        ///     
        ///                      '[]' signify anything match for these character ranges
        ///                           since '-' is used for ranges, if you want to match '-', put it at the start or end.
        ///                      '[', ']' and '\' need to be backspaced (ie '\[', '\]', '\\').
        ///                      '[*]' is special and signifies a wildcard (all characters)
        ///                      '[*a]' is not special and the characters '*' and 'a' will be searched for
        ///     
        ///     Throws FormatException.
        /// </summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> RegExpMatchItems(string regexpPattern, SearchOption match = SearchOption.ExactMatch) {
            var bitArray = ParseRegExpFormat(regexpPattern);
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                ExtractValue    = true,
                HammingDistance = 0,
            };
            var searchOptions = new InternalSearchOptions(){
                HammingCostPerMissingCharacter = int.MaxValue,
                HammingCostPerExtraCharacter   = match == SearchOption.ExactMatch ? int.MaxValue : 0,
            };
    
            foreach(var item in this.RegExpMatchImplementation(bitArray, searchOptions, filterOptions))
                yield return item.GetItem(this);
        }
        #endregion
    
        // NearNeighbors()
        #region RegExpNearNeighbors()
        /// <summary>
        ///     O(k)    (k = # of characters, + siblings sub-branches running in O(k_remaining), + Hamming sub-branches)
        ///     
        ///     RegExpMatch() + Hamming distance.
        ///     Search with a simplified regular-expression-like syntax.
        ///     Returns near-neighboors, which is keys that are within a given number of character difference.
        ///     
        ///     Pattern format:  '[a-zA-Z0-9-]abcdef'   (similar to regex)
        ///     complex example: '[a-z\\-\]-]aaaaa[*]'
        ///     
        ///                      '[]' signify anything match for these character ranges
        ///                           since '-' is used for ranges, if you want to match '-', put it at the start or end.
        ///                      '[', ']' and '\' need to be backspaced (ie '\[', '\]', '\\').
        ///                      '[*]' is special and signifies a wildcard (all characters)
        ///                      '[*a]' is not special and the characters '*' and 'a' will be searched for
        ///     
        ///     Throws FormatException.
        /// </summary>
        /// <param name="hammingDistance">The Hamming distance; indicates how many characters are allowed to differ. 0 = exact match. 1 = 1 character may differ, etc. Do not specify a negative value.</param>
        public IEnumerable<TKey> RegExpNearNeighbors(string regexpPattern, int hammingDistance, int hammingCostPerMissingCharacter = 1, int hammingCostPerExtraCharacter = 1) {
            var bitArray = ParseRegExpFormat(regexpPattern);
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                ExtractValue    = false,
                HammingDistance = hammingDistance,
            };
            var searchOptions = new InternalSearchOptions(){
                HammingCostPerMissingCharacter = hammingCostPerMissingCharacter,
                HammingCostPerExtraCharacter   = hammingCostPerExtraCharacter, // you could also make extra chars be free  "SearchOptions == StartsWith ? 0 : 1"
            };
    
            foreach(var item in this.RegExpMatchImplementation(bitArray, searchOptions, filterOptions))
                yield return item.GetKey(this);
        }
        #endregion
        #region RegExpNearNeighborsValues()
        /// <summary>
        ///     O(k)    (k = # of characters, + siblings sub-branches running in O(k_remaining), + Hamming sub-branches)
        /// 
        ///     RegExpMatch() + Hamming distance.
        ///     Search with a simplified regular-expression-like syntax.
        ///     Returns near-neighboors, which is keys that are within a given number of character difference.
        ///     
        ///     Pattern format:  '[a-zA-Z0-9-]abcdef'   (similar to regex)
        ///     complex example: '[a-z\\-\]-]aaaaa[*]'
        ///     
        ///                      '[]' signify anything match for these character ranges
        ///                           since '-' is used for ranges, if you want to match '-', put it at the start or end.
        ///                      '[', ']' and '\' need to be backspaced (ie '\[', '\]', '\\').
        ///                      '[*]' is special and signifies a wildcard (all characters)
        ///                      '[*a]' is not special and the characters '*' and 'a' will be searched for
        ///     
        ///     Throws FormatException.
        /// </summary>
        /// <param name="hammingDistance">The Hamming distance; indicates how many characters are allowed to differ. 0 = exact match. 1 = 1 character may differ, etc. Do not specify a negative value.</param>
        public IEnumerable<TValue> RegExpNearNeighborsValues(string regexpPattern, int hammingDistance, int hammingCostPerMissingCharacter = 1, int hammingCostPerExtraCharacter = 1) {
            var bitArray = ParseRegExpFormat(regexpPattern);
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                ExtractValue    = true,
                HammingDistance = hammingDistance,
            };
            var searchOptions = new InternalSearchOptions(){
                HammingCostPerMissingCharacter = hammingCostPerMissingCharacter,
                HammingCostPerExtraCharacter   = hammingCostPerExtraCharacter, // you could also make extra chars be free  "SearchOptions == StartsWith ? 0 : 1"
            };
    
            foreach(var item in this.RegExpMatchImplementation(bitArray, searchOptions, filterOptions))
                yield return item.GetValue(this);
        }
        #endregion
        #region RegExpNearNeighborsItems()
        /// <summary>
        ///     O(k)    (k = # of characters, + siblings sub-branches running in O(k_remaining), + Hamming sub-branches)
        /// 
        ///     RegExpMatch() + Hamming distance.
        ///     Search with a simplified regular-expression-like syntax.
        ///     Returns near-neighboors, which is keys that are within a given number of character difference.
        ///     
        ///     Pattern format:  '[a-zA-Z0-9-]abcdef'   (similar to regex)
        ///     complex example: '[a-z\\-\]-]aaaaa[*]'
        ///     
        ///                      '[]' signify anything match for these character ranges
        ///                           since '-' is used for ranges, if you want to match '-', put it at the start or end.
        ///                      '[', ']' and '\' need to be backspaced (ie '\[', '\]', '\\').
        ///                      '[*]' is special and signifies a wildcard (all characters)
        ///                      '[*a]' is not special and the characters '*' and 'a' will be searched for
        ///     
        ///     Throws FormatException.
        /// </summary>
        /// <param name="hammingDistance">The Hamming distance; indicates how many characters are allowed to differ. 0 = exact match. 1 = 1 character may differ, etc. Do not specify a negative value.</param>
        public IEnumerable<KeyValuePair<TKey, TValue>> RegExpNearNeighborsItems(string regexpPattern, int hammingDistance, int hammingCostPerMissingCharacter = 1, int hammingCostPerExtraCharacter = 1) {
            var bitArray = ParseRegExpFormat(regexpPattern);
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                ExtractValue    = true,
                HammingDistance = hammingDistance,
            };
            var searchOptions = new InternalSearchOptions(){
                HammingCostPerMissingCharacter = hammingCostPerMissingCharacter,
                HammingCostPerExtraCharacter   = hammingCostPerExtraCharacter, // you could also make extra chars be free  "SearchOptions == StartsWith ? 0 : 1"
            };
    
            foreach(var item in this.RegExpMatchImplementation(bitArray, searchOptions, filterOptions))
                yield return item.GetItem(this);
        }
        #endregion
    
        // Range()
        #region Range()
        public enum RangeOption {
            /// <summary>
            ///     Considers the range to be alphabetical in nature.
            ///     This means the range [AAA - ZZZ] will consider 'BB' to be within it.
            ///     Depth-First pre-order tree traversal.
            /// </summary>
            Alphabetical,
            /// <summary>
            ///     Considers the range to be within branches.
            ///     ex: range [null, DEF]  will almost return the equivalent to regex "[\0-D][\0-E][\0-F].*" 
            ///         range [ABC, DEF]   will almost return the equivalent to regex "[A-D][B-E][C-F].*" 
            ///     This is more efficient than Alphabetical, and is mostly intended for [start, null] or [null, end] ranges.
            ///     Depth-First pre-order tree traversal.
            /// </summary>
            Tree,
        }
        /// <summary>
        ///     O(k)    (k = # of characters)
        ///     
        ///     Returns keys within an inclusive range.
        ///     Will consider empty keys = default.
        /// </summary>
        /// <param name="start">Default: default(TKey). Inclusive. If default, then starts at MinimumKey.</param>
        /// <param name="end">Default: default(TKey). Inclusive. If default, then ends at MaximumKey.</param>
        public IEnumerable<TKey> Range(TKey start, TKey end, RangeOption option = RangeOption.Alphabetical) {
            foreach(var item in this.RangeImplementation(start, end, false, option)) {
                if(item != null)
                    yield return item.GetKey(this);
                else {
                    foreach(var o in this.Keys)
                        yield return o;
                    yield break;
                }
            }
        }
        /// <summary>
        ///     O(k)    (k = # of characters)
        ///     
        ///     Returns keys within an inclusive range.
        ///     Will consider empty keys = default.
        /// </summary>
        /// <param name="start">Default: default(TKey). Inclusive. If default, then starts at MinimumKey.</param>
        /// <param name="end">Default: default(TKey). Inclusive. If default, then ends at MaximumKey.</param>
        private IEnumerable<FilterablePathEnumerator.Node> RangeImplementation(TKey start, TKey end, bool extractValue, RangeOption option) {
            if(start == default && end == default) {
                yield return null; // signal list all
                yield break;
            }
    
            Buffer convertedStart = null;
            if(start != default) {
                convertedStart = m_keyBuffer;
                m_keyEncoder(start, convertedStart);
                if(convertedStart.Length != 0)
                    EscapeLeafKeyTerminator(convertedStart);
                else
                    convertedStart = null;
            }
    
            Buffer convertedEnd = null;
            if(end != default) {
                convertedEnd = new Buffer();
                m_keyEncoder(end, convertedEnd);
                if(convertedEnd.Length != 0)
                    EscapeLeafKeyTerminator(convertedEnd);
                else
                    convertedEnd = null;
            }
    
            if(convertedStart == null && convertedEnd == null) {
                yield return null; // signal list all
                yield break;
            }
    
            ValidateParams();
    
            var filterOptions = new FilterablePathEnumerator.Options(){
                Owner                    = this,
                ExtractValue             = extractValue,
                HammingDistance          = 0,
            };
    
            // since this is a hot path, we de-duplicate the code (with tiny changes) rather than re-evaluate the scenario every pass
    
            if(convertedStart != null && convertedEnd != null) {
                if(option == RangeOption.Alphabetical) {
                    filterOptions.CalculateHammingDistance = new Func<FilterablePathEnumerator.FilterItem, int>(o => {
                        int max = Math.Min(o.KeyLength, convertedStart.Length);
                        for(int i = 0; i < max; i++) { // "int i = o.LastAcceptedLength" if tree-like filtered
                            var c1 = o.EncodedKey[i];
                            var c2 = convertedStart.Content[i];
                            if(c1 < c2)
                                return 1; // ie: prune < start branches
                            else if(c1 > c2)
                                break;
                        }
                        bool all_equal = true;
                        max = Math.Min(o.KeyLength, convertedEnd.Length);
                        for(int i = 0; i < max; i++) { // "int i = o.LastAcceptedLength" if tree-like filtered
                            var c1 = o.EncodedKey[i];
                            var c2 = convertedEnd.Content[i];
                            if(c1 > c2)
                                return 1; // ie: prune > end branches
                            if(c1 < c2) {
                                all_equal = false;
                                break;
                            }
                        }
                        if(all_equal && o.KeyLength > convertedEnd.Length)
                            return 1;
                        return 0;
                    });
                } else {
                    filterOptions.CalculateHammingDistance = new Func<FilterablePathEnumerator.FilterItem, int>(o => {
                        int max = Math.Min(o.KeyLength, convertedStart.Length);
                        for(int i = o.LastAcceptedLength; i < max; i++) {
                            var c1 = o.EncodedKey[i];
                            var c2 = convertedStart.Content[i];
                            if(c1 < c2)
                                return 1; // ie: prune < start branches
                            else if(c1 > c2)
                                break;
                        }
                        bool all_equal = true;
                        max = Math.Min(o.KeyLength, convertedEnd.Length);
                        for(int i = o.LastAcceptedLength; i < max; i++) {
                            var c1 = o.EncodedKey[i];
                            var c2 = convertedEnd.Content[i];
                            if(c1 > c2)
                                return 1; // ie: prune > end branches
                            if(c1 < c2) {
                                all_equal = false;
                                break;
                            }
                        }
                        if(all_equal && o.KeyLength > convertedEnd.Length)
                            return 1;
                        return 0;
                    });
                }
            } else if(convertedStart != null && convertedEnd == null) {
                if(option == RangeOption.Alphabetical) {
                    filterOptions.CalculateHammingDistance = new Func<FilterablePathEnumerator.FilterItem, int>(o => {
                        int max = Math.Min(o.KeyLength, convertedStart.Length);
                        for(int i = 0; i < max; i++) { // "int i = o.LastAcceptedLength" if tree-like filtered
                            var c1 = o.EncodedKey[i];
                            var c2 = convertedStart.Content[i];
                            if(c1 < c2)
                                return 1; // ie: prune < start branches
                            else if(c1 > c2)
                                break;
                        }
                        return 0;
                    });
                } else {
                    filterOptions.CalculateHammingDistance = new Func<FilterablePathEnumerator.FilterItem, int>(o => {
                        int max = Math.Min(o.KeyLength, convertedStart.Length);
                        for(int i = o.LastAcceptedLength; i < max; i++) {
                            var c1 = o.EncodedKey[i];
                            var c2 = convertedStart.Content[i];
                            if(c1 < c2)
                                return 1; // ie: prune < start branches
                            else if(c1 > c2)
                                break;
                        }
                        return 0;
                    });
                }
            } else if(convertedStart == null && convertedEnd != null) {
                if(option == RangeOption.Alphabetical) {
                    filterOptions.CalculateHammingDistance = new Func<FilterablePathEnumerator.FilterItem, int>(o => {
                        bool all_equal = true;
                        int max = Math.Min(o.KeyLength, convertedEnd.Length);
                        for(int i = 0; i < max; i++) { // "int i = o.LastAcceptedLength" if tree-like filtered
                            var c1 = o.EncodedKey[i];
                            var c2 = convertedEnd.Content[i];
                            if(c1 > c2)
                                return 1; // ie: prune > end branches
                            if(c1 < c2) {
                                all_equal = false;
                                break;
                            }
                        }
                        if(all_equal && o.KeyLength > convertedEnd.Length)
                            return 1;
                        return 0;
                    });
                } else {
                    filterOptions.CalculateHammingDistance = new Func<FilterablePathEnumerator.FilterItem, int>(o => {
                        bool all_equal = true;
                        int max = Math.Min(o.KeyLength, convertedEnd.Length);
                        for(int i = o.LastAcceptedLength; i < max; i++) {
                            var c1 = o.EncodedKey[i];
                            var c2 = convertedEnd.Content[i];
                            if(c1 > c2)
                                return 1; // ie: prune > end branches
                            if(c1 < c2) {
                                all_equal = false;
                                break;
                            }
                        }
                        if(all_equal && o.KeyLength > convertedEnd.Length)
                            return 1;
                        return 0;
                    });
                }
            } else {
                filterOptions.CalculateHammingDistance = new Func<FilterablePathEnumerator.FilterItem, int>(o => {
                    // accept everything
                    return 0;
                });
            }
    
            // note:
            // CalculateHammingDistance() cannot check for too early results because we are drilling down the tree, 
            // such as [aaa] when start was [aaaaa]
                
            bool startListing = convertedStart == null;
    
            // consider caching the enumerator if you call this often
            var enumerator = new FilterablePathEnumerator();
            foreach(var item in enumerator.Run(filterOptions)) {
                if(!startListing) {
                    if(item.KeyLength < convertedStart.Length) {
                        // if shorter, then the key must be > to consider listing
                        bool all_equal = true;
                        int max = Math.Min(item.KeyLength, convertedStart.Length);
                        for(int i = 0; i < max; i++) {
                            var c1 = item.Key[i];
                            var c2 = convertedStart.Content[i];
                            if(c1 > c2) {
                                all_equal = false;
                                break;
                            }
                        }
                        if(all_equal)
                            continue;
                    }
                    startListing = true;
                }
    
                yield return item;
            }
    
            void ValidateParams() {
                // verify start <= end
                if(convertedStart != null && convertedEnd != null) {
                    int i = 0;
                    bool all_equal_characters = true;
                    while(i < convertedStart.Length && i < convertedEnd.Length) {
                        var c1 = convertedStart.Content[i];
                        var c2 = convertedEnd.Content[i];
                        if(c2 > c1) {
                            all_equal_characters = false;
                            break;
                        } else if(c2 < c1)
                            throw new ArgumentException($"{nameof(end)} ({end}) < {nameof(start)} ({start})", nameof(end));
                        i++;
                    }
                    // if start=AAAAA end=AA
                    if(all_equal_characters && convertedEnd.Length < convertedStart.Length)
                        throw new ArgumentException($"{nameof(end)} ({end}) < {nameof(start)} ({start})", nameof(end));
    
                    // note: not sure if more specific tests should be done "if(option == RangeOption.ListingOrder)"
                }
            }
        }
        #endregion
        #region RangeValues()
        /// <summary>
        ///     O(k)    (k = # of characters)
        ///     
        ///     Returns keys within an inclusive range.
        ///     Will consider empty keys = default.
        /// </summary>
        /// <param name="start">Default: default(TKey). Inclusive. If default, then starts at MinimumKey.</param>
        /// <param name="end">Default: default(TKey). Inclusive. If default, then ends at MaximumKey.</param>
        public IEnumerable<TValue> RangeValues(TKey start, TKey end, RangeOption option = RangeOption.Alphabetical) {
            foreach(var item in this.RangeImplementation(start, end, true, option)) {
                if(item != null)
                    yield return item.GetValue(this);
                else {
                    foreach(var o in this.Values)
                        yield return o;
                    yield break;
                }
            }
        }
        #endregion
        #region RangeItems()
        /// <summary>
        ///     O(k)    (k = # of characters)
        ///     
        ///     Returns keys within an inclusive range.
        ///     Will consider empty keys = default.
        /// </summary>
        /// <param name="start">Default: default(TKey). Inclusive. If default, then starts at MinimumKey.</param>
        /// <param name="end">Default: default(TKey). Inclusive. If default, then ends at MaximumKey.</param>
        public IEnumerable<KeyValuePair<TKey, TValue>> RangeItems(TKey start, TKey end, RangeOption option = RangeOption.Alphabetical) {
            foreach(var item in this.RangeImplementation(start, end, true, option)) {
                if(item != null)
                    yield return item.GetItem(this);
                else {
                    foreach(var o in this.Items)
                        yield return o;
                    yield break;
                }
            }
        }
        #endregion
    
        #region Optimize()
        /// <summary>
        ///     Rebuilds the tree in a way that localizes branches to be stored nearby.
        ///     This will also compact the memory and leave no fragmented memory.
        /// </summary>
        /// <remarks>
        ///     This will not rebalance the tree, as the tree is always maintained in a balanced state.
        ///     As such, no O(x) gains will be had, but cache locality will be optimized so [CPU caches]/[whatever Stream backend] can be more efficient.
        /// </remarks>
        public AdaptiveRadixTree<TKey, TValue> Optimize(Stream storageStream = null) {
            var res = new AdaptiveRadixTree<TKey, TValue>(storageStream, m_keyEncoder, m_valueEncoder, m_keyDecoder, m_valueDecoder);
    
            var breadthFirstEnumerator   = new PathEnumerator();
            var items                    = breadthFirstEnumerator.Run(
                new NodePointer(0, m_rootPointer), 
                this.Stream, 
                true, 
                true, 
                null, 
                -1, 
                PathEnumerator.TraversalAlgorithm.BreadthFirst);
    
            // this could be massively sped up by using a redblacktree instead of sortedlist
    
            // sorted by _old
            var oldAddressesOrdered      = new (long _old, long _oldStart, long _new)[this.Count];
            int oldAddressesOrderedCount = 0;
            bool first                   = true;
            var rawValueBuffer           = new Buffer();
            long alloc_position          = NODE_POINTER_BYTE_SIZE;
    
            foreach(var path in items) {
                var last               = path.Trail[path.Trail.Count - 1];
                int size               = last.CalculateNodeSize();
                long oldAddress        = last.Pointer.Target;
                long oldAddressAddress = last.Pointer.Address;
                long newAddress;
    
                // copy node from tree to tree
                if(last.Type != NodeType.Leaf) {
                    var rawNode         = last.GetRawNode(breadthFirstEnumerator);
                    var num_children    = rawNode[1];
                    var current_size    = size;
    
                    // optimize space
                    if(last.Type > NodeType.Node4 && num_children <= MaxChildCount(last.Type - 1)) {
                        // if the node is oversized versus the current needs, then downsize it
                        DowngradeNode(rawNode, last.Type);
                        current_size = CalculateNodeSize(last.Type - 1);
                    }
    
                    newAddress          = alloc_position; // res.Alloc(current_size);
                    alloc_position     += current_size;
                    res.Stream.Position = newAddress;
                    res.Stream.Write(rawNode, 0, current_size);
                } else {
                    //var rawValue = new ConvertResult(last.ValueBuffer) { Length = last.ValueLength };
                    //// shift-down
                    //if(last.ValueIndex != 0)
                    //    BlockCopy(last.ValueBuffer, last.ValueIndex, last.ValueBuffer, 0, last.ValueLength);
    
                    // just to be sure, dont toy with the valuebuffer even if it should be fine
                    // this is in order to avoid potential future issues with code changes
                    Buffer bufferValue;
                    if(last.ValueIndex != 0) {
                        bufferValue        = rawValueBuffer;
                        rawValueBuffer.EnsureCapacity(last.ValueLength);
                        bufferValue.Length = last.ValueLength;
                        BlockCopy(last.ValueBuffer, last.ValueIndex, bufferValue.Content, 0, last.ValueLength);
                    } else
                        bufferValue = new Buffer(last.ValueBuffer) { Length = last.ValueLength };
    
                    newAddress = res.CreateLeafNode(last.GetKeyRaw(path), bufferValue, alloc_size => { var temp = alloc_position; alloc_position += alloc_size; return temp; });
                }
    
                // fix root pointer
                if(first) {
                    first = false;
                    res.m_rootPointer = newAddress;
                }
    
                // fix pointer to current
                var index                   = ~BinarySearch(oldAddressesOrderedCount, oldAddressAddress);
                var (_old, _oldStart, _new) = oldAddressesOrdered[index];
    
                WriteNodePointer(res.m_buffer, 0, newAddress);
                res.Stream.Position = _new + (oldAddressAddress - _oldStart);
                res.Stream.Write(res.m_buffer, 0, NODE_POINTER_BYTE_SIZE);
                    
                BinarySearchInsert(oldAddress + size, oldAddress, newAddress);
    
                res.LongCount++;
            }
    
            res.m_memoryManager.Load(new[] { ((long)0, alloc_position) });
    
            return res;
    
            int BinarySearch(int length, long value) {
                int min = 0;
                int max = min + length - 1;
                    
                while(min <= max) {
                    int median = (min + max) >> 1;
                    var diff   = oldAddressesOrdered[median]._old - value;
                        
                    if(diff < 0)
                        min = median + 1;
                    else if(diff > 0)
                        max = median - 1;
                    else
                        return median;
                }
                    
                return ~min;
            }
            void BinarySearchInsert(long oldAddress, long oldStart, long newAddress) {
                if(oldAddressesOrderedCount == oldAddressesOrdered.Length)
                    Array.Resize(ref oldAddressesOrdered, oldAddressesOrderedCount * 2);
    
                var index = ~BinarySearch(oldAddressesOrderedCount, oldAddress);
                if(index < oldAddressesOrderedCount) {
                    // up-shift
                    var count = oldAddressesOrderedCount - index;
                    Array.Copy(oldAddressesOrdered, index, oldAddressesOrdered, index + 1, count);
                }
                oldAddressesOrdered[index] = (oldAddress, oldStart, newAddress);
                oldAddressesOrderedCount++;
            }
        }
        #endregion
        #region CalculateShortestUniqueKey()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    Returns the shortest unique key path for the given value.
        ///    This is useful if you want to return the shortest unique identifier to point to resources.
        ///    
        ///    Returns the shortest length within encodedKey that makes the key uniquely identifiable.
        ///    Returns at least one character even if the key shares no branches (ex: items=abc (and no other items)  shortestkey(abc)=a).
        /// </summary>
        public bool CalculateShortestUniqueKey(in TKey key, out TKey result) {
            var path = this.TryGetPath(in key, false, true);
    
            // if no root or not found
            if(path == null || !path.IsKeyExactMatch()) {
                result = default;
                return false;
            }
    
            var encodedKey       = path.EncodedSearchKey;
            var uniqueTrailStart = path.CalculateTrailUniquenessStart();
    
            int uniqueLength = 1;
            for(int i = 0; i < uniqueTrailStart; i++)
                uniqueLength += path.Trail[i].PartialKeyLength;
                
            result = encodedKey.GetPartialKey(this, ref uniqueLength);
            return true;
        }
        #endregion
    
        #region DebugDump()
        public string DebugDump(bool includeMemoryManagerMemoryDump = false) {
            var sb = new StringBuilder();
            long totalNodesSize = NODE_POINTER_BYTE_SIZE; // root pointer
    
            sb.AppendLine($"RootPointer @0 -> {m_rootPointer}");
            sb.AppendLine();
    
            try{
                DumpNodes();
                sb.AppendLine();
                sb.AppendLine($"total: {totalNodesSize} bytes");
            } catch(Exception ex) {
                sb.AppendLine();
                sb.AppendLine("EXCEPTION");
                sb.AppendLine("=============================");
                sb.AppendLine(ex.ToString());
            }
    
            sb.AppendLine();
            DumpMemoryManager();
            if(includeMemoryManagerMemoryDump)
                DumpMemoryManagerMemory();
            sb.AppendLine();
    
            try{
                DumpItems();
            } catch(Exception ex) {
                sb.AppendLine();
                sb.AppendLine("EXCEPTION");
                sb.AppendLine("=============================");
                sb.AppendLine(ex.ToString());
            }
    
    
            return sb.ToString();
    
            void DumpNodes() {
                var raw = new byte[4096];
                foreach(var path in new PathEnumerator().Run(new NodePointer(0, m_rootPointer), this.Stream, true, true)) {
                    var last              = path.Trail[path.Trail.Count - 1];
                    TKey key              = default;
                    bool format_exception = false;
                    try {
                        key = last.GetKey(this, path, ref raw);
                    } catch(FormatException) {
                        // occurs when a key part simply doesnt make sense on its own
                        // this isnt an error per-se, since it can occur naturally
                        // you're not meant to ever read "key parts", only full keys
                        format_exception = true;
                    }
                    string value    = last.Type != NodeType.Leaf ? null : "  value=" + last.GetValue(this).ToString();
                    var size        = last.CalculateNodeSize();
                    totalNodesSize += size;
    
                    sb.Append(' ', (path.Trail.Count - 1) * 3);
                    sb.AppendLine($"[@{last.Pointer.Target} ({size} bytes)] key='{(!format_exception ? key.ToString().Replace("\0", "\\0") : "format_exception")}'  {last.Type.ToString()}{value?.ToString().Replace("\0", "\\0")}");
                }
            }
            void DumpItems() {
                var items = this.Items.ToList();
                long index = 0;
                sb.AppendLine($"ITEMS ({items.Count})");
                sb.AppendLine("=============================");
                foreach(var item in items)
                    sb.AppendLine($"[{index++}] {item.Key.ToString().Replace("\0", "\\0")} - {item.Value?.ToString().Replace("\0", "\\0")}");
            }
            void DumpMemoryManager() {
                var sub_memory_managers = new[] { 
                    new { MemManager = m_memoryManagerNode4,   VarName = nameof(m_memoryManagerNode4) },
                    new { MemManager = m_memoryManagerNode8,   VarName = nameof(m_memoryManagerNode8) },
                    new { MemManager = m_memoryManagerNode16,  VarName = nameof(m_memoryManagerNode16) },
                    new { MemManager = m_memoryManagerNode32,  VarName = nameof(m_memoryManagerNode32) },
                    new { MemManager = m_memoryManagerNode64,  VarName = nameof(m_memoryManagerNode64) },
                    new { MemManager = m_memoryManagerNode128, VarName = nameof(m_memoryManagerNode128) },
                    new { MemManager = m_memoryManagerNode256, VarName = nameof(m_memoryManagerNode256) },
                };
                var sum_preallocated = sub_memory_managers.Sum(o => o.MemManager.TotalFree);
    
                sb.AppendLine($"MEMORY (capacity={m_memoryManager.Capacity})");
                sb.AppendLine("=============================");
                sb.AppendLine($"used={m_memoryManager.TotalAllocated} - {sum_preallocated} (pre-allocated/re-use) = {m_memoryManager.TotalAllocated - sum_preallocated} actually used");
                sb.AppendLine($"free={m_memoryManager.TotalFree}");
    
                sb.AppendLine("----- pre-allocated/re-used chunks -----");
                foreach(var item in sub_memory_managers)
                    sb.AppendLine(string.Format("{0}.{1} = {2}   ({3} items)", item.VarName, nameof(FixedSizeMemoryManager.TotalFree), item.MemManager.TotalFree, item.MemManager.TotalFree / item.MemManager.AllocSize));
            }
            void DumpMemoryManagerMemory() {
                var avails = m_memoryManager.GetAvailableMemory().ToList();
    
                sb.AppendLine();
                sb.AppendLine($"MEMORY AVAILABLE ({avails.Count})");
                for(int i = 0; i < avails.Count; i++) {
                    var (address, length) = avails[i];
                    sb.AppendLine($"[{i}] @{address} - {length} bytes");
                }
            }
        }
        #endregion
        #region CalculateMetrics()
        /// <summary>
        ///     Returns metrics intended to check whether usage is warranted or not vs dictionary.
        ///     Also helps tuning of values based on data.
        /// </summary>
        public string CalculateMetrics() {
            var sb    = new StringBuilder();
            var nodes = new Dictionary<long, InternalMetrics>();
    
            foreach(var path in new PathEnumerator().Run(new NodePointer(0, m_rootPointer), this.Stream, false, false)) {
                foreach(var trail in path.Trail) {
                    var key = trail.Pointer.Target;
                    if(!nodes.TryGetValue(key, out var metrics)) {
                        // clone the node because this is meant for immediate use, otherwise objects are re-used
                        metrics = new InternalMetrics() { Node = (PathEnumerator.Node)trail.Clone() };
                        nodes.Add(key, metrics);
                    }
                    metrics.ReferenceCount++;
                }
            }
    
            var sub_memory_managers = new[] { 
                new { MemManager = m_memoryManagerNode4,   VarName = nameof(m_memoryManagerNode4) },
                new { MemManager = m_memoryManagerNode8,   VarName = nameof(m_memoryManagerNode8) },
                new { MemManager = m_memoryManagerNode16,  VarName = nameof(m_memoryManagerNode16) },
                new { MemManager = m_memoryManagerNode32,  VarName = nameof(m_memoryManagerNode32) },
                new { MemManager = m_memoryManagerNode64,  VarName = nameof(m_memoryManagerNode64) },
                new { MemManager = m_memoryManagerNode128, VarName = nameof(m_memoryManagerNode128) },
                new { MemManager = m_memoryManagerNode256, VarName = nameof(m_memoryManagerNode256) },
            };
            var sum_preallocated = sub_memory_managers.Sum(o => o.MemManager.TotalFree);
    
            sb.AppendLine($"nodes = {nodes.Values.Where(o => o.Node.Type != NodeType.Leaf).Count()}");
            sb.AppendLine($"leafs/items = {nodes.Values.Where(o => o.Node.Type == NodeType.Leaf).Count()}");
            sb.AppendLine();
            sb.AppendLine($"sum(keys.length) = {nodes.Values.Sum(o => o.Node.KeyLength * o.ReferenceCount)}");
            sb.AppendLine($"sum(keys.length) stored = {nodes.Values.Sum(o => o.Node.KeyLength)}");
            sb.AppendLine($"sum(keys.length) storage overhead = {nodes.Values.Where(o => o.Node.Type != NodeType.Leaf).Sum(o => MAX_PREFIX_LEN - o.Node.KeyLength)}   // every node reserves {nameof(MAX_PREFIX_LEN)}={MAX_PREFIX_LEN} characters, this is how much is unused");
            sb.AppendLine($"sum(keys.length) storage savings (w/o overhead) = {(nodes.Values.Sum(o => o.Node.KeyLength * o.ReferenceCount) - nodes.Values.Where(o => o.Node.Type != NodeType.Leaf).Sum(o => o.Node.KeyLength) - nodes.Values.Where(o => o.Node.Type == NodeType.Leaf).Sum(o => o.Node.KeyLength))}   // the real savings comparatively to storing every key fully");
            sb.AppendLine($"sum(keys.length) storage savings (w/ overhead)  = {(nodes.Values.Sum(o => o.Node.KeyLength * o.ReferenceCount) - nodes.Values.Where(o => o.Node.Type != NodeType.Leaf).Sum(o => MAX_PREFIX_LEN) - nodes.Values.Where(o => o.Node.Type == NodeType.Leaf).Sum(o => o.Node.KeyLength))}   // the real savings comparatively to storing every key fully");
            sb.AppendLine($"average(keys.length)     = {(nodes.Values.Sum(o => o.Node.KeyLength * o.ReferenceCount) / (double)nodes.Values.Where(o => o.Node.Type == NodeType.Leaf).Count())}");
            sb.AppendLine($"average(node.key_length) = {nodes.Values.Where(o => o.Node.Type != NodeType.Leaf).Average(o => o.Node.KeyLength)}   // used for tuning {nameof(MAX_PREFIX_LEN)}={MAX_PREFIX_LEN}");
            sb.AppendLine();
            sb.AppendLine($"sum(value.length)     = {nodes.Values.Where(o => o.Node.Type == NodeType.Leaf).Sum(o => o.Node.ValueLength)}");
            sb.AppendLine($"average(value.length) = {nodes.Values.Where(o => o.Node.Type == NodeType.Leaf).Average(o => o.Node.ValueLength)}");
            sb.AppendLine();
            long pointers1 = nodes.Values.Where(o => o.Node.Type != NodeType.Leaf).Sum(o => (long)MaxChildCount(o.Node.Type));
            sb.AppendLine($"count(pointers)        = {pointers1} * {nameof(NODE_POINTER_BYTE_SIZE)}({NODE_POINTER_BYTE_SIZE}) = {(pointers1 * NODE_POINTER_BYTE_SIZE)}");
            long pointers2 = nodes.Values.Count;
            sb.AppendLine($"count(pointers) used   = {pointers2} * {nameof(NODE_POINTER_BYTE_SIZE)}({NODE_POINTER_BYTE_SIZE}) = {(pointers2 * NODE_POINTER_BYTE_SIZE)}");
            long pointers3 = pointers1 - pointers2;
            sb.AppendLine($"count(pointers) unused = {pointers3} * {nameof(NODE_POINTER_BYTE_SIZE)}({NODE_POINTER_BYTE_SIZE}) = {(pointers3 * NODE_POINTER_BYTE_SIZE)}");
            sb.AppendLine();
            var groups = nodes.Values.OrderBy(o => o.Node.Type).GroupBy(o => o.Node.Type).ToDictionary(o => o.Key, o => o.Count());
            foreach(var group in groups)
                sb.AppendLine($"group[{group.Key.ToString()}].Count = {group.Value}");
            sb.AppendLine();
            sb.AppendLine($"tune {nameof(MAX_PREFIX_LEN)}={MAX_PREFIX_LEN} for nodes; if most node.keys.length are low, consider decreasing. If most node.keys.length are high/near max, consider increasing. Leaves have no limit.");
            var groups2 = nodes.Values.Where(o => o.Node.Type != NodeType.Leaf).OrderBy(o => o.Node.KeyLength).GroupBy(o => o.Node.KeyLength).ToDictionary(o => o.Key, o => o.Count());
            foreach(var group in groups2)
                sb.AppendLine($"node.keys.length[{group.Key.ToString()}].Count = {group.Value}{(group.Key == 0 ? "   // only root node can have zero length key" : null)}");
            var groups3 = nodes.Values.Where(o => o.Node.Type == NodeType.Leaf).OrderBy(o => o.Node.KeyLength).GroupBy(o => o.Node.KeyLength).ToDictionary(o => o.Key, o => o.Count());
            foreach(var group in groups3)
                sb.AppendLine($"leafs.keys.length[{group.Key.ToString()}].Count = {group.Value}");
            sb.AppendLine();
            sb.AppendLine($"memory_manager.capacity        = {m_memoryManager.Capacity}");
            sb.AppendLine($"memory_manager.total_allocated = {m_memoryManager.TotalAllocated} - {sum_preallocated} (pre-allocated/re-use) = {m_memoryManager.TotalAllocated - sum_preallocated} actually used");
            sb.AppendLine($"memory_manager.total_free      = {m_memoryManager.TotalFree}");
            sb.AppendLine("----- pre-allocated/re-used chunks -----");
            foreach(var item in sub_memory_managers)
                sb.AppendLine(string.Format("{0}.{1} = {2}   ({3} items)", item.VarName, nameof(FixedSizeMemoryManager.TotalFree), item.MemManager.TotalFree, item.MemManager.TotalFree / item.MemManager.AllocSize));

            //sb.AppendLine();
            //sb.AppendLine("allocated chunks");
            //var allocated_chunks = m_memoryManager
            //    .GetAllocatedMemory()
            //    .GroupBy(o => o.length)
            //    .Select(o => new { o.Key, Count =  o.Count() })
            //    .OrderBy(o => o.Key);
            //foreach(var item in allocated_chunks)
            //    sb.AppendLine($"[size={item.Key}] count={item.Count}");

            return sb.ToString();
        }
        private class InternalMetrics {
            public PathEnumerator.Node Node;
            public long ReferenceCount;
        }
        #endregion

            
        // add() logic here
        #region private TryAddItem()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    Returns true if the item was added, false if existing.
        ///    Throws ArgumentException on empty key.
        /// </summary>
        /// <param name="key">Only used if path = null.</param>
        private bool TryAddItem(Path path, in TKey key, in TValue value) {
            if(path != null && path.IsKeyExactMatch())
                // key already exists
                return false;
    
            var valueBuffer = m_valueBuffer;
            m_valueEncoder(value, valueBuffer);
    
            if(path != null) {
                var encodedKeySpan = new ReadOnlySpan<byte>(path.EncodedSearchKey.Content, 0, path.EncodedSearchKey.Length);
    
                if(path.Trail.Count != 0) {
                    // if theres only a partial match
                    var last              = path.Trail[path.Trail.Count - 1];
                    var key_match_length  = path.CalculateKeyMatchLength();
                    var partial_key_match = encodedKeySpan.Slice(key_match_length - last.PartialKeyMatchLength, last.PartialKeyMatchLength);
                    var new_leaf_key      = encodedKeySpan.Slice(key_match_length);
    
                    var new_leaf_address = this.CreateLeafNode(in new_leaf_key, valueBuffer);
    
                    // need to reload m_buffer with 'last' for AddItem()
                    this.Stream.Position = last.Address;
                    // intentionally dont try to load all if leaf, since it wont be entirely read most times
                    int readBytes        = this.Stream.Read(m_buffer, 0, CalculateNodePrefetchSize(last.Type));
    
                    this.AddItem(
                        new NodePointer(last.ParentPointerAddress, last.Address),
                        //key_match_length,
                        in partial_key_match,
                        last.PartialKeyLength,
                        last.ValueLength,
                        readBytes,
                        new_leaf_key.Length > 0 ? new_leaf_key[0] : (byte)0,
                        new_leaf_address);
                } else {
                    // if theres literally nothing in common with root node, then we must add to root node
                    // keep in mind we share no partial key at all with the root
    
                    var new_leaf_address = this.CreateLeafNode(in encodedKeySpan, valueBuffer);
    
                    // need to reload m_buffer for AddItem()
                    var root             = m_rootPointer;
                    this.Stream.Position = root;
                    var nodeType         = (NodeType)this.Stream.ReadByte();
                    m_buffer[0]          = (byte)nodeType;
                    // intentionally dont try to load all if leaf, since it wont be entirely read most times
                    int readBytes        = this.Stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                    this.AddItem(
                        new NodePointer(0, root),
                        //0,
                        ReadOnlySpan<byte>.Empty,
                        0,
                        valueBuffer.Length,
                        readBytes,
                        path.EncodedSearchKey.Content[0],
                        new_leaf_address);
                }
            } else {
                // if theres no root (m_rootPointer == 0)
                var keyBuffer = m_keyBuffer;
                m_keyEncoder(key, keyBuffer); 
                if(keyBuffer.Length == 0)
                    throw new ArgumentException(nameof(key));
                EscapeLeafKeyTerminator(keyBuffer);
    
                var encodedKeySpan = new ReadOnlySpan<byte>(keyBuffer.Content, 0, keyBuffer.Length);
    
                // leaf node
                var new_leaf_address = this.CreateLeafNode(in encodedKeySpan, valueBuffer);
    
                WriteNodePointer(m_buffer, 0, new_leaf_address);
                this.Stream.Position = 0;
                this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
                m_rootPointer = new_leaf_address;
            }
    
            this.LongCount++;
            return true;
        }
        #endregion
        #region private AddItem()
        /// <summary>
        ///     Adds an item to a node, and upgrades the node if necessary.
        ///     This code assumes the node doesnt contain the item/branch already.
        /// </summary>
        /// <param name="partial_key_length">Includes LEAF_NODE_KEY_TERMINATOR</param>
        /// <param name="bufferRead">Only used in some specific scenarios</param>
        private void AddItem(NodePointer ptr, in ReadOnlySpan<byte> partial_key_match, int partial_key_length, int value_length, int bufferRead, byte new_item_c, long new_item_address) {
            var nodeType = (NodeType)m_buffer[0];
    
            if(nodeType != NodeType.Leaf) {
                var num_children = m_buffer[1];
    
                // if fully matching the current node
                if(partial_key_match.Length == partial_key_length) {
                    // if space remains in node
                    if(num_children < MaxChildCount(nodeType)) {
                        AddItemToNonFullNode(m_buffer, 0, new_item_c, new_item_address);
                        this.Stream.Position = ptr.Target;
                        this.Stream.Write(m_buffer, 0, CalculateNodeSize(nodeType));
                    } else {
                        // if the node is at capacity, then upgrade it, then add
                        UpgradeNode(m_buffer, nodeType);
                        AddItemToNonFullNode(m_buffer, 0, new_item_c, new_item_address);
    
                        var new_node_type    = (NodeType)m_buffer[0];
                        var new_size         = CalculateNodeSize(new_node_type);
                        var new_address      = this.Alloc(new_node_type);
                        this.Stream.Position = new_address;
                        this.Stream.Write(m_buffer, 0, new_size);
    
                        WriteNodePointer(m_buffer, 0, new_address);
                        this.Stream.Position = ptr.Address;
                        this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
    
                        if(ptr.Address == 0)
                            m_rootPointer = new_address;
    
                        this.Free(ptr.Target, nodeType);
                    }
                } else {
                    // if partially matching the current node
                    // then we need to shorten the key
                    //
                    // ex:      [abc]         try to insert 'abcde\0'         [abc]
                    //         /     \        which should generate:         /     \
                    //        /       \                                     /       \
                    //      [def]   [xyz]                                 [de]    [xyz]
                    //                                                   /    \
                    //                                               [\0]      [f]
                    //
                    // also, its possible that some branches can be merged 
                    // ex:    ...           if the node were cutting ([def]) has only a single child, 
                    //       /              and that child is not a leaf, then attempt to merge both if possible
                    //     [def]            we do not try to always move-up the data, as that could result in a lot of writes
                    //       |              and essentially trashing if doing a lot of updates
                    //      [g]             as a compromise we only merge if both partial keys fit within one, otherwise, we leave as-is
                    var old_branch_prefixes = new ReadOnlySpan<byte>(m_buffer, 3 + partial_key_match.Length, partial_key_length - partial_key_match.Length).ToArray();
                    bool use_node_merge     = false;
    
                    if(num_children == 1) {
                        // read the node at the end of m_buffer as it can't overlap with the current entry
                        var child            = GetMinChild(m_buffer, ptr.Target);
                        this.Stream.Position = child.Target;
                        var child_nodetype   = (NodeType)this.Stream.ReadByte();
                        if(child_nodetype != NodeType.Leaf) {
                            var child_size            = CalculateNodeSize(child_nodetype);
                            var childWriteIndex       = m_buffer.Length - child_size;
                            m_buffer[childWriteIndex] = (byte)child_nodetype;
                            var readBytes             = this.Stream.Read(m_buffer, childWriteIndex + 1, child_size - 1) + 1;
                            var child_partial_length  = m_buffer[childWriteIndex + 2];
    
                            // if merge confirmed, then prepend old_branch_prefixes on secondary node
                            if(child_partial_length + old_branch_prefixes.Length <= MAX_PREFIX_LEN) {
                                use_node_merge       = true;
                                m_buffer[childWriteIndex + 2] = unchecked((byte)(child_partial_length + old_branch_prefixes.Length));
                                BlockCopy(m_buffer, childWriteIndex + 3, m_buffer, childWriteIndex + 3 + old_branch_prefixes.Length, child_partial_length);
                                BlockCopy(old_branch_prefixes, 0, m_buffer, childWriteIndex + 3, old_branch_prefixes.Length);
                                var old_branch_address = this.Alloc(child_nodetype);
                                var old_branch_c       = old_branch_prefixes[0];
                                this.Stream.Position   = old_branch_address;
                                this.Stream.Write(m_buffer, childWriteIndex, child_size);
    
                                CreateEmptyNode4(m_buffer, 0, in partial_key_match);
                                AddItemToNonFullNode(m_buffer, 0, old_branch_c, old_branch_address);
                                AddItemToNonFullNode(m_buffer, 0, new_item_c, new_item_address);
                                var size2              = CalculateNodeSize(NodeType.Node4);
                                var new_branch_address = this.Alloc(NodeType.Node4);
                                this.Stream.Position   = new_branch_address;
                                this.Stream.Write(m_buffer, 0, size2);
    
                                WriteNodePointer(m_buffer, 0, new_branch_address);
                                this.Stream.Position = ptr.Address;
                                this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
    
                                if(ptr.Address == 0)
                                    m_rootPointer = new_branch_address;
    
                                this.Free(ptr.Target, nodeType);
                                this.Free(child.Target, child_nodetype);
                            }
                        }
                    }
    
                    if(!use_node_merge) {
                        m_buffer[2] = unchecked((byte)old_branch_prefixes.Length);
                        BlockCopy(old_branch_prefixes, 0, m_buffer, 3, old_branch_prefixes.Length);
                        var size               = CalculateNodeSize(nodeType);
                        var old_branch_address = this.Alloc(nodeType);
                        var old_branch_c       = old_branch_prefixes[0];
                        this.Stream.Position   = old_branch_address;
                        this.Stream.Write(m_buffer, 0, size);
    
                        CreateEmptyNode4(m_buffer, 0, in partial_key_match);
                        AddItemToNonFullNode(m_buffer, 0, old_branch_c, old_branch_address);
                        AddItemToNonFullNode(m_buffer, 0, new_item_c, new_item_address);
                        var size2              = CalculateNodeSize(NodeType.Node4);
                        var new_branch_address = this.Alloc(NodeType.Node4);
                        this.Stream.Position   = new_branch_address;
                        this.Stream.Write(m_buffer, 0, size2);
    
                        WriteNodePointer(m_buffer, 0, new_branch_address);
                        this.Stream.Position = ptr.Address;
                        this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
    
                        if(ptr.Address == 0)
                            m_rootPointer = new_branch_address;
    
                        this.Free(ptr.Target, nodeType);
                    }
                }
            } else { // leaf
                if(partial_key_match.Length > 0 && partial_key_match.Length < partial_key_length - 1) { // partial_key_match.Length > 0
                    // if this breaks, it means you called this method when the key already existed
                    System.Diagnostics.Debug.Assert(partial_key_match.Length < partial_key_length);
    
                    // need to shorten the key of the leaf by partial_key_match.Length bytes
                    var currentLeafSize = CalculateLeafNodeSize(partial_key_length, value_length);
                    var resizedLeafSize = CalculateLeafNodeSize(partial_key_length - partial_key_match.Length, value_length);
                    var bytes_removed   = currentLeafSize - resizedLeafSize;
    
                    int index = 1;
                    WriteVarUInt64(m_buffer, ref index, unchecked((ulong)(partial_key_length - partial_key_match.Length)));
                    WriteVarUInt64(m_buffer, ref index, unchecked((ulong)value_length));
                    var remaining           = resizedLeafSize - index;
                    long readPosition       = ptr.Target + 1 + CalculateVarUInt64Length(unchecked((ulong)partial_key_length)) + CalculateVarUInt64Length(unchecked((ulong)value_length)) + bytes_removed;
                    var resizedLeafAddress  = this.Alloc(resizedLeafSize);
                    long writePosition      = resizedLeafAddress;
                    bool firstCharacterRead = false;
                    byte firstCharacter     = 0;
    
                    // copy prev leaf to new leaf (with shorter key)
                    while(remaining > 0) {
                        var processed = Math.Min(remaining, m_buffer.Length - index);
    
                        this.Stream.Position = readPosition;
                        int readBytes = this.Stream.Read(m_buffer, index, processed);
    
                        this.Stream.Position = writePosition;
                        this.Stream.Write(m_buffer, 0, index + readBytes);
    
                        if(!firstCharacterRead && readBytes > 0) {
                            firstCharacter     = m_buffer[index];
                            firstCharacterRead = true;
                        }
    
                        readPosition  += readBytes;
                        writePosition += index + readBytes;
                        remaining     -= readBytes;
                        index          = 0;
                    }
    
                    var children = new List<(byte c, long address)>(2) {
                        (firstCharacter, resizedLeafAddress),
                        (new_item_c, new_item_address)
                    };
    
                    // then build the entire partial_key_match branch that has just been removed from key
                    for(int i = ((partial_key_match.Length - 1) / MAX_PREFIX_LEN) * MAX_PREFIX_LEN; i >= 0; i -= MAX_PREFIX_LEN) {
                        var current_node_partial_key = partial_key_match.Slice(i, Math.Min(MAX_PREFIX_LEN, partial_key_match.Length - i));
                        CreateEmptyNode4(m_buffer, 0, in current_node_partial_key);
                        foreach(var (c, address) in children)
                            AddItemToNonFullNode(m_buffer, 0, c, address);
                        var current_node_address = this.Alloc(NodeType.Node4);
                        this.Stream.Position = current_node_address;
                        this.Stream.Write(m_buffer, 0, CalculateNodeSize(NodeType.Node4));
    
                        children.Clear();
                        children.Add((current_node_partial_key[0], current_node_address));
                    }
                        
                    WriteNodePointer(m_buffer, 0, children[0].address);
                    this.Stream.Position = ptr.Address;
                    this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
    
                    if(ptr.Address == 0)
                        m_rootPointer = children[0].address;
                        
                    this.Free(ptr.Target, currentLeafSize);
                } else if(partial_key_match.Length > 0) {
                    // this case is basically when your tree is [aaaa] and you try to add [aaaaXXXX]
    
                    System.Diagnostics.Debug.Assert(partial_key_match.Length == partial_key_length - 1);
    
                    // make a new leaf for the current leaf, with key being just LEAF_NODE_KEY_TERMINATOR
                    // copy the value from whats been already read
                    var valueBuffer = m_valueBuffer;
    
                    valueBuffer.EnsureCapacity(value_length);
                    valueBuffer.Length               = value_length;
                    var current_leaf_raw_value_start = 1 + CalculateVarUInt64Length(unchecked((ulong)partial_key_length)) + CalculateVarUInt64Length(unchecked((ulong)value_length)) + partial_key_length;
                    var size                         = Math.Min(bufferRead - current_leaf_raw_value_start, value_length);
                    var remaining                    = value_length - size;
                    BlockCopy(m_buffer, current_leaf_raw_value_start, valueBuffer.Content, 0, size);
                    if(remaining > 0) {
                        this.Stream.Position = ptr.Target + bufferRead;
                        this.Stream.Read(valueBuffer.Content, size, remaining);
                    }
    
                    var current_leaf_address = this.CreateLeafNode(ReadOnlySpan<byte>.Empty, valueBuffer);
                    var current_leaf_c       = LEAF_NODE_KEY_TERMINATOR;
    
                    CreateEmptyNode4(m_buffer, 0, in partial_key_match);
                    AddItemToNonFullNode(m_buffer, 0, current_leaf_c, current_leaf_address);
                    AddItemToNonFullNode(m_buffer, 0, new_item_c, new_item_address);
    
                    size                 = CalculateNodeSize(NodeType.Node4);
                    var new_leaf_address = this.Alloc(NodeType.Node4);
                    this.Stream.Position = new_leaf_address;
                    this.Stream.Write(m_buffer, 0, size);
    
                    WriteNodePointer(m_buffer, 0, new_leaf_address);
                    this.Stream.Position = ptr.Address;
                    this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
    
                    if(ptr.Address == 0) // as explained, this should always be true
                        m_rootPointer = new_leaf_address;
    
                    this.Free(ptr.Target, CalculateLeafNodeSize(partial_key_length, value_length));
                } else { // partial_key_match.Length == 0
                    // this case is basically when your tree contains only one node [aaaa] you you try to insert [bbbb]
                    //
                    // for what its worth, this *only* makes sense if the current leaf is the root node
                    // in every other case, you should have *some* common prefix with the parent branch and thus not end up here
                    // ie: [*root* AB] -> [CDE] -> [*leaf* F]
                    //     if adding 'ABCDEXXXXX', then you should be adding to the node [CDE] and *not* the leaf 'F'
    
                    // so the code here assumes ptr.Address==0
                    // which also means we intentionally create a node4 without any partial key
    
                    // this works because all leafs must contain 1+ partial_length
                    var current_leaf_c       = m_buffer[CalculateVarUInt64LengthEncoded(m_buffer[1]) + 1];
                    var current_leaf_address = ptr.Target;
    
                    CreateEmptyNode4(m_buffer, 0, in partial_key_match); // note: partial_key_match is empty
                    AddItemToNonFullNode(m_buffer, 0, current_leaf_c, current_leaf_address);
                    AddItemToNonFullNode(m_buffer, 0, new_item_c, new_item_address);
    
                    var size             = CalculateNodeSize(NodeType.Node4);
                    var new_leaf_address = this.Alloc(NodeType.Node4);
                    this.Stream.Position = new_leaf_address;
                    this.Stream.Write(m_buffer, 0, size);
    
                    WriteNodePointer(m_buffer, 0, new_leaf_address);
                    this.Stream.Position = ptr.Address;
                    this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
    
                    if(ptr.Address == 0) // as explained, this should always be true
                        m_rootPointer = new_leaf_address;
                }
            }
        }
        #endregion
        #region private static AddItemToNonFullNode()
        private static void AddItemToNonFullNode(byte[] buffer, int index, byte c, long address) {
            // just rewrite the entire node, since that is likely faster than selectively writing just what changed and make multiple calls to Stream.Write()
            // keep in mind, Stream.Write() can be quite convoluted, with writeahead logs or even memory paging and whatnot
    
            var nodeType      = (NodeType)buffer[index + 0];
            var num_children  = buffer[index + 1];
            int key_index     = CalculateKeysIndex(nodeType) + index;
            buffer[index + 1] = unchecked((byte)(num_children + 1));
    
            switch(nodeType) {
                case NodeType.Node4:
                case NodeType.Node8:
                    // add at the end, since there is no ordering
                    //var insertIndex = BinarySearchInsert(m_buffer, key_index, num_children, c);
                    buffer[key_index + num_children] = c;
                    WriteNodePointer(buffer, key_index + MaxChildCount(nodeType) + num_children * NODE_POINTER_BYTE_SIZE, address);
                    break;
                case NodeType.Node16:
                case NodeType.Node32:
                    var insertIndex = BinarySearchInsert(buffer, key_index, num_children, c);
                    key_index      += MaxChildCount(nodeType);
                    var start       = key_index + insertIndex * NODE_POINTER_BYTE_SIZE;
                    if(insertIndex < num_children) {
                        // up-shift
                        var count = (num_children - insertIndex) * NODE_POINTER_BYTE_SIZE;
                        BlockCopy(buffer, start, buffer, start + NODE_POINTER_BYTE_SIZE, count);
                    }
                    WriteNodePointer(buffer, start, address);
                    break;
                case NodeType.Node64:
                case NodeType.Node128:
                    // add at the end, since there is no ordering
                    buffer[key_index + c] = unchecked((byte)(num_children + 1));
                    WriteNodePointer(buffer, key_index + 256 + num_children * NODE_POINTER_BYTE_SIZE, address);
                    break;
                case NodeType.Node256:
                    WriteNodePointer(buffer, key_index + c * NODE_POINTER_BYTE_SIZE, address);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        #endregion
        #region private static UpgradeNode()
        /// <summary>
        ///     Upgrades Node 4,8,16,32,64,128 to one level above.
        /// </summary>
        private static void UpgradeNode(byte[] buffer, NodeType currentNodeType) {
            // node4 & node8                         |  node16 & node32                       |  node64 & node128                      |  node256
            // **********************************************************************************************************************************************************
            // byte                  node_type       |  byte                  node_type       |  byte                  node_type       |  byte                  node_type
            // byte                  num_children    |  byte                  num_children    |  byte                  num_children    |  byte                  num_children
            // byte                  partial_length  |  byte                  partial_length  |  byte                  partial_length  |  byte                  partial_length
            // char[MAX_PREFIX_LEN]  partial         |  char[MAX_PREFIX_LEN]  partial         |  char[MAX_PREFIX_LEN]  partial         |  char[MAX_PREFIX_LEN]  partial
            // char[n]               keys (no-order) |  char[n]               keys (ordered)  |  char[256]             keys (index+1)  |  ---
            // NodePointer[n]        children        |  NodePointer[n]        children        |  NodePointer[n]        children        |  NodePointer[n]        children
    
            var upgradedNodeType = currentNodeType + 1;
    
            if(currentNodeType >= NodeType.Node4 && currentNodeType <= NodeType.Node16) {
                var num_children = buffer[1];
                var index        = CalculateKeysIndex(currentNodeType);
                    
                if(currentNodeType == NodeType.Node8)
                    // must order 8 keys/children
                    SelectionSortNode8Keys(buffer);
    
                BlockCopy(
                    buffer, 
                    index + MaxChildCount(currentNodeType),
                    buffer, 
                    index + MaxChildCount(upgradedNodeType),
                    num_children * NODE_POINTER_BYTE_SIZE);
            } else if(currentNodeType == NodeType.Node32) {
                var num_children = buffer[1];
                var index        = CalculateKeysIndex(NodeType.Node32);
    
                BlockCopy(
                    buffer, 
                    index + MaxChildCount(NodeType.Node32),
                    buffer, 
                    index + 256,
                    num_children * NODE_POINTER_BYTE_SIZE);
    
                // then move the keys to the node64 format
                int keysCopyIndex = buffer.Length - num_children - 1;
                BlockCopy(buffer, index, buffer, keysCopyIndex, num_children);
                Array.Clear(buffer, index, 256);
                for(int i = 0; i < num_children; i++)
                    buffer[index + buffer[keysCopyIndex + i]] = unchecked((byte)(i + 1));
            } else if(currentNodeType == NodeType.Node64) {
                // intentionally empty
                // additions are all at the end, and they arent read, so nothing further needs to be done
            } else if(currentNodeType == NodeType.Node128) {
                var index                = CalculateKeysIndex(NodeType.Node128);
                var keysAndChildrenSize  = 256 + MaxChildCount(NodeType.Node128) * NODE_POINTER_BYTE_SIZE;
                var keysAndChildrenIndex = buffer.Length - keysAndChildrenSize - 1;
                BlockCopy(buffer, index, buffer, keysAndChildrenIndex, keysAndChildrenSize);
                Array.Clear(buffer, index, MaxChildCount(NodeType.Node256) * NODE_POINTER_BYTE_SIZE);
                for(int i = 0; i < 256; i++) {
                    var item = buffer[keysAndChildrenIndex + i];
                    if(item == 0)
                        continue;
                    item--;
                    //BlockCopy(buffer, keysAndChildrenIndex + 256 + item * NODE_POINTER_BYTE_SIZE, buffer, index + i * NODE_POINTER_BYTE_SIZE, NODE_POINTER_BYTE_SIZE);
                    // this runs probably faster than blockcopy
                    var child = ReadNodePointer(buffer, keysAndChildrenIndex + 256 + item * NODE_POINTER_BYTE_SIZE);
                    WriteNodePointer(buffer, index + i * NODE_POINTER_BYTE_SIZE, child);
                }
            } else
                throw new ArgumentException(nameof(currentNodeType));
                
            buffer[0] = (byte)upgradedNodeType;
        }
        #endregion
        #region private static SelectionSortNode8Keys()
        [MethodImpl(AggressiveInlining)]
        private static void SelectionSortNode8Keys(byte[] buffer) {
            var num_children = buffer[1]; // should be 8, but could be theoretically 0-8
            var keys_start   = CalculateKeysIndex(NodeType.Node8);
    
            for(int i = 0; i < num_children - 1; i++) {
                int min_index  = i;
                byte min_value = buffer[keys_start + i];
    
                for(int j = i + 1; j < num_children; j++) {
                    var current = buffer[keys_start + j];
                    if(current < min_value) {
                        min_index = j;
                        min_value = current;
                    }
                }
    
                if(min_index != i) {
                    //swap(buffer[keys_start + i], buffer[keys_start + min_index]);
                    var swap                       = buffer[keys_start + i];
                    buffer[keys_start + i]         = min_value;
                    buffer[keys_start + min_index] = swap;
                    //swap(children);
                    var address1 = keys_start + MaxChildCount(NodeType.Node8) + i * NODE_POINTER_BYTE_SIZE;
                    var address2 = keys_start + MaxChildCount(NodeType.Node8) + min_index * NODE_POINTER_BYTE_SIZE;
                    var swap2 = ReadNodePointer(buffer, address1);
                    var swap3 = ReadNodePointer(buffer, address2);
                    WriteNodePointer(buffer, address1, swap3);
                    WriteNodePointer(buffer, address2, swap2);
                }
            }
        }
        #endregion
        #region private static BinarySearchInsert()
        /// <summary>
        ///     Returns the insert position relative to min.
        ///     Throws ArgumentException on existing/duplicate key.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static int BinarySearchInsert(byte[] array, int min, int length, byte value) {
            var index = BinarySearch(array, min, length, value);
                
            if(index < 0) {
                index = ~index;
                if(index < min + length) {
                    // up-shift
                    var count = min + length - index;
                    BlockCopy(array, index, array, index + 1, count);
                }
                array[index] = value;
            } else
                throw new ArgumentException("The value already exists.", nameof(value));
    
            return index - min;
        }
        #endregion
    
        // remove() logic here
        #region private TryRemoveItem()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    Returns true if the item was removed, false if not found.
        ///    Throws ArgumentException on empty key.
        /// </summary>
        /// <param name="key">Only used if path = null.</param>
        private bool TryRemoveItem(Path path) {
            if(path == null || !path.IsKeyExactMatch())
                // key isnt found
                return false;
    
            int uniqueTrailStart = path.CalculateTrailUniquenessStart();
                
            int uniqueLength = 1;
            for(int i = 0; i < uniqueTrailStart; i++)
                uniqueLength += path.Trail[i].PartialKeyLength;
    
            // the item to remove within node
            var keyToRemove  = uniqueLength - 1 < path.EncodedSearchKey.Length ? path.EncodedSearchKey.Content[uniqueLength - 1] : LEAF_NODE_KEY_TERMINATOR;
            var nodeToModify = path.Trail[Math.Max(uniqueTrailStart - 1, 0)];
    
            // if removing the last item 
            var removingLastItem = this.LongCount == 1; // nodeToModify.Type != NodeType.Leaf && nodeToModify.ChildrenCount == 1;
            if(!removingLastItem) {
                var old_size = CalculateNodeSize(nodeToModify.Type);
                    
                this.Stream.Position = nodeToModify.Address;
                this.Stream.Read(m_buffer, 0, old_size);
    
                RemoveItemFromNode(m_buffer, 0, keyToRemove);
    
                // ex:      [abc]         try to delete 'abcde\0'         [abc]
                //         /     \        which should generate:         /     \
                //        /       \                                     /       \
                //      [de]     [xyz]                                [def]    [xyz]
                //      /  \
                //   [\0]   [f]
                //
                // also, its possible that some branches can be merged 
                // ex:    ...           if the node were cutting ([def]) has only a single child, 
                //       /              and that child is not a leaf, then attempt to merge both if possible
                //     [def]            we do not try to always move-up the data, as that could result in a lot of writes
                //       |              and essentially trashing if doing a lot of updates
                //      [g]             as a compromise we only merge if both partial keys fit within one, otherwise, we leave as-is
                bool use_node_merge      = false;
                if(nodeToModify.ChildrenCount == 2) {
                    var ptr              = GetMinChild(m_buffer, nodeToModify.Address);
                    this.Stream.Position = ptr.Target;
                    var offBranchType    = (NodeType)this.Stream.ReadByte();
                    
                    if(offBranchType != NodeType.Leaf) {
                        var new_size           = CalculateNodeSize(offBranchType);
                        var readBytes          = this.Stream.Read(m_buffer, old_size + 1, new_size - 1) + 1;
                        var partial_length     = m_buffer[2];
                        var off_partial_length = m_buffer[old_size + 2];
                    
                        if(partial_length + off_partial_length <= MAX_PREFIX_LEN) {
                            use_node_merge = true;
                    
                            // merge partial_key
                            m_buffer[old_size]     = (byte)offBranchType;
                            m_buffer[old_size + 2] = unchecked((byte)(partial_length + off_partial_length));
                            BlockCopy(m_buffer, old_size + 3, m_buffer, old_size + 3 + partial_length, off_partial_length);
                            BlockCopy(m_buffer, 3, m_buffer, old_size + 3, partial_length);
                    
                            var new_address      = this.Alloc(offBranchType);
                            this.Stream.Position = new_address;
                            this.Stream.Write(m_buffer, old_size, new_size);
                    
                            WriteNodePointer(m_buffer, 0, new_address);
                            this.Stream.Position = nodeToModify.ParentPointerAddress;
                            this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
                    
                            if(nodeToModify.ParentPointerAddress == 0)
                                m_rootPointer = new_address;
                    
                            this.Free(nodeToModify.Address, nodeToModify.Type);
                            this.Free(ptr.Target, offBranchType);
                        }
                    }
                }
    
                if(!use_node_merge){
                    var new_size   = old_size;
                    var alloc_type = nodeToModify.Type;
    
                    if(nodeToModify.ChildrenCount - 1 < MinChildCount(nodeToModify.Type)) {
                        DowngradeNode(m_buffer, nodeToModify.Type);
                        alloc_type = nodeToModify.Type - 1;
                        new_size   = CalculateNodeSize(alloc_type);
                    }
    
                    var new_address      = this.Alloc(alloc_type);
                    this.Stream.Position = new_address;
                    this.Stream.Write(m_buffer, 0, new_size);
    
                    WriteNodePointer(m_buffer, 0, new_address);
                    this.Stream.Position = nodeToModify.ParentPointerAddress;
                    this.Stream.Write(m_buffer, 0, NODE_POINTER_BYTE_SIZE);
    
                    if(nodeToModify.ParentPointerAddress == 0)
                        m_rootPointer = new_address;
    
                    this.Free(nodeToModify.Address, nodeToModify.Type);
                }
    
                // free memory
                for(int i = Math.Max(uniqueTrailStart, 1); i < path.Trail.Count; i++) {
                    var node  = path.Trail[i];
    
                    if(node.Type != NodeType.Leaf)
                        this.Free(node.Address, node.Type);
                    else
                        this.Free(node.Address, CalculateLeafNodeSize(node.PartialKeyLength, node.ValueLength));
                }
    
                this.LongCount--;
            } else
                // case where there is only 1 item in total, and its the one were removing
                this.Clear();
    
            return true;
        }
        #endregion
        #region private static RemoveItemFromNode()
        private static void RemoveItemFromNode(byte[] buffer, int index, byte c) {
            // just rewrite the entire node, since that is likely faster than selectively writing just what changed and make multiple calls to Stream.Write()
            // keep in mind, Stream.Write() can be quite convoluted, with writeahead logs or even memory paging and whatnot
    
            var nodeType      = (NodeType)buffer[index + 0];
            var num_children  = buffer[index + 1];
            int key_index     = CalculateKeysIndex(nodeType) + index;
            buffer[index + 1] = unchecked((byte)(num_children - 1));
    
            switch(nodeType) {
                case NodeType.Node4:
                case NodeType.Node8:
                    for(int i = 0; i < num_children; i++) {
                        if(buffer[key_index + i] == c) {
                            // downshift the few remaining bytes (dont use blockcopy with this few items)
                            for(int j = i + 1; j < num_children; j++)
                                buffer[j + key_index - 1] = buffer[j + key_index];
                            var maxChildCount = MaxChildCount(nodeType);
                            BlockCopy(
                                buffer, 
                                key_index + maxChildCount + (i + 1) * NODE_POINTER_BYTE_SIZE, 
                                buffer, 
                                key_index + maxChildCount + i * NODE_POINTER_BYTE_SIZE,
                                (num_children - i - 1) * NODE_POINTER_BYTE_SIZE);
                            break;
                        }
                    }
    
                    break;
                case NodeType.Node16:
                case NodeType.Node32:
                    // code below needs testing
    
                    var deleteIndex = BinarySearch(buffer, key_index, num_children, c) - key_index;
                    // down-shift
                    var count   = num_children - deleteIndex - 1;
                    BlockCopy(buffer, key_index + deleteIndex + 1, buffer, key_index + deleteIndex, count);
    
                    key_index  += MaxChildCount(nodeType);
                    var start   = key_index + deleteIndex * NODE_POINTER_BYTE_SIZE;
                    count       = (num_children - deleteIndex - 1) * NODE_POINTER_BYTE_SIZE;
                    BlockCopy(buffer, start + NODE_POINTER_BYTE_SIZE, buffer, start, count);
    
                    break;
                case NodeType.Node64:
                case NodeType.Node128:
                    // since the add always write to num_children position, we need to find the key pointing to it
    
                    var old_index = buffer[key_index + c];
                    buffer[key_index + c] = 0;
                    if(old_index != num_children) {
                        int i = new ReadOnlySpan<byte>(buffer, key_index, 256).IndexOf(num_children);
                        System.Diagnostics.Debug.Assert(i >= 0);
                        buffer[key_index + i] = old_index;
                        var child = ReadNodePointer(buffer, key_index + 256 + (num_children - 1) * NODE_POINTER_BYTE_SIZE);
                        WriteNodePointer(buffer, key_index + 256 + (old_index - 1) * NODE_POINTER_BYTE_SIZE, child);
                    }
                    break;
                case NodeType.Node256:
                    WriteNodePointer(buffer, key_index + c * NODE_POINTER_BYTE_SIZE, 0);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        #endregion
        #region private static DowngradeNode()
        /// <summary>
        ///     Downgrades Node 256,128,64,32,16,8 to one level under.
        /// </summary>
        private static void DowngradeNode(byte[] buffer, NodeType currentNodeType) {
            // node4 & node8                         |  node16 & node32                       |  node64 & node128                      |  node256
            // **********************************************************************************************************************************************************
            // byte                  node_type       |  byte                  node_type       |  byte                  node_type       |  byte                  node_type
            // byte                  num_children    |  byte                  num_children    |  byte                  num_children    |  byte                  num_children
            // byte                  partial_length  |  byte                  partial_length  |  byte                  partial_length  |  byte                  partial_length
            // char[MAX_PREFIX_LEN]  partial         |  char[MAX_PREFIX_LEN]  partial         |  char[MAX_PREFIX_LEN]  partial         |  char[MAX_PREFIX_LEN]  partial
            // char[n]               keys (no-order) |  char[n]               keys (ordered)  |  char[256]             keys (index+1)  |  ---
            // NodePointer[n]        children        |  NodePointer[n]        children        |  NodePointer[n]        children        |  NodePointer[n]        children
            
            var downgradedNodeType = currentNodeType - 1;
    
            if(currentNodeType >= NodeType.Node8 && currentNodeType <= NodeType.Node32) {
                var num_children = buffer[1];
                var index        = CalculateKeysIndex(currentNodeType);
    
                BlockCopy(
                    buffer, 
                    index + MaxChildCount(currentNodeType),
                    buffer, 
                    index + MaxChildCount(downgradedNodeType),
                    num_children * NODE_POINTER_BYTE_SIZE);
            } else if(currentNodeType == NodeType.Node64) {
                var num_children       = buffer[1];
                var index              = CalculateKeysIndex(NodeType.Node64);
                int lastValidIndex     = 0;
                int childrenWriteIndex = buffer.Length - num_children * NODE_POINTER_BYTE_SIZE - 1;
    
                for(int i = 0; i < 256; i++) {
                    var key = buffer[index + i];
                    if(key == 0)
                        continue;
                    var child = ReadNodePointer(buffer, index + 256 + (key - 1) * NODE_POINTER_BYTE_SIZE);
                    WriteNodePointer(buffer, childrenWriteIndex + lastValidIndex * NODE_POINTER_BYTE_SIZE, child);
                    buffer[index + lastValidIndex] = unchecked((byte)i);
                    lastValidIndex++;
                }
                System.Diagnostics.Debug.Assert(lastValidIndex == buffer[1]);
                BlockCopy(buffer, childrenWriteIndex, buffer, index + MaxChildCount(NodeType.Node32), num_children * NODE_POINTER_BYTE_SIZE);
            } else if(currentNodeType == NodeType.Node128) {
                // intentionally empty
                // additions are all at the end, and they arent read, so nothing further needs to be done
            } else if(currentNodeType == NodeType.Node256) {
                var index          = CalculateKeysIndex(NodeType.Node256);
                int keysWriteIndex = buffer.Length - 256 - 1;
                int lastValidIndex = 0;
                Array.Clear(buffer, keysWriteIndex, 256);
    
                for(int i = 0; i < 256; i++) {
                    var child = ReadNodePointer(buffer, index + i * NODE_POINTER_BYTE_SIZE);
                    if(child == 0)
                        continue;
                    buffer[keysWriteIndex + i] = unchecked((byte)(lastValidIndex + 1));
                    WriteNodePointer(buffer, index + lastValidIndex * NODE_POINTER_BYTE_SIZE, child);
                    lastValidIndex++;
                }
                System.Diagnostics.Debug.Assert(lastValidIndex == buffer[1]);
                BlockCopy(buffer, index, buffer, index + 256, lastValidIndex * NODE_POINTER_BYTE_SIZE);
                BlockCopy(buffer, keysWriteIndex, buffer, index, 256);
            } else
                throw new ArgumentException(nameof(currentNodeType));
                
            buffer[0] = (byte)downgradedNodeType;
        }
        #endregion
        #region private static MinChildCount()
        /// <summary>
        ///     Returns the thresholds at which deletes will downgrade the nodes.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static int MinChildCount(NodeType nodeType) {
            //return MaxChildCount(nodeType - 1) + 1;
            //return MaxChildCount(nodeType - 1) * 1.50 + 1;
    
            switch(nodeType) {
                case NodeType.Node4:   return 0; // 0 prevents trying to downsize
                case NodeType.Node8:   return 3;
                case NodeType.Node16:  return 7;
                case NodeType.Node32:  return 13;
                case NodeType.Node64:  return 25;
                case NodeType.Node128: return 49;
                case NodeType.Node256: return 97;
            }
    
            // dont throw as it prevents inlining
            System.Diagnostics.Debug.Fail("Invalid node type.");
    
            return 0;
        }
        #endregion
    
        #region protected TryGetPath()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    
        ///    Throws ArgumentException on empty key.
        /// </summary>
        /// <param name="throwOnEmptyKey">Default: true</param>
        protected Path TryGetPath(in TKey key, bool fetchValue, bool throwOnEmptyKey) {
            if(m_rootPointer == 0)
                return null;
    
            var current      = new NodePointer(0, m_rootPointer);
            int compareIndex = 0;
            var keyBuffer    = m_keyBuffer;
            m_keyEncoder(key, keyBuffer);
    
            if(keyBuffer.Length == 0) {
                if(throwOnEmptyKey)
                    throw new ArgumentException(nameof(key));
                else
                    return null;
            }
    
            EscapeLeafKeyTerminator(keyBuffer);
    
            var res = new Path(){
                Trail            = new List<NodeData>(8),
                Value            = default,
                EncodedSearchKey = keyBuffer,
                LastBuffer       = m_buffer,
                LastRead         = -1,
            };
    
            while(current.Target != 0) {
                this.Stream.Position = current.Target;
                var nodeType = (NodeType)this.Stream.ReadByte();
                m_buffer[0]  = (byte)nodeType;
    
                // assume fetching data is faster than multiple calls
                int readBytes = this.Stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
                res.LastRead  = readBytes;
    
                if(nodeType == NodeType.Leaf) {
                    int readIndex = 1;
                    bool bufferChanged;
    
                    var leafData = new NodeData(){
                        Type                  = NodeType.Leaf,
                        Address               = current.Target,
                        ParentPointerAddress  = current.Address,
                        ChildrenCount         = 0,
                        PartialKeyLength      = -1,
                        ValueLength           = -1,
                        PartialKeyMatchLength = -1,
                    };
                    leafData.PartialKeyMatchLength = CompareLeafKey_LongestCommonPrefix(
                        m_buffer, 
                        ref readIndex, 
                        ref readBytes, 
                        this.Stream, 
                        res.EncodedSearchKey, 
                        compareIndex, 
                        LEAF_NODE_VALUE_PREFETCH_SIZE,
                        out leafData.PartialKeyLength, 
                        out leafData.ValueLength,
                        out bufferChanged);
                    res.Trail.Add(leafData);
    
                    if(fetchValue && res.IsKeyExactMatch()) {
                        byte[] big_buffer = null;
                        // this works because we know readIndex starts at value directly
                        var temp          = ReadLeafValue(m_buffer, readIndex, readBytes, this.Stream, leafData.ValueLength, ref big_buffer, out bufferChanged);
                        res.Value         = (TValue)m_valueDecoder(temp.buffer, temp.index, temp.len);
                        //big_buffer      = null;
                    }
    
                    if(bufferChanged) {
                        res.LastBuffer = null;
                        res.LastRead   = -1;
                    }
    
                    break;
                }
                    
                var nodeData = new NodeData(){
                    Type                  = nodeType,
                    Address               = current.Target,
                    ParentPointerAddress  = current.Address,
                    ChildrenCount         = m_buffer[1],
                    PartialKeyLength      = m_buffer[2],
                    ValueLength           = -1,
                    PartialKeyMatchLength = CompareNodeKey_LongestCommonPrefix(m_buffer, res.EncodedSearchKey, ref compareIndex),
                };
                res.Trail.Add(nodeData);
    
                if(!nodeData.IsSuccess())
                    break;
    
                byte num_children       = m_buffer[1];
                var index               = CalculateKeysIndex(nodeType);
                var currentKeyCharacter = compareIndex < res.EncodedSearchKey.Length ? res.EncodedSearchKey.Content[compareIndex] : LEAF_NODE_KEY_TERMINATOR;
    
                // system.numerics.vector requires vector.add(vector.bitwiseand(vector.equals(), [1,2,3,4,...])) and finally for(aggregatevector) to get the first value that isnt zero
                // so basically, lack of cpu intrinsics makes vectors not worth it vs binarysearch
    
                if(nodeType == NodeType.Node4 || nodeType == NodeType.Node8) {
                    // unrolling this loop or using binarysearch is slower
                    bool found = false;
                    for(int i = 0; i < num_children; i++) {
                        if(m_buffer[index + i] == currentKeyCharacter) {
                            found = true;
                            var address = index + MaxChildCount(nodeType) + i * NODE_POINTER_BYTE_SIZE;
                            current = new NodePointer(
                                current.Target + address,
                                ReadNodePointer(m_buffer, address));
                            break;
                        }
                    }
                    if(!found)
                        break;
                } else if(nodeType == NodeType.Node16 || nodeType == NodeType.Node32) {
                    var i = BinarySearch(m_buffer, index, num_children, currentKeyCharacter);
                    if(i < 0)
                        break;
                    i -= index;
                    var address = index + MaxChildCount(nodeType) + i * NODE_POINTER_BYTE_SIZE;
                    current = new NodePointer(
                        current.Target + address,
                        ReadNodePointer(m_buffer, address));
                } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                    var i = m_buffer[index + currentKeyCharacter];
                    if(i == 0)
                        break;
                    i--;
                    var address = index + 256 + i * NODE_POINTER_BYTE_SIZE;
                    current = new NodePointer(
                        current.Target + address,
                        ReadNodePointer(m_buffer, address));
                } else { // nodeType == NodeType.Node256
                    var address = index + currentKeyCharacter * NODE_POINTER_BYTE_SIZE;
                    current = new NodePointer(
                        current.Target + address,
                        ReadNodePointer(m_buffer, address));
                }
            }
    
            return res;
        }
        protected sealed class Path {
            /// <summary>
            ///     Every item is guaranteed at least one character to match.
            ///     If this.Success, last entry is always a Leaf.
            /// </summary>
            public List<NodeData> Trail;
            public TValue Value;
            /// <summary>
            ///     The encoded key, which excludes LEAF_NODE_KEY_TERMINATOR.
            ///     Also this key went through EscapeLeafKeyTerminator().
            ///     This always matches the search key, and not the trail keys.
            /// </summary>
            public Buffer EncodedSearchKey;
    
            /// <summary>
            ///     If set, means it contains the last node start data.
            ///     Always set for nodes, may be missing for Leafs.
            /// </summary>
            public byte[] LastBuffer;
            /// <summary>
            ///     If set, means it contains the last node start data.
            ///     Always set for nodes, may be missing for Leafs.
            /// </summary>
            public int LastRead;
    
            public int CalculateKeyMatchLength() {
                int res   = 0;
                var count = this.Trail.Count;
                for(int i = 0; i < count; i++)
                    res += this.Trail[i].PartialKeyMatchLength;
                return res;
            }
    
            /// <summary>
            ///     True only if the last item is a leaf and leaf_key.StartsWith(search_key).
            /// </summary>
            public bool IsKeyStartingWith() {
                return this.Trail.Count > 0 &&
                    this.Trail[this.Trail.Count - 1].Type == NodeType.Leaf && 
                    this.CalculateKeyMatchLength() == this.EncodedSearchKey.Length;
            }
            /// <summary>
            ///     True only if the last item is a leaf and leaf_key == search_key.
            /// </summary>
            public bool IsKeyExactMatch() {
                if(this.Trail.Count == 0)
                    return false;
                var last = this.Trail[this.Trail.Count - 1];
                return last.Type == NodeType.Leaf && 
                    this.CalculateKeyMatchLength() == this.EncodedSearchKey.Length &&
                    last.IsSuccess();
            }
    
            public TKey GetKey(AdaptiveRadixTree<TKey, TValue> owner) {
                this.ReadPathEntireKey(owner, this.EncodedSearchKey);
                UnescapeLeafKeyTerminator(this.EncodedSearchKey.Content, 0, ref this.EncodedSearchKey.Length);
                return (TKey)owner.m_keyDecoder(this.EncodedSearchKey.Content, 0, this.EncodedSearchKey.Length);
            }
            public TValue GetValue() {
                return this.Value;
            }
            public KeyValuePair<TKey, TValue> GetItem(AdaptiveRadixTree<TKey, TValue> owner) {
                return new KeyValuePair<TKey, TValue>(this.GetKey(owner), this.GetValue());
            }
    
            /// <summary>
            ///     Combines [EncodedSearchKey] + [Items.Last().PartialKeyLength after PartialKeyMatchLength]
            /// </summary>
            public void ReadPathEntireKey(AdaptiveRadixTree<TKey, TValue> owner, Buffer pathKey) {
                if(this.Trail.Count == 0) {
                    pathKey.Length = 0;
                    return;
                }
    
                var last        = this.Trail[this.Trail.Count - 1];
                int totalLength = 0;
                var count       = this.Trail.Count;
                for(int i = 0; i < count; i++)
                    totalLength += this.Trail[i].PartialKeyLength;
                if(last.Type == NodeType.Leaf && totalLength > 0)
                    totalLength--; // remove terminator
                    
                pathKey.EnsureCapacity(totalLength);
                pathKey.Length = totalLength;
    
                var keyMatchLength = this.CalculateKeyMatchLength();
    
                if(pathKey != this.EncodedSearchKey)
                    BlockCopy(this.EncodedSearchKey.Content, 0, pathKey.Content, 0, keyMatchLength);
    
                // if this is true, this method probably shouldnt have been called in the first place
                if(keyMatchLength == totalLength)
                    return;
    
                //last.IsSuccess() == false
                    
                var remainingPartialKeyLength = totalLength - keyMatchLength;
    
                if(last.Type != NodeType.Leaf) {
                    var skippable = 3 + last.PartialKeyMatchLength;
                    BlockCopy(this.LastBuffer, skippable, pathKey.Content, keyMatchLength, remainingPartialKeyLength);
                } else {
                    byte[] buffer;
                    int start;
                    int read;
    
                    if(this.LastBuffer != null) {
                        buffer = this.LastBuffer;
                        read   = this.LastRead;
                        start  = 1;
                    } else {
                        buffer = owner.m_buffer;
                        read   = 1;
                        start  = 1;
                    }
                        
                    owner.Stream.Position = last.Address + read;
                    start                += CalculateVarUInt64Length(unchecked((ulong)last.PartialKeyLength)) + CalculateVarUInt64Length(unchecked((ulong)last.ValueLength));
                    ReadLeafKey(buffer, ref start, ref read, owner.Stream, last.PartialKeyLength, ref pathKey.Content, ref keyMatchLength, 0);
    
                    //System.Diagnostics.Debug.Assert(keyMatchLength - 1 == totalLength);
                }
            }
    
            /// <summary>
            ///     Returns the index within the trail where the key becomes unique.
            ///     Returns -1 if root points directly to item.
            /// </summary>
            public int CalculateTrailUniquenessStart() {
                // this code assumes you have a full match (ie: IsKeyExactMatch() == true)
                // also, note that normally you expect only the last trail item to be what makes the trail unique, 
                // but in practice, we avoid doing as much node update as possible during add/removes which may result in nodes containing just 1 item
                // also nodes with just 1 item are normal if the [shared length > MAX_PREFIX_LEN]
                // as a consequence, we do have to walk up the tree rather than assume only last item applies
    
                int uniqueTrailStart = -1;
                for(int i = this.Trail.Count - 1; i >= 0; i--) {
                    var current = this.Trail[i];
    
                    // note: only root entry can have partialkeylength==0
                    if((current.Type != NodeType.Leaf && current.ChildrenCount > 1) || current.PartialKeyLength == 0)
                        break;
    
                    uniqueTrailStart = i;
                }
                return uniqueTrailStart;
            }
        }
        protected class NodeData {
            public NodeType Type;
            public byte ChildrenCount;
            /// <summary>
            ///     Includes LEAF_NODE_KEY_TERMINATOR.
            /// </summary>
            public int PartialKeyLength;
            public long Address;
            /// <summary>
            ///     The address of the pointer containing Address.
            /// </summary>
            public long ParentPointerAddress;
            public int ValueLength;
            /// <summary>
            ///     Excludes LEAF_NODE_KEY_TERMINATOR.
            /// </summary>
            public int PartialKeyMatchLength;
    
            /// <summary>
            ///     If true, means full match (partial key match == key length)
            /// </summary>
            public bool IsSuccess() {
                return this.Type != NodeType.Leaf ?
                    this.PartialKeyMatchLength == this.PartialKeyLength :
                    this.PartialKeyMatchLength == this.PartialKeyLength - 1;
            }
    
            public override string ToString() {
                return $"[@{this.Address}] {this.Type.ToString()}";
            }
        }
        #endregion
        #region protected TryGetLeaf()
        /// <summary>
        ///    O(k)    (k = # of characters)
        ///    
        ///    Throws ArgumentException on empty key.
        /// </summary>
        protected bool TryGetLeaf(in TKey key, bool fetchValue, out TValue value) {
            value = default;
    
            var current = m_rootPointer;
            if(current == 0)
                return false;
    
            int compareIndex = 0;
            var keyBuffer    = m_keyBuffer;
            m_keyEncoder(key, keyBuffer);
    
            if(keyBuffer.Length == 0)
                throw new ArgumentException(nameof(key));
    
            EscapeLeafKeyTerminator(keyBuffer);
    
            while(current != 0) {
                this.Stream.Position = current;
                var nodeType = (NodeType)this.Stream.ReadByte();
                m_buffer[0]  = (byte)nodeType;
    
                // assume fetching data is faster than multiple calls
                int readBytes = this.Stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                if(nodeType == NodeType.Leaf) {
                    int readIndex = 1;
                    var success = CompareLeafKey(m_buffer, ref readIndex, ref readBytes, this.Stream, keyBuffer, compareIndex, fetchValue ? LEAF_NODE_VALUE_PREFETCH_SIZE : 0, out long value_length);
                    if(success && fetchValue) {
                        byte[] big_buffer = null;
                        var temp          = ReadLeafValue(m_buffer, readIndex, readBytes, this.Stream, value_length, ref big_buffer, out _);
                        value             = (TValue)m_valueDecoder(temp.buffer, temp.index, temp.len);
                        //big_buffer        = null;
                    }
                    return success;
                }
    
                if(!CompareNodeKey(m_buffer, keyBuffer, ref compareIndex))
                    return false;
    
                byte num_children       = m_buffer[1];
                var index               = CalculateKeysIndex(nodeType);
                var currentKeyCharacter = compareIndex < keyBuffer.Length ? keyBuffer.Content[compareIndex] : LEAF_NODE_KEY_TERMINATOR;
    
                // system.numerics.vector requires vector.add(vector.bitwiseand(vector.equals(), [1,2,3,4,...])) and finally for(aggregatevector) to get the first value that isnt zero
                // so basically, lack of cpu intrinsics makes vectors not worth it vs binarysearch
    
                if(nodeType == NodeType.Node4 || nodeType == NodeType.Node8) {
                    // unrolling this loop or using binarysearch is slower
                    bool found = false;
                    for(int i = 0; i < num_children; i++) {
                        if(m_buffer[index + i] == currentKeyCharacter) {
                            found   = true;
                            current = ReadNodePointer(m_buffer, index + MaxChildCount(nodeType) + i * NODE_POINTER_BYTE_SIZE);
                            break;
                        }
                    }
                    if(!found)
                        return false;
                } else if(nodeType == NodeType.Node16 || nodeType == NodeType.Node32) {
                    var i = BinarySearch(m_buffer, index, num_children, currentKeyCharacter);
                    if(i < 0)
                        return false;
                    i -= index;
                    current = ReadNodePointer(m_buffer, index + MaxChildCount(nodeType) + i * NODE_POINTER_BYTE_SIZE);
                } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                    var i = m_buffer[index + currentKeyCharacter];
                    if(i == 0)
                        return false;
                    i--;
                    current = ReadNodePointer(m_buffer, index + 256 + i * NODE_POINTER_BYTE_SIZE);
                } else { // nodeType == NodeType.Node256
                    current = ReadNodePointer(m_buffer, index + currentKeyCharacter * NODE_POINTER_BYTE_SIZE);
                }
            }
    
            return false;
        }
        #endregion
    
        #region private CreateLeafNode()
        /// <summary>
        ///     Returns the address.
        /// </summary>
        /// <param name="remainingEncodedKey">Excludes LEAF_NODE_KEY_TERMINATOR</param>
        [MethodImpl(AggressiveInlining)]
        private long CreateLeafNode(in ReadOnlySpan<byte> remainingEncodedKey, Buffer encodedValue, Func<long, long> custom_alloc = null) {
            // leaf
            // **************************************
            // byte                  node_type
            // var long              partial_length (1-9 bytes)
            // var long              value_length (1-9 bytes)
            // char[partial_length]  partial
            // byte[value_length]    value
    
    
            // +1 for LEAF_NODE_KEY_TERMINATOR
            var varlen_key_size   = CalculateVarUInt64Length(unchecked((ulong)remainingEncodedKey.Length + 1));
            var varlen_value_size = CalculateVarUInt64Length(unchecked((ulong)encodedValue.Length));
            var alloc_size        = 1 + varlen_key_size + varlen_value_size + remainingEncodedKey.Length + 1 + encodedValue.Length;
            var address           = custom_alloc == null ? this.Alloc(alloc_size) : custom_alloc(alloc_size);
    
            this.Stream.Position  = address;
    
            m_buffer[0] = (byte)NodeType.Leaf;
    
            int writeIndex = 1;
            WriteVarUInt64(m_buffer, ref writeIndex, unchecked((ulong)remainingEncodedKey.Length + 1));
            WriteVarUInt64(m_buffer, ref writeIndex, unchecked((ulong)encodedValue.Length));
    
            WriteSpan(m_buffer, ref writeIndex, this.Stream, in remainingEncodedKey);
    
            m_buffer[writeIndex++] = LEAF_NODE_KEY_TERMINATOR;
            if(writeIndex == m_buffer.Length) {
                this.Stream.Write(m_buffer, 0, writeIndex);
                writeIndex = 0;
            }
    
            var span = new ReadOnlySpan<byte>(encodedValue.Content, 0, encodedValue.Length);
            WriteSpan(m_buffer, ref writeIndex, this.Stream, in span);
    
            if(writeIndex != 0)
                this.Stream.Write(m_buffer, 0, writeIndex);
    
            return address;
        }
        #endregion
        #region private static CreateEmptyNode4()
        /// <summary>
        ///     Creates a new node4 with zero children.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static void CreateEmptyNode4(byte[] buffer, int index, in ReadOnlySpan<byte> partialKey) {
            System.Diagnostics.Debug.Assert(partialKey.Length <= MAX_PREFIX_LEN);
    
            buffer[index + 0] = (byte)NodeType.Node4;               // node_type
            buffer[index + 1] = 0;                                  // num_children
            buffer[index + 2] = unchecked((byte)partialKey.Length); // partial_length
    
            // partial
            partialKey.CopyTo(new Span<byte>(buffer, index + 3, partialKey.Length));
        }
        #endregion
        #region private static CompareNodeKey()
        [MethodImpl(AggressiveInlining)]
        private static bool CompareNodeKey(byte[] buffer, Buffer encodedKey, ref int compareIndex) {
            var partial_length = buffer[2];
    
            // the only place this would make sense would be on the root node
            if(partial_length == 0)
                return true;
    
            System.Diagnostics.Debug.Assert(partial_length <= MAX_PREFIX_LEN);
    
            // potentially allow "partial_length > encodedKey.Length - compareIndex" if leafs can have a zero length partial key
            // the == part of >= is because leafs must contain at least one character (LEAF_NODE_KEY_TERMINATOR)
            if(partial_length >= encodedKey.Length - compareIndex)
                return false;
    
            var res = new ReadOnlySpan<byte>(encodedKey.Content, compareIndex, partial_length)
                .SequenceEqual(new ReadOnlySpan<byte>(buffer, 3, partial_length));
                
            if(res)
                compareIndex += partial_length;
    
            return res;
        }
        #endregion
        #region private static CompareNodeKey_LongestCommonPrefix()
        [MethodImpl(AggressiveInlining)]
        private static byte CompareNodeKey_LongestCommonPrefix(byte[] buffer, Buffer encodedKey, ref int compareIndex) {
            var partial_length = buffer[2];
    
            System.Diagnostics.Debug.Assert(partial_length <= MAX_PREFIX_LEN);
    
            byte count = 0;
            byte len   = unchecked((byte)Math.Min(partial_length, encodedKey.Length - compareIndex));
    
            for(byte i = 0; i < len; i++) {
                if(encodedKey.Content[compareIndex + count] == buffer[3 + i])
                    count++;
                else
                    break;
            }
    
            compareIndex += count;
            return count;
        }
        #endregion
        #region private static CompareLeafKey()
        [MethodImpl(AggressiveInlining)]
        private static bool CompareLeafKey(byte[] buffer, ref int bufferIndex, ref int bufferRead, Stream stream, Buffer encodedKey, int compareIndex, int value_prefetch, out long value_length) {
            var partial_length = ReadVarInt64(buffer, ref bufferIndex);
            value_length       = ReadVarInt64(buffer, ref bufferIndex);
            if(partial_length != encodedKey.Length + 1 - compareIndex)
                return false;
    
            // note: 
            // buffer + partial_length     LEAF_NODE_KEY_TERMINATOR included
            // encodedKey + compareIndex   LEAF_NODE_KEY_TERMINATOR *not* included
    
            while(partial_length > 0) {
                if(bufferIndex == bufferRead) {
                    bufferIndex = 0;
                    bufferRead  = stream.Read(buffer, 0, unchecked((int)Math.Min(partial_length + value_prefetch, buffer.Length)));
                }
    
                var processed = unchecked((int)Math.Min(partial_length, bufferRead - bufferIndex));
    
                if(compareIndex + processed == encodedKey.Length + 1) {
                    // avoid processing LEAF_NODE_KEY_TERMINATOR
                    if(processed > 1 && !new ReadOnlySpan<byte>(encodedKey.Content, compareIndex, processed - 1).SequenceEqual(new ReadOnlySpan<byte>(buffer, bufferIndex, processed - 1)))
                        return false;
                    // then do
                    if(buffer[bufferIndex + processed - 1] != LEAF_NODE_KEY_TERMINATOR)
                        return false;
                } else {
                    if(!new ReadOnlySpan<byte>(encodedKey.Content, compareIndex, processed).SequenceEqual(new ReadOnlySpan<byte>(buffer, bufferIndex, processed)))
                        return false;
                }
    
                partial_length -= processed;
                compareIndex   += processed;
                bufferIndex    += processed;
            }
    
            return true;
        }
        #endregion
        #region private static CompareLeafKey_LongestCommonPrefix()
        [MethodImpl(AggressiveInlining)]
        private static int CompareLeafKey_LongestCommonPrefix(byte[] buffer, ref int bufferIndex, ref int bufferRead, Stream stream, Buffer encodedKey, int compareIndex, int value_prefetch, out int partial_length, out int value_length, out bool bufferChanged) {
            bufferChanged  = false;
            partial_length = unchecked((int)ReadVarInt64(buffer, ref bufferIndex));
            value_length   = unchecked((int)ReadVarInt64(buffer, ref bufferIndex));
    
            // note: 
            // buffer + partial_length     LEAF_NODE_KEY_TERMINATOR included
            // encodedKey + compareIndex   LEAF_NODE_KEY_TERMINATOR *not* included
    
            var remaining         = partial_length; // also indicates how further away it is from bufferIndex
            var compareIndexStart = compareIndex;
            var len               = Math.Min(remaining - 1, encodedKey.Length - compareIndex);
    
            for(int i = 0; i < len; i++) {
                if(bufferIndex == bufferRead) {
                    bufferIndex   = 0;
                    bufferRead    = stream.Read(buffer, 0, Math.Min(remaining + value_prefetch, buffer.Length));
                    bufferChanged = true;
                }
    
                if(buffer[bufferIndex] == encodedKey.Content[compareIndex]) {
                    compareIndex++;
                    bufferIndex++;
                    remaining--;
                } else
                    break;
            }
    
            // if fully compared, then check LEAF_NODE_KEY_TERMINATOR
            bool terminator_match = false;
            if(remaining == 0 && compareIndex == encodedKey.Length) {
                if(bufferIndex == bufferRead) {
                    bufferIndex   = 0;
                    bufferRead    = stream.Read(buffer, 0, 1 + MAX_VARINT64_ENCODED_SIZE);
                    bufferChanged = true;
                }
                if(buffer[bufferIndex] == LEAF_NODE_KEY_TERMINATOR)
                    terminator_match = true;
            } else {
                // partial match; must re-adjust buffer/stream to skip directly to value
                var move = Math.Min(remaining, bufferRead - bufferIndex);
    
                remaining   -= move;
                bufferIndex += move;
    
                // if we need to skip somewhere past our buffer
                if(remaining > 0) {
                    bufferIndex = 0;
                    bufferRead  = 0;
                    stream.Seek(remaining, SeekOrigin.Current);
                    //bufferChanged = true; // unsure if relevant
                }
            }
    
            return compareIndex - compareIndexStart + (terminator_match ? 1 : 0);
        }
        #endregion
        #region private static ReadLeafKey()
        [MethodImpl(AggressiveInlining)]
        private static void ReadLeafKey(byte[] buffer, ref int bufferIndex, ref int bufferRead, Stream stream, int partial_length, ref byte[] key, ref int keySize, int value_prefetch) {
            // dont read terminal byte
    
            partial_length--; // remove LEAF_NODE_KEY_TERMINATOR
    
            if(key.Length - keySize < partial_length)
                Array.Resize(ref key, unchecked((int)(keySize + partial_length)));
    
            // note: 
            // buffer + partial_length   LEAF_NODE_KEY_TERMINATOR included
            // key                       LEAF_NODE_KEY_TERMINATOR *not* included
    
            while(partial_length > 0) {
                if(bufferIndex == bufferRead) {
                    bufferIndex = 0;
                    bufferRead  = stream.Read(buffer, 0, unchecked((int)Math.Min(partial_length + value_prefetch, buffer.Length)));
                }
    
                var processed = unchecked((int)Math.Min(partial_length, bufferRead - bufferIndex));
    
                BlockCopy(buffer, bufferIndex, key, keySize, processed);
    
                // note: could read the LEAF_NODE_KEY_TERMINATOR to make sure its a properly formed leaf
    
                partial_length -= processed;
                keySize        += processed;
                bufferIndex    += processed;
            }
    
            // skip terminal byte
            bufferIndex++;
        }
        #endregion
        #region private static ReadLeafValue()
        [MethodImpl(AggressiveInlining)]
        private static (byte[] buffer, int index, int len) ReadLeafValue(byte[] buffer, int bufferIndex, int bufferRead, Stream stream, long value_length, ref byte[] alternativeBuffer, out bool bufferChanged) {
            bufferChanged = false;
    
            if(value_length <= 0)
                return (null, 0, 0);
    
            // if value is already entirely read in buffer
            if(value_length <= bufferRead - bufferIndex)
                return (buffer, bufferIndex, unchecked((int)value_length));
            else if(value_length <= buffer.Length) {
                // if value isn't fully read, but it would fit in the buffer
    
                // downshift data
                var size = bufferRead - bufferIndex;
                if(bufferIndex > 0 && size > 0)
                    BlockCopy(buffer, bufferIndex, buffer, 0, size);
    
                bufferChanged = true;
                var temp      = stream.Read(buffer, size, unchecked((int)(value_length - size)));
                if(temp + size < value_length)
                    throw new ApplicationException("Unable to read value from node/leaf due to read() operation not returning the expected number of bytes.");
                return (buffer, 0, unchecked((int)value_length));
            } else {
                // if encoded value > buffer.Length
    
                // store in alternative buffer, and resize if required
                if(alternativeBuffer == null || alternativeBuffer.Length < value_length)
                    alternativeBuffer = new byte[value_length];
    
                //bufferChanged = false;
    
                // downshift data
                var size = bufferRead - bufferIndex;
                if(size > 0)
                    BlockCopy(buffer, bufferIndex, alternativeBuffer, 0, size);
    
                var temp = stream.Read(alternativeBuffer, size, unchecked((int)(value_length - size)));
                if(temp + size < value_length)
                    throw new ApplicationException("Unable to read value from node/leaf due to read() operation not returning the expected number of bytes.");
                return (alternativeBuffer, 0, unchecked((int)value_length));
            }
        }
        #endregion
    
        #region private static ReadNodePointer()
        [MethodImpl(AggressiveInlining)]
        private static long ReadNodePointer(byte[] buffer, int index) {
            long res = 0;
    
#pragma warning disable CS0162 // Unreachable code detected
            if(NODE_POINTER_BYTE_SIZE >= 1)
                res |= buffer[index + 0];
            if(NODE_POINTER_BYTE_SIZE >= 2)
                res |= (long)buffer[index + 1] << 8;
            if(NODE_POINTER_BYTE_SIZE >= 3)
                res |= (long)buffer[index + 2] << 16;
            if(NODE_POINTER_BYTE_SIZE >= 4)
                res |= (long)buffer[index + 3] << 24;
            if(NODE_POINTER_BYTE_SIZE >= 5)
                res |= (long)buffer[index + 4] << 32;
            if(NODE_POINTER_BYTE_SIZE >= 6)
                res |= (long)buffer[index + 5] << 40;
            if(NODE_POINTER_BYTE_SIZE >= 7)
                res |= (long)buffer[index + 6] << 48;
            if(NODE_POINTER_BYTE_SIZE >= 8)
                res |= (long)buffer[index + 7] << 56;
#pragma warning restore CS0162 // Unreachable code detected
    
            return res;
        }
        #endregion
        #region private static WriteNodePointer()
        [MethodImpl(AggressiveInlining)]
        private static void WriteNodePointer(byte[] buffer, int index, long address) {
#pragma warning disable CS0162 // Unreachable code detected
            if(NODE_POINTER_BYTE_SIZE >= 1)
                buffer[index + 0] = unchecked((byte)((address >> 0) & 0xFF));
            if(NODE_POINTER_BYTE_SIZE >= 2)
                buffer[index + 1] = unchecked((byte)((address >> 8) & 0xFF));
            if(NODE_POINTER_BYTE_SIZE >= 3)
                buffer[index + 2] = unchecked((byte)((address >> 16) & 0xFF));
            if(NODE_POINTER_BYTE_SIZE >= 4)
                buffer[index + 3] = unchecked((byte)((address >> 24) & 0xFF));
            if(NODE_POINTER_BYTE_SIZE >= 5)
                buffer[index + 4] = unchecked((byte)((address >> 32) & 0xFF));
            if(NODE_POINTER_BYTE_SIZE >= 6)
                buffer[index + 5] = unchecked((byte)((address >> 40) & 0xFF));
            if(NODE_POINTER_BYTE_SIZE >= 7)
                buffer[index + 6] = unchecked((byte)((address >> 48) & 0xFF));
            if(NODE_POINTER_BYTE_SIZE >= 8)
                buffer[index + 7] = unchecked((byte)((address >> 56) & 0xFF));
#pragma warning restore CS0162 // Unreachable code detected
    
            //index += NODE_POINTER_BYTE_SIZE;
        }
        #endregion
    
        #region private static CalculateNodeSize()
        /// <summary>
        ///     Returns -1 on leaf.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static int CalculateNodeSize(NodeType nodeType) {
            switch(nodeType) {
                case NodeType.Node4:   return 3 + MAX_PREFIX_LEN + 4   + (4 * NODE_POINTER_BYTE_SIZE);
                case NodeType.Node8:   return 3 + MAX_PREFIX_LEN + 8   + (8 * NODE_POINTER_BYTE_SIZE);
                case NodeType.Node16:  return 3 + MAX_PREFIX_LEN + 16  + (16 * NODE_POINTER_BYTE_SIZE);
                case NodeType.Node32:  return 3 + MAX_PREFIX_LEN + 32  + (32 * NODE_POINTER_BYTE_SIZE);
    
                case NodeType.Node64:  return 3 + MAX_PREFIX_LEN + 256 + (64 * NODE_POINTER_BYTE_SIZE);
                case NodeType.Node128: return 3 + MAX_PREFIX_LEN + 256 + (128 * NODE_POINTER_BYTE_SIZE);
    
                case NodeType.Node256: return 3 + MAX_PREFIX_LEN + (256 * NODE_POINTER_BYTE_SIZE);
    
                case NodeType.Leaf:    return -1;
    
                default:
                    throw new NotImplementedException();
            }
        }
        #endregion
        #region private static CalculateLeafNodeSize()
        /// <param name="partialKeyLength">Includes LEAF_NODE_KEY_TERMINATOR</param>
        [MethodImpl(AggressiveInlining)]
        private static int CalculateLeafNodeSize(int partialKeyLength, int valueLength) {
            return 
                1 +
                CalculateVarUInt64Length(unchecked((ulong)partialKeyLength)) + 
                CalculateVarUInt64Length(unchecked((ulong)valueLength)) + 
                partialKeyLength + 
                valueLength;
        }
        #endregion
        #region private static CalculateNodePrefetchSize()
        [MethodImpl(AggressiveInlining)]
        private static int CalculateNodePrefetchSize(NodeType nodeType) {
            if(nodeType != NodeType.Leaf)
                return CalculateNodeSize(nodeType);
    
            return LEAF_NODE_PREFETCH_SIZE;
        }
        #endregion
        #region private static CalculateKeysIndex()
        [MethodImpl(AggressiveInlining)]
        private static int CalculateKeysIndex(NodeType nodeType) {
            // this intentionally returns the keys location for Node256 even though there arent any
            if(nodeType != NodeType.Leaf)
                return 3 + MAX_PREFIX_LEN;
                
            return -1;
        }
        #endregion
        #region private static MaxChildCount()
        [MethodImpl(AggressiveInlining)]
        private static int MaxChildCount(NodeType nodeType) {
            switch(nodeType) {
                case NodeType.Node4:   return 4;
                case NodeType.Node8:   return 8;
                case NodeType.Node16:  return 16;
                case NodeType.Node32:  return 32;
                case NodeType.Node64:  return 64;
                case NodeType.Node128: return 128;
                case NodeType.Node256: return 256;
#if DEBUG
                // only include this in debug as throwing prevents inlining
                default:
                    throw new NotImplementedException();
#endif
            }
#if !DEBUG
            return 0;
#endif
        }
        #endregion
        #region private static BinarySearch()
        [MethodImpl(AggressiveInlining)]
        private static int BinarySearch(byte[] array, int min, int length, byte value) {
            int max = min + length - 1;
                
            while(min <= max) {
                int median = (min + max) >> 1;
                var diff   = array[median] - value;
                        
                if(diff < 0)
                    min = median + 1;
                else if(diff > 0)
                    max = median - 1;
                else
                    return median;
            }
                    
            return ~min;
        }
        #endregion
    
        #region private static ReadVarInt64()
        /// <summary>
        ///     Read variable length LE-encoded (little endian) int64.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static long ReadVarInt64(byte[] buffer, ref int index) {
            long res;
            byte c = buffer[index++];
    
            if((c >> 7) == 0) {
                return c;
            } else if((c >> 6) == 0) {
                res = ((long)c & 0x3F) |
                    ((long)buffer[index++] << 6);
            } else if((c >> 5) == 0) {
                res = ((long)c & 0x1F) |
                    ((long)buffer[index + 0] << 5) |
                    ((long)buffer[index + 1] << 13);
                index += 2;
            } else if((c >> 4) == 0) {
                res = ((long)c & 0x0F) |
                    ((long)buffer[index + 0] << 4) |
                    ((long)buffer[index + 1] << 12) |
                    ((long)buffer[index + 2] << 20);
                index += 3;
            } else if((c >> 3) == 0) {
                res = ((long)c & 0x07) |
                    ((long)buffer[index + 0] << 3) |
                    ((long)buffer[index + 1] << 11) |
                    ((long)buffer[index + 2] << 19) |
                    ((long)buffer[index + 3] << 27);
                index += 4;
            } else if((c >> 2) == 0) {
                res = ((long)c & 0x03) |
                    ((long)buffer[index + 0] << 2) |
                    ((long)buffer[index + 1] << 10) |
                    ((long)buffer[index + 2] << 18) |
                    ((long)buffer[index + 3] << 26) |
                    ((long)buffer[index + 4] << 34);
                index += 5;
            } else if((c >> 1) == 0) {
                res = ((long)c & 0x01) |
                    ((long)buffer[index + 0] << 1) |
                    ((long)buffer[index + 1] << 9) |
                    ((long)buffer[index + 2] << 17) |
                    ((long)buffer[index + 3] << 25) |
                    ((long)buffer[index + 4] << 33) |
                    ((long)buffer[index + 5] << 41);
                index += 6;
            } else if((c & 1) == 0) {
                res = //((long)c & 0x00) |
                    ((long)buffer[index + 0] << 0) |
                    ((long)buffer[index + 1] << 8) |
                    ((long)buffer[index + 2] << 16) |
                    ((long)buffer[index + 3] << 24) |
                    ((long)buffer[index + 4] << 32) |
                    ((long)buffer[index + 5] << 40) |
                    ((long)buffer[index + 6] << 48);
                index += 7;
            } else {
                res = //((long)c & 0x00) |
                    ((long)buffer[index + 0] << 0) |
                    ((long)buffer[index + 1] << 8) |
                    ((long)buffer[index + 2] << 16) |
                    ((long)buffer[index + 3] << 24) |
                    ((long)buffer[index + 4] << 32) |
                    ((long)buffer[index + 5] << 40) |
                    ((long)buffer[index + 6] << 48) |
                    ((long)buffer[index + 7] << 56);
                index += 8;
            }
    
            return res;
        }
        /// <summary>
        ///     Read variable length LE-encoded (little endian) int64.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static long ReadVarInt64(byte[] buffer, ref int index, ref int read, Stream stream) {
            if(index == read) {
                read = stream.Read(buffer, 0, buffer.Length);
                index = 0;
            }
    
            byte c = buffer[index++];
    
            if((c >> 7) == 0)
                return c;
                
            long res;
            int remaining = buffer.Length - index;
    
            if((c >> 6) == 0) {
                if(remaining == 0) {
                    read = stream.Read(buffer, 0, buffer.Length);
                    index = 0;
                }
                res = ((long)c & 0x3F) |
                    ((long)buffer[index++] << 6);
            } else if((c >> 5) == 0) {
                if(remaining <= 1) {
                    if(remaining >= 1) buffer[0] = buffer[index++];
                    read = stream.Read(buffer, remaining, buffer.Length - remaining) + remaining;
                    index = 0;
                }
                res = ((long)c & 0x1F) |
                    ((long)buffer[index + 0] << 5) |
                    ((long)buffer[index + 1] << 13);
                index += 2;
            } else if((c >> 4) == 0) {
                if(remaining <= 2) {
                    if(remaining >= 1) buffer[0] = buffer[index++];
                    if(remaining >= 2) buffer[1] = buffer[index++];
                    read = stream.Read(buffer, remaining, buffer.Length - remaining) + remaining;
                    index = 0;
                }
                res = ((long)c & 0x0F) |
                    ((long)buffer[index + 0] << 4) |
                    ((long)buffer[index + 1] << 12) |
                    ((long)buffer[index + 2] << 20);
                index += 3;
            } else if((c >> 3) == 0) {
                if(remaining <= 3) {
                    if(remaining >= 1) buffer[0] = buffer[index++];
                    if(remaining >= 2) buffer[1] = buffer[index++];
                    if(remaining >= 3) buffer[2] = buffer[index++];
                    read = stream.Read(buffer, remaining, buffer.Length - remaining) + remaining;
                    index = 0;
                }
                res = ((long)c & 0x07) |
                    ((long)buffer[index + 0] << 3) |
                    ((long)buffer[index + 1] << 11) |
                    ((long)buffer[index + 2] << 19) |
                    ((long)buffer[index + 3] << 27);
                index += 4;
            } else if((c >> 2) == 0) {
                if(remaining <= 4) {
                    if(remaining >= 1) buffer[0] = buffer[index++];
                    if(remaining >= 2) buffer[1] = buffer[index++];
                    if(remaining >= 3) buffer[2] = buffer[index++];
                    if(remaining >= 4) buffer[3] = buffer[index++];
                    read = stream.Read(buffer, remaining, buffer.Length - remaining) + remaining;
                    index = 0;
                }
                res = ((long)c & 0x03) |
                    ((long)buffer[index + 0] << 2) |
                    ((long)buffer[index + 1] << 10) |
                    ((long)buffer[index + 2] << 18) |
                    ((long)buffer[index + 3] << 26) |
                    ((long)buffer[index + 4] << 34);
                index += 5;
            } else if((c >> 1) == 0) {
                if(remaining <= 5) {
                    if(remaining >= 1) buffer[0] = buffer[index++];
                    if(remaining >= 2) buffer[1] = buffer[index++];
                    if(remaining >= 3) buffer[2] = buffer[index++];
                    if(remaining >= 4) buffer[3] = buffer[index++];
                    if(remaining >= 5) buffer[4] = buffer[index++];
                    read = stream.Read(buffer, remaining, buffer.Length - remaining) + remaining;
                    index = 0;
                }
                res = ((long)c & 0x01) |
                    ((long)buffer[index + 0] << 1) |
                    ((long)buffer[index + 1] << 9) |
                    ((long)buffer[index + 2] << 17) |
                    ((long)buffer[index + 3] << 25) |
                    ((long)buffer[index + 4] << 33) |
                    ((long)buffer[index + 5] << 41);
                index += 6;
            } else if((c & 1) == 0) {
                if(remaining <= 6) {
                    if(remaining >= 1) buffer[0] = buffer[index++];
                    if(remaining >= 2) buffer[1] = buffer[index++];
                    if(remaining >= 3) buffer[2] = buffer[index++];
                    if(remaining >= 4) buffer[3] = buffer[index++];
                    if(remaining >= 5) buffer[4] = buffer[index++];
                    if(remaining >= 6) buffer[5] = buffer[index++];
                    read = stream.Read(buffer, remaining, buffer.Length - remaining) + remaining;
                    index = 0;
                }
                res = //((long)c & 0x00) |
                    ((long)buffer[index + 0] << 0) |
                    ((long)buffer[index + 1] << 8) |
                    ((long)buffer[index + 2] << 16) |
                    ((long)buffer[index + 3] << 24) |
                    ((long)buffer[index + 4] << 32) |
                    ((long)buffer[index + 5] << 40) |
                    ((long)buffer[index + 6] << 48);
                index += 7;
            } else {
                if(remaining <= 7) {
                    if(remaining >= 1) buffer[0] = buffer[index++];
                    if(remaining >= 2) buffer[1] = buffer[index++];
                    if(remaining >= 3) buffer[2] = buffer[index++];
                    if(remaining >= 4) buffer[3] = buffer[index++];
                    if(remaining >= 5) buffer[4] = buffer[index++];
                    if(remaining >= 6) buffer[5] = buffer[index++];
                    if(remaining >= 7) buffer[6] = buffer[index++];
                    read = stream.Read(buffer, remaining, buffer.Length - remaining) + remaining;
                    index = 0;
                }
                res = //((long)c & 0x00) |
                    ((long)buffer[index + 0] << 0) |
                    ((long)buffer[index + 1] << 8) |
                    ((long)buffer[index + 2] << 16) |
                    ((long)buffer[index + 3] << 24) |
                    ((long)buffer[index + 4] << 32) |
                    ((long)buffer[index + 5] << 40) |
                    ((long)buffer[index + 6] << 48) |
                    ((long)buffer[index + 7] << 56);
                index += 8;
            }
    
            return res;
        }
        #endregion
        #region private static WriteVarUInt64()
        /// <summary>
        ///     Write variable length LE-encoded (little endian) uint64.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static void WriteVarUInt64(byte[] buffer, ref int index, ulong value) {
            // 20% speedup for using "ref int index" instead of returning the new index
    
            //    value                   first byte  bits
            //                            (encoded_bytes)
            // <= 0x0000_0000_0000_007F   0xxx xxxx   7
            // <= 0x0000_0000_0000_3FFF   10xx xxxx   14
            // <= 0x0000_0000_001F_FFFF   110x xxxx   21
            // <= 0x0000_0000_0FFF_FFFF   1110 xxxx   28
            // <= 0x0000_0007_FFFF_FFFF   1111 0xxx   35
            // <= 0x0000_03FF_FFFF_FFFF   1111 10xx   42
            // <= 0x0001_FFFF_FFFF_FFFF   1111 110x   49
            // <= 0x00FF_FFFF_FFFF_FFFF   1111 1110   56
            // <= 0xFFFF_FFFF_FFFF_FFFF   1111 1111   64
    
            if(value <= 0x0000_0000_0000_007Ful) {
                buffer[index++] = unchecked((byte)value);
            } else if(value <= 0x0000_0000_0000_3FFFul) {
                buffer[index + 0] = unchecked((byte)(0x80 | (int)(value & 0x3F)));
                buffer[index + 1] = unchecked((byte)((value >> 6) & 0xFF));
                index += 2;
            } else if(value <= 0x0000_0000_001F_FFFFul) {
                buffer[index + 0] = unchecked((byte)(0xC0 | (int)(value & 0x1F)));
                buffer[index + 1] = unchecked((byte)((value >> 5) & 0xFF));
                buffer[index + 2] = unchecked((byte)((value >> 13) & 0xFF));
                index += 3;
            } else if(value <= 0x0000_0000_0FFF_FFFFul) {
                buffer[index + 0] = unchecked((byte)(0xE0 | (int)(value & 0x0F)));
                buffer[index + 1] = unchecked((byte)((value >> 4) & 0xFF));
                buffer[index + 2] = unchecked((byte)((value >> 12) & 0xFF));
                buffer[index + 3] = unchecked((byte)((value >> 20) & 0xFF));
                index += 4;
            } else if(value <= 0x0000_0007_FFFF_FFFFul) {
                buffer[index + 0] = unchecked((byte)(0xF0 | (int)(value & 0x07)));
                buffer[index + 1] = unchecked((byte)((value >> 3) & 0xFF));
                buffer[index + 2] = unchecked((byte)((value >> 11) & 0xFF));
                buffer[index + 3] = unchecked((byte)((value >> 19) & 0xFF));
                buffer[index + 4] = unchecked((byte)((value >> 27) & 0xFF));
                index += 5;
            } else if(value <= 0x0000_03FF_FFFF_FFFFul) {
                buffer[index + 0] = unchecked((byte)(0xF8 | (int)(value & 0x03)));
                buffer[index + 1] = unchecked((byte)((value >> 2) & 0xFF));
                buffer[index + 2] = unchecked((byte)((value >> 10) & 0xFF));
                buffer[index + 3] = unchecked((byte)((value >> 18) & 0xFF));
                buffer[index + 4] = unchecked((byte)((value >> 26) & 0xFF));
                buffer[index + 5] = unchecked((byte)((value >> 34) & 0xFF));
                index += 6;
            } else if(value <= 0x0001_FFFF_FFFF_FFFFul) {
                buffer[index + 0] = unchecked((byte)(0xFC | (int)(value & 0x01)));
                buffer[index + 1] = unchecked((byte)((value >> 1) & 0xFF));
                buffer[index + 2] = unchecked((byte)((value >> 9) & 0xFF));
                buffer[index + 3] = unchecked((byte)((value >> 17) & 0xFF));
                buffer[index + 4] = unchecked((byte)((value >> 25) & 0xFF));
                buffer[index + 5] = unchecked((byte)((value >> 33) & 0xFF));
                buffer[index + 6] = unchecked((byte)((value >> 41) & 0xFF));
                index += 7;
            } else if(value <= 0x00FF_FFFF_FFFF_FFFFul) {
                buffer[index + 0] = 0xFE;
                buffer[index + 1] = unchecked((byte)((value >> 0) & 0xFF));
                buffer[index + 2] = unchecked((byte)((value >> 8) & 0xFF));
                buffer[index + 3] = unchecked((byte)((value >> 16) & 0xFF));
                buffer[index + 4] = unchecked((byte)((value >> 24) & 0xFF));
                buffer[index + 5] = unchecked((byte)((value >> 32) & 0xFF));
                buffer[index + 6] = unchecked((byte)((value >> 40) & 0xFF));
                buffer[index + 7] = unchecked((byte)((value >> 48) & 0xFF));
                index += 8;
            } else {
                buffer[index + 0] = 0xFF;
                buffer[index + 1] = unchecked((byte)((value >> 0) & 0xFF));
                buffer[index + 2] = unchecked((byte)((value >> 8) & 0xFF));
                buffer[index + 3] = unchecked((byte)((value >> 16) & 0xFF));
                buffer[index + 4] = unchecked((byte)((value >> 24) & 0xFF));
                buffer[index + 5] = unchecked((byte)((value >> 32) & 0xFF));
                buffer[index + 6] = unchecked((byte)((value >> 40) & 0xFF));
                buffer[index + 7] = unchecked((byte)((value >> 48) & 0xFF));
                buffer[index + 8] = unchecked((byte)((value >> 56) & 0xFF));
                index += 9;
            }
        }
        /// <summary>
        ///     Write variable length LE-encoded (little endian) uint64.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static void WriteVarUInt64(byte[] buffer, ref int index, Stream stream, ulong value) {
            // 20% speedup for using "ref int index" instead of returning the new index
    
            //    value                   first byte  bits
            //                            (encoded_bytes)
            // <= 0x0000_0000_0000_007F   0xxx xxxx   7
            // <= 0x0000_0000_0000_3FFF   10xx xxxx   14
            // <= 0x0000_0000_001F_FFFF   110x xxxx   21
            // <= 0x0000_0000_0FFF_FFFF   1110 xxxx   28
            // <= 0x0000_0007_FFFF_FFFF   1111 0xxx   35
            // <= 0x0000_03FF_FFFF_FFFF   1111 10xx   42
            // <= 0x0001_FFFF_FFFF_FFFF   1111 110x   49
            // <= 0x00FF_FFFF_FFFF_FFFF   1111 1110   56
            // <= 0xFFFF_FFFF_FFFF_FFFF   1111 1111   64
    
    
            if(value <= 0x0000_0000_0000_007Ful) {
                buffer[index++] = unchecked((byte)value);
            } else {
                int remaining = buffer.Length - index;
    
                if(value <= 0x0000_0000_0000_3FFFul) {
                    if(remaining <= 1) {
                        stream.Write(buffer, 0, index);
                        index = 0;
                    }
                    buffer[index + 0] = unchecked((byte)(0x80 | (int)(value & 0x3F)));
                    buffer[index + 1] = unchecked((byte)((value >> 6) & 0xFF));
                    index += 2;
                } else if(value <= 0x0000_0000_001F_FFFFul) {
                    if(remaining <= 2) {
                        stream.Write(buffer, 0, index);
                        index = 0;
                    }
                    buffer[index + 0] = unchecked((byte)(0xC0 | (int)(value & 0x1F)));
                    buffer[index + 1] = unchecked((byte)((value >> 5) & 0xFF));
                    buffer[index + 2] = unchecked((byte)((value >> 13) & 0xFF));
                    index += 3;
                } else if(value <= 0x0000_0000_0FFF_FFFFul) {
                    if(remaining <= 3) {
                        stream.Write(buffer, 0, index);
                        index = 0;
                    }
                    buffer[index + 0] = unchecked((byte)(0xE0 | (int)(value & 0x0F)));
                    buffer[index + 1] = unchecked((byte)((value >> 4) & 0xFF));
                    buffer[index + 2] = unchecked((byte)((value >> 12) & 0xFF));
                    buffer[index + 3] = unchecked((byte)((value >> 20) & 0xFF));
                    index += 4;
                } else if(value <= 0x0000_0007_FFFF_FFFFul) {
                    if(remaining <= 4) {
                        stream.Write(buffer, 0, index);
                        index = 0;
                    }
                    buffer[index + 0] = unchecked((byte)(0xF0 | (int)(value & 0x07)));
                    buffer[index + 1] = unchecked((byte)((value >> 3) & 0xFF));
                    buffer[index + 2] = unchecked((byte)((value >> 11) & 0xFF));
                    buffer[index + 3] = unchecked((byte)((value >> 19) & 0xFF));
                    buffer[index + 4] = unchecked((byte)((value >> 27) & 0xFF));
                    index += 5;
                } else if(value <= 0x0000_03FF_FFFF_FFFFul) {
                    if(remaining <= 5) {
                        stream.Write(buffer, 0, index);
                        index = 0;
                    }
                    buffer[index + 0] = unchecked((byte)(0xF8 | (int)(value & 0x03)));
                    buffer[index + 1] = unchecked((byte)((value >> 2) & 0xFF));
                    buffer[index + 2] = unchecked((byte)((value >> 10) & 0xFF));
                    buffer[index + 3] = unchecked((byte)((value >> 18) & 0xFF));
                    buffer[index + 4] = unchecked((byte)((value >> 26) & 0xFF));
                    buffer[index + 5] = unchecked((byte)((value >> 34) & 0xFF));
                    index += 6;
                } else if(value <= 0x0001_FFFF_FFFF_FFFFul) {
                    if(remaining <= 6) {
                        stream.Write(buffer, 0, index);
                        index = 0;
                    }
                    buffer[index + 0] = unchecked((byte)(0xFC | (int)(value & 0x01)));
                    buffer[index + 1] = unchecked((byte)((value >> 1) & 0xFF));
                    buffer[index + 2] = unchecked((byte)((value >> 9) & 0xFF));
                    buffer[index + 3] = unchecked((byte)((value >> 17) & 0xFF));
                    buffer[index + 4] = unchecked((byte)((value >> 25) & 0xFF));
                    buffer[index + 5] = unchecked((byte)((value >> 33) & 0xFF));
                    buffer[index + 6] = unchecked((byte)((value >> 41) & 0xFF));
                    index += 7;
                } else if(value <= 0x00FF_FFFF_FFFF_FFFFul) {
                    if(remaining <= 7) {
                        stream.Write(buffer, 0, index);
                        index = 0;
                    }
                    buffer[index + 0] = 0xFE;
                    buffer[index + 1] = unchecked((byte)((value >> 0) & 0xFF));
                    buffer[index + 2] = unchecked((byte)((value >> 8) & 0xFF));
                    buffer[index + 3] = unchecked((byte)((value >> 16) & 0xFF));
                    buffer[index + 4] = unchecked((byte)((value >> 24) & 0xFF));
                    buffer[index + 5] = unchecked((byte)((value >> 32) & 0xFF));
                    buffer[index + 6] = unchecked((byte)((value >> 40) & 0xFF));
                    buffer[index + 7] = unchecked((byte)((value >> 48) & 0xFF));
                    index += 8;
                } else {
                    if(remaining <= 8) {
                        stream.Write(buffer, 0, index);
                        index = 0;
                    }
                    buffer[index + 0] = 0xFF;
                    buffer[index + 1] = unchecked((byte)((value >> 0) & 0xFF));
                    buffer[index + 2] = unchecked((byte)((value >> 8) & 0xFF));
                    buffer[index + 3] = unchecked((byte)((value >> 16) & 0xFF));
                    buffer[index + 4] = unchecked((byte)((value >> 24) & 0xFF));
                    buffer[index + 5] = unchecked((byte)((value >> 32) & 0xFF));
                    buffer[index + 6] = unchecked((byte)((value >> 40) & 0xFF));
                    buffer[index + 7] = unchecked((byte)((value >> 48) & 0xFF));
                    buffer[index + 8] = unchecked((byte)((value >> 56) & 0xFF));
                    index += 9;
                }
            }
    
            if(index == buffer.Length) {
                stream.Write(buffer, 0, index);
                index = 0;
            }
        }
        #endregion
        #region private static CalculateVarUInt64Length()
        /// <summary>
        ///     Returns the number of bytes needed to encode a variable length uint64.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static int CalculateVarUInt64Length(ulong value) {
            if(value <= 0x0000_0000_0000_007Ful)
                return 1;
            else if(value <= 0x0000_0000_0000_3FFFul)
                return 2;
            else if(value <= 0x0000_0000_001F_FFFFul)
                return 3;
            else if(value <= 0x0000_0000_0FFF_FFFFul)
                return 4;
            else if(value <= 0x0000_0007_FFFF_FFFFul)
                return 5;
            else if(value <= 0x0000_03FF_FFFF_FFFFul)
                return 6;
            else if(value <= 0x0001_FFFF_FFFF_FFFFul)
                return 7;
            else if(value <= 0x00FF_FFFF_FFFF_FFFFul)
                return 8;
            else 
                return 9;
        }
        #endregion
        #region private static CalculateVarUInt64LengthEncoded()
        /// <summary>
        ///     Decodes the variable length LE-encoded (little endian) int64 encoded size.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static int CalculateVarUInt64LengthEncoded(byte encodedFirstByte) {
            if((encodedFirstByte >> 7) == 0)
                return 1;
            else if((encodedFirstByte >> 6) == 0)
                return 2;
            else if((encodedFirstByte >> 5) == 0)
                return 3;
            else if((encodedFirstByte >> 4) == 0)
                return 4;
            else if((encodedFirstByte >> 3) == 0)
                return 5;
            else if((encodedFirstByte >> 2) == 0)
                return 6;
            else if((encodedFirstByte >> 1) == 0)
                return 7;
            else if((encodedFirstByte & 1) == 0)
                return 8;
            else
                return 9;
        }
        #endregion
        #region private static WriteSpan()
        //[MethodImpl(AggressiveInlining)]
        private static void WriteSpan(byte[] buffer, ref int index, Stream stream, in ReadOnlySpan<byte> value) {
            // 20% speedup for using "ref int index" instead of returning the new index
    
            var remaining = value.Length;
            if(remaining == 0)
                return;
                
            var processed = Math.Min(remaining, buffer.Length - index);
    
            // Buffer.BlockCopy()
            if(remaining <= value.Length)
                value.CopyTo(new Span<byte>(buffer, index, processed));
            else
                value.Slice(0, processed).CopyTo(new Span<byte>(buffer, index, processed));
    
            index += processed;
    
            if(index == buffer.Length) {
                stream.Write(buffer, 0, index);
                index = 0;
            }
    
            remaining    -= processed;
            var readIndex = processed;
    
            while(remaining > 0) {
                processed = Math.Min(remaining, buffer.Length - index);
    
                // Buffer.BlockCopy()
                value.Slice(readIndex, processed).CopyTo(new Span<byte>(buffer, index, processed));
    
                remaining -= processed;
                readIndex += processed;
                index     += processed;
    
                if(index == buffer.Length) {
                    stream.Write(buffer, 0, index);
                    index = 0;
                }
            }
        }
        #endregion
        #region private static BlockCopy()
        /// <summary>
        ///     Same as Buffer.BlockCopy()
        ///     But allows optimisations in case of unsafe context.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static void BlockCopy(byte[] source, int sourceIndex, byte[] dest, int destIndex, int count) {
            //Buffer.BlockCopy(source, sourceIndex, dest, destIndex, count);
            //System.Runtime.CompilerServices.Unsafe.CopyBlock()
            new ReadOnlySpan<byte>(source, sourceIndex, count).CopyTo(new Span<byte>(dest, destIndex, count));
        }
        #endregion
    
        #region private static GetMinChild()
        private static NodePointer GetMinChild(byte[] buffer, long address) {
            var nodeType = (NodeType)buffer[0];
            var index    = CalculateKeysIndex(nodeType);
    
            if(nodeType >= NodeType.Node4 && nodeType <= NodeType.Node32) {
                var pos = index + MaxChildCount(nodeType) + 0 * NODE_POINTER_BYTE_SIZE;
                return new NodePointer(address + pos, ReadNodePointer(buffer, pos));
            } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                int i = 0;
                while(buffer[index + i] == 0)
                    i++;
                i       = buffer[index + i] - 1;
                var pos = index + 256 + i * NODE_POINTER_BYTE_SIZE;
                return new NodePointer(address + pos, ReadNodePointer(buffer, pos));
            } else { // nodeType == NodeType.Node256
                long pos;
                while(true) {
                    pos = ReadNodePointer(buffer, index);
                    if(pos != 0)
                        break;
                    index += NODE_POINTER_BYTE_SIZE;
                }
                return new NodePointer(address + index, pos);
            }
        }
        #endregion
        #region private static GetMaxChild()
        private static NodePointer GetMaxChild(byte[] buffer, long address) {
            var nodeType = (NodeType)buffer[0];
            var index    = CalculateKeysIndex(nodeType);
    
            if(nodeType >= NodeType.Node4 && nodeType <= NodeType.Node32) {
                byte num_children = buffer[1];
                var pos           = index + MaxChildCount(nodeType) + (num_children - 1) * NODE_POINTER_BYTE_SIZE;
                return new NodePointer(address + pos, ReadNodePointer(buffer, pos));
            } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                int i = 255;
                while(buffer[index + i] == 0)
                    i--;
                i       = buffer[index + i] - 1;
                var pos = index + 256 + i * NODE_POINTER_BYTE_SIZE;
                return new NodePointer(address + pos, ReadNodePointer(buffer, pos));
            } else { // nodeType == NodeType.Node256
                long pos;
                index += 255 * NODE_POINTER_BYTE_SIZE;
                while(true) {
                    pos = ReadNodePointer(buffer, index);
                    if(pos != 0)
                        break;
                    index -= NODE_POINTER_BYTE_SIZE;
                }
                return new NodePointer(address + index, pos);
            }
        }
        #endregion
        #region private TryGetMinimumLeaf()
        /// <summary>
        ///    O(log n)
        /// </summary>
        private bool TryGetMinimumLeaf(out TKey key) {
            var current   = m_rootPointer;
            var keyBuffer = new byte[32];
            int keySize   = 0;
    
            while(current != 0) {
                this.Stream.Position = current;
                var nodeType = (NodeType)this.Stream.ReadByte();
                m_buffer[0]  = (byte)nodeType;
    
                // assume fetching data is faster than multiple calls
                int readBytes = this.Stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                if(nodeType == NodeType.Leaf) {
                    int start           = 1;
                    int partial_length2 = unchecked((int)ReadVarInt64(m_buffer, ref start));
                    start              += CalculateVarUInt64LengthEncoded(m_buffer[start]); // long value_length = ReadVarInt64(m_buffer, ref start);
                    ReadLeafKey(m_buffer, ref start, ref readBytes, this.Stream, partial_length2, ref keyBuffer, ref keySize, 0);
    
                    UnescapeLeafKeyTerminator(keyBuffer, 0, ref keySize);
    
                    key = (TKey)m_keyDecoder(keyBuffer, 0, keySize);
                    return true;
                }
    
                // copy partial key
                var partial_length = m_buffer[2];
                for(byte i = 0; i < partial_length; i++) {
                    if(keySize == keyBuffer.Length)
                        Array.Resize(ref keyBuffer, keySize * 2);
                    keyBuffer[keySize++] = m_buffer[3 + i];
                }
    
                current = GetMinChild(m_buffer, current).Target;
            }
    
            key = default;
            return false;
        }
        #endregion
        #region private TryGetMaximumLeaf()
        /// <summary>
        ///    O(log n)
        /// </summary>
        private bool TryGetMaximumLeaf(out TKey key) {
            var current   = m_rootPointer;
            var keyBuffer = new byte[32];
            int keySize   = 0;
    
            while(current != 0) {
                this.Stream.Position = current;
                var nodeType = (NodeType)this.Stream.ReadByte();
                m_buffer[0]  = (byte)nodeType;
    
                // assume fetching data is faster than multiple calls
                int readBytes = this.Stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                if(nodeType == NodeType.Leaf) {
                    int start           = 1;
                    int partial_length2 = unchecked((int)ReadVarInt64(m_buffer, ref start));
                    start              += CalculateVarUInt64LengthEncoded(m_buffer[start]); // long value_length = ReadVarInt64(m_buffer, ref start);
                    ReadLeafKey(m_buffer, ref start, ref readBytes, this.Stream, partial_length2, ref keyBuffer, ref keySize, 0);
    
                    UnescapeLeafKeyTerminator(keyBuffer, 0, ref keySize);
    
                    key = (TKey)m_keyDecoder(keyBuffer, 0, keySize);
                    return true;
                }
    
                // copy partial key
                var partial_length = m_buffer[2];
                for(byte i = 0; i < partial_length; i++) {
                    if(keySize == keyBuffer.Length)
                        Array.Resize(ref keyBuffer, keySize * 2);
                    keyBuffer[keySize++] = m_buffer[3 + i];
                }
    
                current = GetMaxChild(m_buffer, current).Target;
            }
    
            key = default;
            return false;
        }
        #endregion
    
    
        // enumerators
        #region public class ChildrenKeyEnumerator
        /// <summary>
        ///     O(n)
        ///     Returns the current node and all children in order.
        ///     This enumerator is made for re-use, to avoid array reallocations.
        ///     Depth-First pre-order tree traversal.
        /// </summary>
        public sealed class ChildrenKeyEnumerator {
            // manually handled stack for better performance
            // this value can increase fast with a big item count (ie: 1M+)
            // every node will add all its children to the stack, hence why we init with a high value
            private InternalNode[] m_stack = new InternalNode[128];
            private int m_stackIndex = 0;
            private byte[] m_key = new byte[32];
            private readonly byte[] m_buffer = new byte[BUFFER_SIZE];
    
            private readonly struct InternalNode {
                public readonly long Address;
                public readonly int KeySize;
                public InternalNode(long address, int keySize) : this(){
                    this.Address = address;
                    this.KeySize = keySize;
                }
            }
    
            /// <summary>
            ///     Note: for performance reason, the same byte[] is passed and reused.
            /// </summary>
            public IEnumerable<Node> Run(long address, Stream stream, byte[] initialKey = null, int initialKeySize = -1) {
                if(m_stackIndex > 0) {
                    Array.Clear(m_stack, 0, m_stackIndex);
                    m_stackIndex = 0;
                }
    
                // if theres no root
                if(address == 0)
                    yield break;
    
                int keySize = 0;
    
                // copy initial key
                if(initialKey != null && initialKeySize > 0) {
                    if(initialKeySize > initialKey.Length)
                        throw new ArgumentOutOfRangeException(nameof(initialKeySize));
                    if(initialKeySize >= m_key.Length)
                        Array.Resize(ref m_key, initialKeySize * 2); // not going to lead to multiples of 2s, oh well
                    keySize = initialKeySize;
                    BlockCopy(initialKey, 0, m_key, 0, initialKeySize);
                    //new ReadOnlySpan<byte>(initialKey, 0, initialKeySize).CopyTo(new Span<byte>(m_key, 0, initialKeySize));
                }
    
                this.Push(new InternalNode(address, keySize));
    
                var res = new Node(){
                    Key       = m_key,
                    KeyLength = 0,
                };
    
                while(m_stackIndex > 0) {
                    var pop = this.Pop();
    
                    keySize         = pop.KeySize;
                    stream.Position = pop.Address;
                    var nodeType    = (NodeType)stream.ReadByte();
                    m_buffer[0]     = (byte)nodeType;
    
                    // assume fetching data is faster than multiple calls
                    int readBytes = stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                    if(nodeType == NodeType.Leaf) {
                        int start           = 1;
                        int partial_length2 = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        int value_length    = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        ReadLeafKey(m_buffer, ref start, ref readBytes, stream, partial_length2, ref m_key, ref keySize, 0);
                        res.KeyLength   = keySize;
                        res.Key         = m_key;
                        res.ValueLength = value_length;
                        yield return res;
                        continue;
                    }
    
                    // copy partial key
                    var partial_length = m_buffer[2];
                    for(byte i = 0; i < partial_length; i++) {
                        if(keySize == m_key.Length) {
                            Array.Resize(ref m_key, keySize * 2);
                            res.Key = m_key;
                        }
                        m_key[keySize++] = m_buffer[3 + i];
                    }
    
                    var index = CalculateKeysIndex(nodeType);
    
                    if(nodeType >= NodeType.Node4 && nodeType <= NodeType.Node32) {
                        byte num_children = m_buffer[1];
                        var ptr           = index + MaxChildCount(nodeType) + (num_children - 1) * NODE_POINTER_BYTE_SIZE;
                        while(num_children-- > 0) {
                            var child = ReadNodePointer(m_buffer, ptr);
                            ptr      -= NODE_POINTER_BYTE_SIZE;
                            this.Push(new InternalNode(child, keySize));
                        }
                    } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                        for(int i = 255; i >= 0; i--) {
                            var redirect = m_buffer[index + i];
                            if(redirect != 0) {
                                var child = ReadNodePointer(m_buffer, index + 256 + (redirect - 1) * NODE_POINTER_BYTE_SIZE);
                                this.Push(new InternalNode(child, keySize));
                            }
                        }
                    } else { // nodeType == NodeType.Node256
                        var ptr = index + (256 - 1) * NODE_POINTER_BYTE_SIZE;
                        for(int i = 255; i >= 0; i--) {
                            var child = ReadNodePointer(m_buffer, ptr);
                            ptr      -= NODE_POINTER_BYTE_SIZE;
                            if(child != 0)
                                this.Push(new InternalNode(child, keySize));
                        }
                    }
                }
            }
            private void Push(InternalNode value) {
                if(m_stackIndex == m_stack.Length)
                    Array.Resize(ref m_stack, m_stackIndex * 2);
                m_stack[m_stackIndex++] = value;
            }
            private InternalNode Pop() {
                var node = m_stack[--m_stackIndex];
                m_stack[m_stackIndex] = default;
                return node;
            }
        }
        #endregion
        #region public class ChildrenValueEnumerator
        /// <summary>
        ///     O(n)
        ///     Returns the current node and all children in order.
        ///     This enumerator is made for re-use, to avoid array reallocations.
        ///     Depth-First pre-order tree traversal.
        /// </summary>
        public sealed class ChildrenValueEnumerator {
            // manually handled stack for better performance
            // this value can increase fast with a big item count (ie: 1M+)
            // every node will add all its children to the stack, hence why we init with a high value
            private long[] m_stack = new long[128];
            private int m_stackIndex = 0;
            private readonly byte[] m_buffer = new byte[BUFFER_SIZE];
            private byte[] m_bigBuffer = null;
    
            /// <summary>
            ///     Note: for performance reason, the same byte[] is passed and reused.
            /// </summary>
            public IEnumerable<Node> Run(long address, Stream stream) {
                if(m_stackIndex > 0) {
                    Array.Clear(m_stack, 0, m_stackIndex);
                    m_stackIndex = 0;
                }
    
                // if theres no root
                if(address == 0)
                    yield break;
    
                this.Push(address);
    
                var res = new Node();
    
                while(m_stackIndex > 0) {
                    stream.Position = this.Pop();
                    var nodeType    = (NodeType)stream.ReadByte();
                    m_buffer[0]     = (byte)nodeType;
    
                    // assume fetching data is faster than multiple calls
                    int readBytes = stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                    if(nodeType == NodeType.Leaf) {
                        int start           = 1;
                        var partial_length2 = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        var value_length    = ReadVarInt64(m_buffer, ref start);
                        var x               = ReadLeafValue(m_buffer, start + partial_length2, readBytes, stream, value_length, ref m_bigBuffer, out _);
                        res.ValueBuffer     = x.buffer;
                        res.ValueIndex      = x.index;
                        res.ValueLength     = x.len;
                        yield return res;
                        continue;
                    }
    
                    var index = CalculateKeysIndex(nodeType);
    
                    if(nodeType >= NodeType.Node4 && nodeType <= NodeType.Node32) {
                        byte num_children = m_buffer[1];
                        var ptr           = index + MaxChildCount(nodeType) + (num_children - 1) * NODE_POINTER_BYTE_SIZE;
                        while(num_children-- > 0) {
                            var child = ReadNodePointer(m_buffer, ptr);
                            ptr      -= NODE_POINTER_BYTE_SIZE;
                            this.Push(child);
                        }
                    } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                        for(int i = 255; i >= 0; i--) {
                            var redirect = m_buffer[index + i];
                            if(redirect != 0) {
                                var child = ReadNodePointer(m_buffer, index + 256 + (redirect - 1) * NODE_POINTER_BYTE_SIZE);
                                this.Push(child);
                            }
                        }
                    } else { // nodeType == NodeType.Node256
                        var ptr = index + (256 - 1) * NODE_POINTER_BYTE_SIZE;
                        for(int i = 255; i >= 0; i--) {
                            var child = ReadNodePointer(m_buffer, ptr);
                            ptr      -= NODE_POINTER_BYTE_SIZE;
                            if(child != 0)
                                this.Push(child);
                        }
                    }
                }
            }
            private void Push(long address) {
                if(m_stackIndex == m_stack.Length)
                    Array.Resize(ref m_stack, m_stackIndex * 2);
                m_stack[m_stackIndex++] = address;
            }
            private long Pop() {
                var node = m_stack[--m_stackIndex];
                m_stack[m_stackIndex] = default;
                return node;
            }
        }
        #endregion
        #region public class ChildrenEnumerator
        /// <summary>
        ///     O(n)
        ///     Returns the current node and all children in order.
        ///     This enumerator is made for re-use, to avoid array reallocations.
        ///     Depth-First pre-order tree traversal.
        /// </summary>
        public sealed class ChildrenEnumerator {
            // manually handled stack for better performance
            // this value can increase fast with a big item count (ie: 1M+)
            // every node will add all its children to the stack, hence why we init with a high value
            private InternalNode[] m_stack = new InternalNode[128];
            private int m_stackIndex = 0;
            private byte[] m_key = new byte[32];
            private readonly byte[] m_buffer = new byte[BUFFER_SIZE];
            private byte[] m_bigBuffer = null;
    
            private readonly struct InternalNode {
                public readonly long Address;
                public readonly int KeySize;
                public InternalNode(long address, int keySize) : this(){
                    this.Address = address;
                    this.KeySize = keySize;
                }
            }
    
            /// <summary>
            ///     Note: for performance reason, the same byte[]s are passed and reused.
            /// </summary>
            public IEnumerable<Node> Run(long address, Stream stream, byte[] initialKey = null, int initialKeySize = -1) {
                if(m_stackIndex > 0) {
                    Array.Clear(m_stack, 0, m_stackIndex);
                    m_stackIndex = 0;
                }
    
                // if theres no root
                if(address == 0)
                    yield break;
    
                int keySize = 0;
    
                // copy initial key
                if(initialKey != null && initialKeySize > 0) {
                    if(initialKeySize > initialKey.Length)
                        throw new ArgumentOutOfRangeException(nameof(initialKeySize));
                    if(initialKeySize >= m_key.Length)
                        Array.Resize(ref m_key, initialKeySize * 2); // not going to lead to multiples of 2s, oh well
                    keySize = initialKeySize;
                    BlockCopy(initialKey, 0, m_key, 0, initialKeySize);
                    //new ReadOnlySpan<byte>(initialKey, 0, initialKeySize).CopyTo(new Span<byte>(m_key, 0, initialKeySize));
                }
    
                this.Push(new InternalNode(address, keySize));
    
                var res = new Node();
    
                while(m_stackIndex > 0) {
                    var pop = this.Pop();
    
                    keySize         = pop.KeySize;
                    stream.Position = pop.Address;
                    var nodeType    = (NodeType)stream.ReadByte();
                    m_buffer[0]     = (byte)nodeType;
    
                    // assume fetching data is faster than multiple calls
                    int readBytes = stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                    if(nodeType == NodeType.Leaf) {
                        int start           = 1;
                        int partial_length2 = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        int value_length    = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        ReadLeafKey(m_buffer, ref start, ref readBytes, stream, partial_length2, ref m_key, ref keySize, LEAF_NODE_VALUE_PREFETCH_SIZE);
                        var value       = ReadLeafValue(m_buffer, start, readBytes, stream, value_length, ref m_bigBuffer, out _);
                        res.Key         = m_key;
                        res.KeyLength   = keySize;
                        res.ValueBuffer = value.buffer;
                        res.ValueIndex  = value.index;
                        res.ValueLength = value.len; // value_length
                        yield return res;
                        continue;
                    }
    
                    // copy partial key
                    var partial_length = m_buffer[2];
                    for(byte i = 0; i < partial_length; i++) {
                        if(keySize == m_key.Length)
                            Array.Resize(ref m_key, keySize * 2);
                        m_key[keySize++] = m_buffer[3 + i];
                    }
    
                    var index = CalculateKeysIndex(nodeType);
    
                    if(nodeType >= NodeType.Node4 && nodeType <= NodeType.Node32) {
                        byte num_children = m_buffer[1];
                        var ptr           = index + MaxChildCount(nodeType) + (num_children - 1) * NODE_POINTER_BYTE_SIZE;
                        while(num_children-- > 0) {
                            var child = ReadNodePointer(m_buffer, ptr);
                            ptr      -= NODE_POINTER_BYTE_SIZE;
                            this.Push(new InternalNode(child, keySize));
                        }
                    } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                        for(int i = 255; i >= 0; i--) {
                            var redirect = m_buffer[index + i];
                            if(redirect != 0) {
                                var child = ReadNodePointer(m_buffer, index + 256 + (redirect - 1) * NODE_POINTER_BYTE_SIZE);
                                this.Push(new InternalNode(child, keySize));
                            }
                        }
                    } else { // nodeType == NodeType.Node256
                        var ptr = index + (256 - 1) * NODE_POINTER_BYTE_SIZE;
                        for(int i = 255; i >= 0; i--) {
                            var child = ReadNodePointer(m_buffer, ptr);
                            ptr      -= NODE_POINTER_BYTE_SIZE;
                            if(child != 0)
                                this.Push(new InternalNode(child, keySize));
                        }
                    }
                }
            }
            private void Push(InternalNode value) {
                if(m_stackIndex == m_stack.Length)
                    Array.Resize(ref m_stack, m_stackIndex * 2);
                m_stack[m_stackIndex++] = value;
            }
            private InternalNode Pop() {
                var node = m_stack[--m_stackIndex];
                m_stack[m_stackIndex] = default;
                return node;
            }
        }
        #endregion
        #region protected class PathEnumerator
        /// <summary>
        ///     O(n)
        ///     Lists every node, as well as the entire set of parents for each of them.
        ///     Be aware that returned data re-uses the same pointers, clone data if processing is not immediate.
        ///     This enumerator is made for re-use, to avoid array reallocations.
        /// </summary>
        protected sealed class PathEnumerator {
            // manually handled stack for better performance
            // this value can increase fast with a big item count (ie: 1M+)
            // every node will add all its children to the stack, hence why we init with a high value
            private InternalNode[] m_array = new InternalNode[128];
            private int m_count = 0;
            private byte[] m_key = new byte[32];
            private readonly byte[] m_buffer = new byte[BUFFER_SIZE];
            private byte[] m_bigBuffer = null;
            private int m_head = 0;
            private int m_tail = 0;
    
            private sealed class InternalNode {
                public readonly NodePointer Pointer;
                public readonly int KeySize;
                public readonly int Depth;
                public readonly Node Node;
                public readonly InternalNode Parent;
                public InternalNode(NodePointer pointer, int keySize, int depth, Node node, InternalNode parent) {
                    this.Pointer = pointer;
                    this.KeySize = keySize;
                    this.Depth   = depth;
                    this.Node    = node;
                    this.Parent  = parent;
                }
            }
    
            public enum TraversalAlgorithm {
                /// <summary>
                ///     Depth-First pre-order traversal.
                /// </summary>
                DepthFirst = 0,
                /// <summary>
                ///     Breadth-First (level-order) traversal.
                /// </summary>
                BreadthFirst = 1,
            }
    
            /// <summary>
            ///     If returnOnNodes==true, then you only need to read the last item of the path to know whats the 'current' node.
            ///     Be aware that returned data re-uses the same pointers, clone data if processing is not immediate.
            /// </summary>
            /// <param name="returnOnNodes">If false, return only the path on leafs. If true, returns the path on every node.</param>
            public IEnumerable<Path> Run(NodePointer pointer, Stream stream, bool returnOnNodes, bool extractValue, byte[] initialKey = null, int initialKeySize = 0, TraversalAlgorithm traversalAlgorithm = TraversalAlgorithm.DepthFirst) {
                if(m_count > 0) {
                    Array.Clear(m_array, 0, m_count);
                    m_count = 0;
                }
    
                // if theres no root
                if(pointer.Target == 0)
                    return Enumerable.Empty<Path>();
    
                m_head = 0;
                m_tail = 0;
                    
                if(traversalAlgorithm == TraversalAlgorithm.DepthFirst)
                    return this.DepthFirst(pointer, stream, returnOnNodes, extractValue, initialKey, initialKeySize);
                else
                    return this.BreadthFirst(pointer, stream, returnOnNodes, extractValue, initialKey, initialKeySize);
            }
            private IEnumerable<Path> DepthFirst(NodePointer pointer, Stream stream, bool returnOnNodes, bool extractValue, byte[] initialKey, int initialKeySize) {
                var path = new Path(){
                    Key   = m_key,
                    Trail = new List<Node>(16),
                };
                int keySize = 0;
    
                // copy initial key
                if(initialKey != null && initialKeySize > 0) {
                    if(initialKeySize > initialKey.Length)
                        throw new ArgumentOutOfRangeException(nameof(initialKeySize));
                    if(initialKeySize >= m_key.Length) {
                        Array.Resize(ref m_key, initialKeySize * 2); // not going to lead to multiples of 2s, oh well
                        path.Key = m_key;
                    }
                    keySize = initialKeySize;
                    BlockCopy(initialKey, 0, m_key, 0, initialKeySize);
                    //new ReadOnlySpan<byte>(initialKey, 0, initialKeySize).CopyTo(new Span<byte>(m_key, 0, initialKeySize));
                }
    
                this.Push(new InternalNode(pointer, keySize, 0, null, null));
    
                while(m_count > 0) {
                    var pop = this.Pop();
    
                    keySize         = pop.KeySize;
                    stream.Position = pop.Pointer.Target;
                    var depth       = pop.Depth;
                    var nodeType    = (NodeType)stream.ReadByte();
                    m_buffer[0]     = (byte)nodeType;
                    Node current;
    
                    if(depth < path.Trail.Count) {
                        current    = path.Trail[depth];
                        var extras = path.Trail.Count - (depth + 1);
                        if(extras > 0)
                            path.Trail.RemoveRange(depth + 1, extras);
                    } else {
                        current    = new Node();
                        path.Trail.Add(current);
                    }
    
                    current.Pointer = pop.Pointer;
                    current.Type    = nodeType;
    
                    // assume fetching data is faster than multiple calls
                    int readBytes = stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                    if(nodeType == NodeType.Leaf) {
                        int start             = 1;
                        int keySizeStart      = keySize;
                        int partial_length2   = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        int value_length      = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        ReadLeafKey(m_buffer, ref start, ref readBytes, stream, partial_length2, ref m_key, ref keySize, LEAF_NODE_VALUE_PREFETCH_SIZE);
                        path.Key              = m_key;
                        current.KeyLength     = keySize - keySizeStart;
                        current.ChildrenCount = 0;
                        current.ValueLength   = value_length;
                        if(extractValue) {
                            var value = ReadLeafValue(m_buffer, start, readBytes, stream, value_length, ref m_bigBuffer, out _);
                            current.ValueBuffer = value.buffer;
                            current.ValueIndex  = value.index;
                            //current.ValueLength = value.len; // redundant
                        }
                        yield return path;
                        continue;
                    }
    
                    // copy partial key
                    var partial_length = m_buffer[2];
                    for(byte i = 0; i < partial_length; i++) {
                        if(keySize == m_key.Length) {
                            Array.Resize(ref m_key, keySize * 2);
                            path.Key = m_key;
                        }
                        m_key[keySize++] = m_buffer[3 + i];
                    }
    
                    current.ChildrenCount = m_buffer[1];
                    current.KeyLength     = partial_length;
    
                    if(returnOnNodes)
                        yield return path;
    
                    var index = CalculateKeysIndex(nodeType);
    
                    if(nodeType >= NodeType.Node4 && nodeType <= NodeType.Node32) {
                        byte num_children = m_buffer[1];
                        var ptr           = index + MaxChildCount(nodeType) + (num_children - 1) * NODE_POINTER_BYTE_SIZE;
                        var realPtr       = pop.Pointer.Target + ptr;
                        while(num_children-- > 0) {
                            var child        = ReadNodePointer(m_buffer, ptr);
                            var internalNode = new InternalNode(new NodePointer(realPtr, child), keySize, depth + 1, null, null);
                            this.Push(internalNode);
                            ptr             -= NODE_POINTER_BYTE_SIZE;
                            realPtr         -= NODE_POINTER_BYTE_SIZE;
                        }
                    } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                        for(int i = 255; i >= 0; i--) {
                            var redirect = m_buffer[index + i];
                            if(redirect != 0) {
                                var ptr          = index + 256 + (redirect - 1) * NODE_POINTER_BYTE_SIZE;
                                var realPtr      = pop.Pointer.Target + ptr;
                                var child        = ReadNodePointer(m_buffer, ptr);
                                var internalNode = new InternalNode(new NodePointer(realPtr, child), keySize, depth + 1, null, null);
                                this.Push(internalNode);
                            }
                        }
                    } else { // nodeType == NodeType.Node256
                        var ptr     = index + (256 - 1) * NODE_POINTER_BYTE_SIZE;
                        var realPtr = pop.Pointer.Target + ptr;
                        for(int i = 255; i >= 0; i--) {
                            var child = ReadNodePointer(m_buffer, ptr);
                            if(child != 0) {
                                var internalNode = new InternalNode(new NodePointer(realPtr, child), keySize, depth + 1, null, null);
                                this.Push(internalNode);
                            }
                            ptr     -= NODE_POINTER_BYTE_SIZE;
                            realPtr -= NODE_POINTER_BYTE_SIZE;
                        }
                    }
                }
            }
            private void Push(InternalNode value) {
                if(m_count == m_array.Length)
                    Array.Resize(ref m_array, m_count * 2);
                m_array[m_count++] = value;
            }
            private InternalNode Pop() {
                var node = m_array[--m_count];
                m_array[m_count] = default;
                return node;
            }
            private IEnumerable<Path> BreadthFirst(NodePointer pointer, Stream stream, bool returnOnNodes, bool extractValue, byte[] initialKey, int initialKeySize) {
                var path = new Path(){
                    Key   = m_key,
                    Trail = new List<Node>(16),
                };
                int keySize = 0;
    
                // copy initial key
                if(initialKey != null && initialKeySize > 0) {
                    if(initialKeySize > initialKey.Length)
                        throw new ArgumentOutOfRangeException(nameof(initialKeySize));
                    if(initialKeySize >= m_key.Length) {
                        Array.Resize(ref m_key, initialKeySize * 2); // not going to lead to multiples of 2s, oh well
                        path.Key = m_key;
                    }
                    keySize = initialKeySize;
                    BlockCopy(initialKey, 0, m_key, 0, initialKeySize);
                    //new ReadOnlySpan<byte>(initialKey, 0, initialKeySize).CopyTo(new Span<byte>(m_key, 0, initialKeySize));
                }
    
                this.Enqueue(new InternalNode(pointer, keySize, 0, null, null));
    
                while(m_count > 0) {
                    var pop = this.Dequeue();
    
                    keySize         = pop.KeySize;
                    stream.Position = pop.Pointer.Target;
                    var depth       = pop.Depth;
                    var nodeType    = (NodeType)stream.ReadByte();
                    m_buffer[0]     = (byte)nodeType;
                    var current     = new Node();
    
                    if(depth < path.Trail.Count)
                        path.Trail[depth] = current;
                    else
                        path.Trail.Add(current);
    
                    var d     = depth;
                    var xNode = pop;
                    while(d-- > 0) {
                        var prev      = path.Trail[d];
                        if(object.ReferenceEquals(prev, xNode.Node))
                            break;
                        path.Trail[d] = xNode.Node;
                        xNode         = xNode.Parent;
                    }
    
                    current.Pointer = pop.Pointer;
                    current.Type    = nodeType;
    
                    // assume fetching data is faster than multiple calls
                    int readBytes = stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                    if(nodeType == NodeType.Leaf) {
                        int start             = 1;
                        int keySizeStart      = keySize;
                        int partial_length2   = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        int value_length      = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        ReadLeafKey(m_buffer, ref start, ref readBytes, stream, partial_length2, ref m_key, ref keySize, LEAF_NODE_VALUE_PREFETCH_SIZE);
                        path.Key              = m_key;
                        current.KeyLength     = keySize - keySizeStart;
                        current.ChildrenCount = 0;
                        current.ValueLength   = value_length;
                        if(extractValue) {
                            var value = ReadLeafValue(m_buffer, start, readBytes, stream, value_length, ref m_bigBuffer, out _);
                            current.ValueBuffer = value.buffer;
                            current.ValueIndex  = value.index;
                            //current.ValueLength = value.len; // redundant
                        }
                        yield return path;
                        continue;
                    }
    
                    // copy partial key
                    var partial_length = m_buffer[2];
                    for(byte i = 0; i < partial_length; i++) {
                        if(keySize == m_key.Length) {
                            Array.Resize(ref m_key, keySize * 2);
                            path.Key = m_key;
                        }
                        m_key[keySize++] = m_buffer[3 + i];
                    }
    
                    current.ChildrenCount = m_buffer[1];
                    current.KeyLength     = partial_length;
    
                    if(returnOnNodes)
                        yield return path;
    
                    var index = CalculateKeysIndex(nodeType);
    
                    if(nodeType >= NodeType.Node4 && nodeType <= NodeType.Node32) {
                        byte num_children = m_buffer[1];
                        var ptr           = index + MaxChildCount(nodeType) + 0 * NODE_POINTER_BYTE_SIZE;
                        var realPtr       = pop.Pointer.Target + ptr;
                        while(num_children-- > 0) {
                            var child        = ReadNodePointer(m_buffer, ptr);
                            var internalNode = new InternalNode(new NodePointer(realPtr, child), keySize, depth + 1, current, pop);
                            this.Enqueue(internalNode);
                            ptr             += NODE_POINTER_BYTE_SIZE;
                            realPtr         += NODE_POINTER_BYTE_SIZE;
                        }
                    } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                        for(int i = 0; i < 256; i++) {
                            var redirect = m_buffer[index + i];
                            if(redirect != 0) {
                                var ptr          = index + 256 + (redirect - 1) * NODE_POINTER_BYTE_SIZE;
                                var realPtr      = pop.Pointer.Target + ptr;
                                var child        = ReadNodePointer(m_buffer, ptr);
                                var internalNode = new InternalNode(new NodePointer(realPtr, child), keySize, depth + 1, current, pop);
                                this.Enqueue(internalNode);
                            }
                        }
                    } else { // nodeType == NodeType.Node256
                        var ptr     = index + 0 * NODE_POINTER_BYTE_SIZE;
                        var realPtr = pop.Pointer.Target + ptr;
                        for(int i = 0; i < 256; i++) {
                            var child = ReadNodePointer(m_buffer, ptr);
                            if(child != 0) {
                                var internalNode = new InternalNode(new NodePointer(realPtr, child), keySize, depth + 1, current, pop);
                                this.Enqueue(internalNode);
                            }
                            ptr     += NODE_POINTER_BYTE_SIZE;
                            realPtr += NODE_POINTER_BYTE_SIZE;
                        }
                    }
                }
            }
            private void Enqueue(InternalNode value) {
                if(m_count == m_array.Length) {
                    var capacity = m_count * 2;
                    var _new = new InternalNode[capacity];
                    if(m_count > 0) {
                        if(m_head < m_tail)
                            Array.Copy(m_array, m_head, _new, 0, m_count);
                        else {
                            Array.Copy(m_array, m_head, _new, 0, m_array.Length - m_head);
                            Array.Copy(m_array, 0, _new, m_array.Length - m_head, m_tail);
                        }
                    }
                    m_array = _new;
                    m_head  = 0;
                    m_tail  = (m_count == capacity) ? 0 : m_count;
                }
    
                m_array[m_tail] = value;
                m_tail          = (m_tail + 1) % m_array.Length;
                m_count++;
            }
            private InternalNode Dequeue() {
                var node        = m_array[m_head];
                m_array[m_head] = default;
                m_head          = (m_head + 1) % m_array.Length;
                m_count--;
                return node;
            }
    
            public sealed class Path {
                public List<Node> Trail;
                /// <summary>
                ///     The encoded key.
                ///     Excludes the LEAF_NODE_KEY_TERMINATOR.
                ///     This key contains all the trail keys, including past the partial key matches.
                /// </summary>
                public byte[] Key;
            }
            public sealed class Node : ICloneable {
                public NodePointer Pointer;
                public NodeType Type;
                public byte ChildrenCount;
                /// <summary>
                ///     Excludes LEAF_NODE_KEY_TERMINATOR
                /// </summary>
                public int KeyLength;
                public byte[] ValueBuffer;
                public int ValueIndex;
                public int ValueLength;
    
                public TKey GetKey(AdaptiveRadixTree<TKey, TValue> owner, Path path, ref byte[] buffer) {
                    int length = this.KeyLength;
                    var rawKey = this.GetKeyRaw(path);
                    if(rawKey.Length > buffer.Length)
                        buffer = new byte[rawKey.Length];
                    rawKey.CopyTo(new Span<byte>(buffer));
                    UnescapeLeafKeyTerminator(buffer, 0, ref length);
                    return (TKey)owner.m_keyDecoder(buffer, 0, length);
                }
                public ReadOnlySpan<byte> GetKeyRaw(Path path) {
                    var index = path.Trail.IndexOf(this);
                    int total = 0;
                    for(int i = 0; i < index; i++)
                        total += path.Trail[i].KeyLength;
                    return new ReadOnlySpan<byte>(path.Key, total, this.KeyLength);
                }
                public TValue GetValue(AdaptiveRadixTree<TKey, TValue> owner) {
                    if(this.Type != NodeType.Leaf)
                        throw new ApplicationException("Only leaf nodes contain values.");
                    return (TValue)owner.m_valueDecoder(this.ValueBuffer, this.ValueIndex, this.ValueLength);
                }
                public int CalculateNodeSize() {
                    if(this.Type != NodeType.Leaf)
                        return AdaptiveRadixTree<TKey, TValue>.CalculateNodeSize(this.Type);
                    else
                        return CalculateLeafNodeSize(this.KeyLength + 1, this.ValueLength);
                }
                /// <summary>
                ///     Use CalculateNodeSize() for how much to read from buffer.
                /// </summary>
                public byte[] GetRawNode(PathEnumerator owner) {
                    if(this.Type == NodeType.Leaf)
                        return null;
                    return owner.m_buffer;
                }
    
                public object Clone() {
                    return new Node() {
                        ChildrenCount = this.ChildrenCount,
                        KeyLength     = this.KeyLength,
                        Pointer       = this.Pointer,
                        Type          = this.Type,
                        ValueBuffer   = this.ValueBuffer,
                        ValueIndex    = this.ValueIndex,
                        ValueLength   = this.ValueLength,
                    };
                }
    
                public override string ToString() {
                    return $"[{this.Pointer}] {this.Type.ToString()}";
                }
            }
        }
        #endregion
        #region public class FilterablePathEnumerator
        /// <summary>
        ///     O(n)
        ///     Beam Search path enumerator.
        ///     Depth-First pre-order traversal.
        ///     Lets you prune/filter the branches to visit as it enumerates.
        ///     This enumerator is made for re-use, to avoid array reallocations.
        /// </summary>
        public sealed class FilterablePathEnumerator {
            // manually handled stack for better performance
            // this value can increase fast with a big item count (ie: 1M+)
            // every node will add all its children to the stack, hence why we init with a high value
            private InternalNode[] m_array = new InternalNode[128];
            private int m_count = 0;
            private byte[] m_key = new byte[32];
            private readonly byte[] m_buffer = new byte[BUFFER_SIZE];
            private byte[] m_bigBuffer = null;
    
            public class Options {
                public AdaptiveRadixTree<TKey, TValue> Owner;
                /// <summary>
                ///     Calculates how many characters differ from expected.
                ///     This behaves the same as "Predicate&lt;FilterItem&gt; AcceptPredicate", return 1 to indicate "not accepted" and 0 for "accepted".
                /// </summary>
                /// <remarks>
                ///     Do not return any negative values.
                ///     No safeguards are in place if you do.
                /// </remarks>
                public Func<FilterItem, int> CalculateHammingDistance;
                public bool ExtractValue;
                /// <summary>
                ///     The Hamming distance.
                ///     Specifies how many characters may differ from input.
                ///     
                ///     0 = exact match (default).
                ///     1 = 1 character may differ
                ///     2 = ...
                /// </summary>
                /// <remarks>
                ///     Do not return any negative values.
                ///     No safeguards are in place if you do.
                /// </remarks>
                public int HammingDistance = 0; // 0 = exact match, 1 = 1 character may differ, etc. Dont put negative values in there.
            }
    
            private sealed class InternalNode {
                public readonly long Address;
                public readonly int PrevKeySize;
                public readonly int KeySize;
                public readonly int Hamming;
                public InternalNode(long address, int prevKeySize, int keySize, int hamming) {
                    this.Address     = address;
                    this.PrevKeySize = prevKeySize;
                    this.KeySize     = keySize;
                    this.Hamming     = hamming;
                }
            }
            public sealed class FilterItem {
                public byte[] EncodedKey; // normally you want to call UnescapeLeafKeyTerminator() on this
                public int KeyLength;
                public int LastAcceptedLength;
                /// <summary>
                ///     Values:
                ///     false = currently validating a node
                ///     true  = currently validating a leaf
                ///     null  = currently validating a node.child
                /// </summary>
                public bool? IsLeaf;
    
                /// <summary>
                ///     Mostly there only for debugging purposes.
                /// </summary>
                public TKey GetKey(AdaptiveRadixTree<TKey, TValue> owner) {
                    //UnescapeLeafKeyTerminator(this.encoded_key, 0, ref this.key_len);
                    return (TKey)owner.m_keyDecoder(this.EncodedKey, 0, this.KeyLength);
                }
            }
    
            /// <summary>
            ///     For performance reasons, the same item is always the one returned.
            ///     Copy it if you need all results (And copy the key too).
            /// </summary>
            public IEnumerable<Node> Run(Options options) {
                if(m_count > 0) {
                    Array.Clear(m_array, 0, m_count);
                    m_count = 0;
                }
    
                var owner                    = options.Owner;
                var calculateHammingDistance = options.CalculateHammingDistance;
                var extractValue             = options.ExtractValue;
    
                var pointer = owner.m_rootPointer;
                var stream  = owner.Stream;
    
                // if theres no root
                if(pointer == 0)
                    yield break;
    
                var res = new Node(){
                    Key = m_key,
                };
                var filter = new FilterItem(){
                    EncodedKey = m_key,
                };
    
                this.Push(new InternalNode(pointer, 0, 0, options.HammingDistance));
    
                while(m_count > 0) {
                    var pop = this.Pop();
    
                    var keySize     = pop.KeySize;
                    stream.Position = pop.Address;
                    long hamming    = pop.Hamming; // long to avoid overflows
                    var nodeType    = (NodeType)stream.ReadByte();
                    m_buffer[0]     = (byte)nodeType;
    
                    res.Address               = pop.Address;
                    res.Type                  = nodeType;
                    filter.LastAcceptedLength = pop.PrevKeySize;
    
                    // assume fetching data is faster than multiple calls
                    int readBytes = stream.Read(m_buffer, 1, CalculateNodePrefetchSize(nodeType) - 1) + 1;
    
                    if(nodeType == NodeType.Leaf) {
                        int start           = 1;
                        int partial_length2 = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        int value_length    = unchecked((int)ReadVarInt64(m_buffer, ref start));
                        ReadLeafKey(m_buffer, ref start, ref readBytes, stream, partial_length2, ref m_key, ref keySize, LEAF_NODE_VALUE_PREFETCH_SIZE);
                        res.Key             = m_key;
                        filter.EncodedKey   = m_key;
                        filter.IsLeaf       = true;
                        filter.KeyLength    = keySize;
    
                        if(filter.LastAcceptedLength >= keySize || (hamming -= calculateHammingDistance(filter)) >= 0) {
                            res.KeyLength       = keySize;
                            res.ChildrenCount   = 0;
                            res.HammingDistance = unchecked((int)hamming);
                            res.ValueLength     = value_length;
    
                            if(extractValue) {
                                var value       = ReadLeafValue(m_buffer, start, readBytes, stream, value_length, ref m_bigBuffer, out _);
                                res.ValueBuffer = value.buffer;
                                res.ValueIndex  = value.index;
                                //res.ValueLength = value.len; // redundant
                            }
                            yield return res;
                        }
                        continue;
                    }
    
                    // copy partial key
                    var partial_length = m_buffer[2];
                    for(byte i = 0; i < partial_length; i++) {
                        if(keySize == m_key.Length) {
                            Array.Resize(ref m_key, keySize * 2);
                            res.Key           = m_key;
                            filter.EncodedKey = m_key;
                        }
                        m_key[keySize++] = m_buffer[3 + i];
                    }
                    // make sure one extra character is avail
                    if(keySize == m_key.Length) {
                        Array.Resize(ref m_key, keySize * 2);
                        res.Key           = m_key;
                        filter.EncodedKey = m_key;
                    }
    
                    res.ChildrenCount = m_buffer[1];
                    res.KeyLength     = partial_length;
    
    
                    filter.IsLeaf    = false;
                    filter.KeyLength = keySize;
                    if(filter.KeyLength > filter.LastAcceptedLength && (hamming -= calculateHammingDistance(filter)) < 0)
                        continue;
    
    
                    var index        = CalculateKeysIndex(nodeType);
                    var writePos     = keySize; // filter.last_accepted_len + partial_length; // keySize
                    filter.KeyLength = keySize + 1;
                    filter.IsLeaf    = null;
    
                    if(nodeType >= NodeType.Node4 && nodeType <= NodeType.Node32) {
                        // note: potentially consider calling calculateHammingDistance() in proper order for the children
                        // in case the implementation assumes the received order matches the listing order
    
                        byte num_children = m_buffer[1];
                        var ptr           = index + MaxChildCount(nodeType) + (num_children - 1) * NODE_POINTER_BYTE_SIZE;
                        var partialIndex  = index + (num_children - 1);
                        var realPtr       = pop.Address + ptr;
                        while(num_children-- > 0) {
                            var c                       = m_buffer[partialIndex];
                            filter.EncodedKey[writePos] = c;
    
                            var ham_dist = hamming;
                            if(c == LEAF_NODE_KEY_TERMINATOR || (ham_dist -= calculateHammingDistance(filter)) >= 0) {
                                var child        = ReadNodePointer(m_buffer, ptr);
                                var internalNode = new InternalNode(child, keySize + 1, keySize, unchecked((int)ham_dist));
                                this.Push(internalNode);
                            }
    
                            ptr     -= NODE_POINTER_BYTE_SIZE;
                            realPtr -= NODE_POINTER_BYTE_SIZE;
                            partialIndex--;
                        }
                    } else if(nodeType == NodeType.Node64 || nodeType == NodeType.Node128) {
                        // note: potentially consider calling calculateHammingDistance() in proper order for the children
                        // in case the implementation assumes the received order matches the listing order
    
                        for(int i = 255; i >= 0; i--) {
                            var redirect = m_buffer[index + i];
                            if(redirect != 0) {
                                var c                       = unchecked((byte)i);
                                filter.EncodedKey[writePos] = c;
    
                                var ham_dist = hamming;
                                if(c == LEAF_NODE_KEY_TERMINATOR || (ham_dist -= calculateHammingDistance(filter)) >= 0) {
                                    var ptr          = index + 256 + (redirect - 1) * NODE_POINTER_BYTE_SIZE;
                                    var child        = ReadNodePointer(m_buffer, ptr);
                                    var internalNode = new InternalNode(child, keySize + 1, keySize, unchecked((int)ham_dist));
                                    this.Push(internalNode);
                                }
                            }
                        }
                    } else { // nodeType == NodeType.Node256
                        // note: potentially consider calling calculateHammingDistance() in proper order for the children
                        // in case the implementation assumes the received order matches the listing order
    
                        var ptr     = index + (256 - 1) * NODE_POINTER_BYTE_SIZE;
                        var realPtr = pop.Address + ptr;
                        for(int i = 255; i >= 0; i--) {
                            var child = ReadNodePointer(m_buffer, ptr);
                            if(child != 0) {
                                var c                       = unchecked((byte)i);
                                filter.EncodedKey[writePos] = c;
    
                                var ham_dist = hamming;
                                if(c == LEAF_NODE_KEY_TERMINATOR || (ham_dist -= calculateHammingDistance(filter)) >= 0) {
                                    var internalNode = new InternalNode(child, keySize + 1, keySize, unchecked((int)ham_dist));
                                    this.Push(internalNode);
                                }
                            }
                            ptr     -= NODE_POINTER_BYTE_SIZE;
                            realPtr -= NODE_POINTER_BYTE_SIZE;
                        }
                    }
                }
            }
            private void Push(InternalNode value) {
                if(m_count == m_array.Length)
                    Array.Resize(ref m_array, m_count * 2);
                m_array[m_count++] = value;
            }
            private InternalNode Pop() {
                var node = m_array[--m_count];
                m_array[m_count] = default;
                return node;
            }
    
            public sealed class Node : AdaptiveRadixTree<TKey, TValue>.Node {
                /// <summary>
                ///     Remaining hamming distance.
                /// </summary>
                public int HammingDistance;
    
                public long Address;
                internal NodeType Type;
                public byte ChildrenCount;
    
                public override string ToString() {
                    return $"[@{this.Address}] {this.Type.ToString()}";
                }
    
                public object Clone() {
                    return new Node() {
                        Address         = this.Address,
                        ChildrenCount   = this.ChildrenCount,
                        HammingDistance = this.HammingDistance,
                        Key             = this.Key,
                        KeyLength       = this.KeyLength,
                        Type            = this.Type,
                        ValueBuffer     = this.ValueBuffer,
                        ValueIndex      = this.ValueIndex,
                        ValueLength     = this.ValueLength,
                    };
                }
            }
        }
        #endregion
        #region public class Node
        public class Node {
            /// <summary>
            ///     The encoded key.
            ///     Excludes the LEAF_NODE_KEY_TERMINATOR.
            /// </summary>
            public byte[] Key;
            /// <summary>
            ///     Excludes LEAF_NODE_KEY_TERMINATOR
            /// </summary>
            public int KeyLength;
            public byte[] ValueBuffer;
            public int ValueIndex;
            public int ValueLength;
    
            public TKey GetKey(AdaptiveRadixTree<TKey, TValue> owner) {
                UnescapeLeafKeyTerminator(this.Key, 0, ref this.KeyLength);
                return (TKey)owner.m_keyDecoder(this.Key, 0, this.KeyLength);
            }
            public TValue GetValue(AdaptiveRadixTree<TKey, TValue> owner) {
                return (TValue)owner.m_valueDecoder(this.ValueBuffer, this.ValueIndex, this.ValueLength);
            }
            public KeyValuePair<TKey, TValue> GetItem(AdaptiveRadixTree<TKey, TValue> owner) {
                return new KeyValuePair<TKey, TValue>(this.GetKey(owner), this.GetValue(owner));
            }
        }
        #endregion
    
        #region protected Alloc()
        [MethodImpl(AggressiveInlining)]
        protected long Alloc(NodeType nodeType) {
            //return this.Alloc(CalculateNodeSize(nodeType));
    
            switch(nodeType) {
                case NodeType.Node4:   return m_memoryManagerNode4.Alloc();
                case NodeType.Node8:   return m_memoryManagerNode8.Alloc();
                case NodeType.Node16:  return m_memoryManagerNode16.Alloc();
                case NodeType.Node32:  return m_memoryManagerNode32.Alloc();
                case NodeType.Node64:  return m_memoryManagerNode64.Alloc();
                case NodeType.Node128: return m_memoryManagerNode128.Alloc();
                case NodeType.Node256: return m_memoryManagerNode256.Alloc();
                default:
                    return -1;
                    //throw new ArgumentException(nameof(nodeType));
            }
        }
        [MethodImpl(AggressiveInlining)]
        protected long Alloc(long length) {
            return m_memoryManager.Alloc(length);
        }
        #endregion
        #region protected Free()
        [MethodImpl(AggressiveInlining)]
        protected void Free(long address, NodeType nodeType) {
            //this.Free(address, CalculateNodeSize(nodeType));
    
            switch(nodeType) {
                case NodeType.Node4:   m_memoryManagerNode4.Free(address);   break;
                case NodeType.Node8:   m_memoryManagerNode8.Free(address);   break;
                case NodeType.Node16:  m_memoryManagerNode16.Free(address);  break;
                case NodeType.Node32:  m_memoryManagerNode32.Free(address);  break;
                case NodeType.Node64:  m_memoryManagerNode64.Free(address);  break;
                case NodeType.Node128: m_memoryManagerNode128.Free(address); break;
                case NodeType.Node256: m_memoryManagerNode256.Free(address); break;
                //default: throw new ArgumentException(nameof(nodeType));
            }
        }
        [MethodImpl(AggressiveInlining)]
        protected void Free(long address, long length) {
            var prevCapacity = m_memoryManager.Capacity;
    
            m_memoryManager.Free(address, length);
    
            var newCapacity = m_memoryManager.Capacity;
                
            // if we happen to downsize, then notify the stream
            if(newCapacity < prevCapacity)
                this.Stream.SetLength(newCapacity);
        }
        #endregion
    
        // default encoders
        #region public static GetDefaultEncoder<T>()
#if USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE
        public static Action<T, Buffer> GetDefaultEncoder<T>() {
            if(typeof(T) == typeof(string))   return Unsafe.As<Action<T, Buffer>>(new Action<string, Buffer>(EncodeString));
            if(typeof(T) == typeof(char))     return Unsafe.As<Action<T, Buffer>>(new Action<char, Buffer>(EncodeChar));
            if(typeof(T) == typeof(sbyte))    return Unsafe.As<Action<T, Buffer>>(new Action<sbyte, Buffer>(EncodeInt8));
            if(typeof(T) == typeof(short))    return Unsafe.As<Action<T, Buffer>>(new Action<short, Buffer>(EncodeInt16));
            if(typeof(T) == typeof(int))      return Unsafe.As<Action<T, Buffer>>(new Action<int, Buffer>(EncodeInt32));
            if(typeof(T) == typeof(long))     return Unsafe.As<Action<T, Buffer>>(new Action<long, Buffer>(EncodeInt64));
            if(typeof(T) == typeof(byte))     return Unsafe.As<Action<T, Buffer>>(new Action<byte, Buffer>(EncodeUInt8));
            if(typeof(T) == typeof(ushort))   return Unsafe.As<Action<T, Buffer>>(new Action<ushort, Buffer>(EncodeUInt16));
            if(typeof(T) == typeof(uint))     return Unsafe.As<Action<T, Buffer>>(new Action<uint, Buffer>(EncodeUInt32));
            if(typeof(T) == typeof(ulong))    return Unsafe.As<Action<T, Buffer>>(new Action<ulong, Buffer>(EncodeUInt64));
            if(typeof(T) == typeof(bool))     return Unsafe.As<Action<T, Buffer>>(new Action<bool, Buffer>(EncodeBool));
            if(typeof(T) == typeof(float))    return Unsafe.As<Action<T, Buffer>>(new Action<float, Buffer>(BitConverter.IsLittleEndian ? EncodeFloatLE : (Action<float, Buffer>)EncodeFloat));
            if(typeof(T) == typeof(double))   return Unsafe.As<Action<T, Buffer>>(new Action<double, Buffer>(EncodeDouble));
            if(typeof(T) == typeof(decimal))  return Unsafe.As<Action<T, Buffer>>(new Action<decimal, Buffer>(EncodeDecimal));
            if(typeof(T) == typeof(DateTime)) return Unsafe.As<Action<T, Buffer>>(new Action<DateTime, Buffer>(EncodeDateTime));
            if(typeof(T) == typeof(TimeSpan)) return Unsafe.As<Action<T, Buffer>>(new Action<TimeSpan, Buffer>(EncodeTimeSpan));
            if(typeof(T) == typeof(byte[]))   return Unsafe.As<Action<T, Buffer>>(new Action<byte[], Buffer>(EncodeByteArray));
    
            // note: if you need to store live values that can change, store an index to it instead
            // ie: AdaptiveRadixTree<string, class_a> -> AdaptiveRadixTree<string, int> + Dictionary<int, class_a>
                
            return null;
    
            void EncodeString(string key, Buffer res) {
                var count  = Encoding.UTF8.GetByteCount(key);
                res.EnsureCapacity(count);
                res.Length = Encoding.UTF8.GetBytes(key, 0, key.Length, res.Content, 0);
                // could use Encoding.UTF8.GetEncoder().Convert() to avoid GetByteCount()
            }
            void EncodeChar(char key, Buffer res) {
                var item   = new char[1] { (char)key };
                var count  = Encoding.UTF8.GetByteCount(item);
                res.Length = Encoding.UTF8.GetBytes(item, 0, item.Length, res.Content, 0);
            }
            void EncodeInt8(sbyte key, Buffer res) {
                res.Length     = 1;
                res.Content[0] = unchecked((byte)key);
            }
            void EncodeInt16(short key, Buffer res) {
                res.Length     = 2;
                res.Content[0] = unchecked((byte)(key & 0xFF));
                res.Content[1] = unchecked((byte)(key >> 8));
            }
            void EncodeInt32(int key, Buffer res) {
                res.Length = 4;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(key & 0xFF));
                buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
            }
            void EncodeInt64(long key, Buffer res) {
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(key & 0xFF));
                buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((key >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((key >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((key >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((key >> 56) & 0xFF));
            }
            void EncodeUInt8(byte key, Buffer res) {
                res.Length     = 1;
                res.Content[0] = key;
            }
            void EncodeUInt16(ushort key, Buffer res) {
                res.Length     = 2;
                res.Content[0] = unchecked((byte)(key & 0xFF));
                res.Content[1] = unchecked((byte)(key >> 8));
            }
            void EncodeUInt32(uint key, Buffer res) {
                res.Length = 4;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(key & 0xFF));
                buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
            }
            void EncodeUInt64(ulong key, Buffer res) {
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(key & 0xFF));
                buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((key >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((key >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((key >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((key >> 56) & 0xFF));
            }
            void EncodeBool(bool key, Buffer res) {
                res.Length = 1;
                // avoid using LEAF_NODE_KEY_TERMINATOR
                res.Content[0] = LEAF_NODE_KEY_TERMINATOR >= 2 ?
                    (key ? (byte)1 : (byte)0) :
                    (key ? (byte)254 : (byte)255);
            }
            void EncodeFloatLE(float key, Buffer res) {
                res.Length     = 4;
                var buffer     = res.Content;
                var value_uint = new UnionFloat() { Value = key }.Binary;
                buffer[0] = unchecked((byte)(value_uint & 0xFF));
                buffer[1] = unchecked((byte)((value_uint >> 8) & 0xFF));
                buffer[2] = unchecked((byte)((value_uint >> 16) & 0xFF));
                buffer[3] = unchecked((byte)((value_uint >> 24) & 0xFF));
            }
            void EncodeFloat(float key, Buffer res) {
                res.Length = 4;
                var buffer = res.Content;
                var value = BitConverter.GetBytes(key);
                buffer[0] = value[0];
                buffer[1] = value[1];
                buffer[2] = value[2];
                buffer[3] = value[3];
            }
            void EncodeDouble(double key, Buffer res) {
                var item   = unchecked((ulong)BitConverter.DoubleToInt64Bits(key));
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((item >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((item >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((item >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((item >> 56) & 0xFF));
            }
            void EncodeDecimal(decimal key, Buffer res) {
                res.Length = 16;
                var buffer = res.Content;
                var bits   = decimal.GetBits(key);
    
                // technically could be compressed since theres some unused ranges
                // int[3] bits [30-24] and [0-15] are always zero
    
                int bit = bits[0];
                buffer[0] = unchecked((byte)((bit >> 0) & 0xFF));
                buffer[1] = unchecked((byte)((bit >> 8) & 0xFF));
                buffer[2] = unchecked((byte)((bit >> 16) & 0xFF));
                buffer[3] = unchecked((byte)((bit >> 24) & 0xFF));
                bit = bits[1];
                buffer[4] = unchecked((byte)((bit >> 0) & 0xFF));
                buffer[5] = unchecked((byte)((bit >> 8) & 0xFF));
                buffer[6] = unchecked((byte)((bit >> 16) & 0xFF));
                buffer[7] = unchecked((byte)((bit >> 24) & 0xFF));
                bit = bits[2];
                buffer[8] = unchecked((byte)((bit >> 0) & 0xFF));
                buffer[9] = unchecked((byte)((bit >> 8) & 0xFF));
                buffer[10] = unchecked((byte)((bit >> 16) & 0xFF));
                buffer[11] = unchecked((byte)((bit >> 24) & 0xFF));
                bit = bits[3];
                buffer[12] = unchecked((byte)((bit >> 0) & 0xFF));
                buffer[13] = unchecked((byte)((bit >> 8) & 0xFF));
                buffer[14] = unchecked((byte)((bit >> 16) & 0xFF));
                buffer[15] = unchecked((byte)((bit >> 24) & 0xFF));
            }
            void EncodeDateTime(DateTime key, Buffer res) {
                var item   = unchecked((ulong)key.Ticks);
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((item >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((item >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((item >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((item >> 56) & 0xFF));
            }
            void EncodeTimeSpan(TimeSpan key, Buffer res) {
                var item   = unchecked((ulong)key.Ticks);
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((item >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((item >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((item >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((item >> 56) & 0xFF));
            }
            void EncodeByteArray(byte[] key, Buffer res) {
                res.Length = key.Length;
                BlockCopy(key, 0, res.Content, 0, key.Length);
            }
        }
#else
        public static Action<object, Buffer> GetDefaultEncoder<T>() {
            if(typeof(T) == typeof(string))   return EncodeString;
            if(typeof(T) == typeof(char))     return EncodeChar;
            if(typeof(T) == typeof(sbyte))    return EncodeInt8;
            if(typeof(T) == typeof(short))    return EncodeInt16;
            if(typeof(T) == typeof(int))      return EncodeInt32;
            if(typeof(T) == typeof(long))     return EncodeInt64;
            if(typeof(T) == typeof(byte))     return EncodeUInt8;
            if(typeof(T) == typeof(ushort))   return EncodeUInt16;
            if(typeof(T) == typeof(uint))     return EncodeUInt32;
            if(typeof(T) == typeof(ulong))    return EncodeUInt64;
            if(typeof(T) == typeof(bool))     return EncodeBool;
            if(typeof(T) == typeof(float))    return BitConverter.IsLittleEndian ? EncodeFloatLE : (Action<object, Buffer>)EncodeFloat;
            if(typeof(T) == typeof(double))   return EncodeDouble;
            if(typeof(T) == typeof(decimal))  return EncodeDecimal;
            if(typeof(T) == typeof(DateTime)) return EncodeDateTime;
            if(typeof(T) == typeof(TimeSpan)) return EncodeTimeSpan;
            if(typeof(T) == typeof(byte[]))   return EncodeByteArray;
    
            // note: if you need to store live values that can change, store an index to it instead
            // ie: AdaptiveRadixTree<string, class_a> -> AdaptiveRadixTree<string, int> + Dictionary<int, class_a>
                
            return null;
    
            void EncodeString(object key, Buffer res) {
                var item   = (string)key;
                var count  = Encoding.UTF8.GetByteCount(item);
                res.EnsureCapacity(count);
                res.Length = Encoding.UTF8.GetBytes(item, 0, item.Length, res.Content, 0);
                // could use Encoding.UTF8.GetEncoder().Convert() to avoid GetByteCount()
            }
            void EncodeChar(object key, Buffer res) {
                var item   = new char[1] { (char)key };
                var count  = Encoding.UTF8.GetByteCount(item);
                res.Length = Encoding.UTF8.GetBytes(item, 0, item.Length, res.Content, 0);
            }
            void EncodeInt8(object key, Buffer res) {
                var item       = (sbyte)key;
                res.Length     = 1;
                res.Content[0] = unchecked((byte)item);
            }
            void EncodeInt16(object key, Buffer res) {
                var item       = (short)key;
                res.Length     = 2;
                res.Content[0] = unchecked((byte)(item & 0xFF));
                res.Content[1] = unchecked((byte)(item >> 8));
            }
            void EncodeInt32(object key, Buffer res) {
                var item   = (int)key;
                res.Length = 4;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
            }
            void EncodeInt64(object key, Buffer res) {
                var item   = (long)key;
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((item >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((item >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((item >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((item >> 56) & 0xFF));
            }
            void EncodeUInt8(object key, Buffer res) {
                var item       = (byte)key;
                res.Length     = 1;
                res.Content[0] = item;
            }
            void EncodeUInt16(object key, Buffer res) {
                var item       = (ushort)key;
                res.Length     = 2;
                res.Content[0] = unchecked((byte)(item & 0xFF));
                res.Content[1] = unchecked((byte)(item >> 8));
            }
            void EncodeUInt32(object key, Buffer res) {
                var item   = (uint)key;
                res.Length = 4;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
            }
            void EncodeUInt64(object key, Buffer res) {
                var item   = (ulong)key;
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((item >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((item >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((item >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((item >> 56) & 0xFF));
            }
            void EncodeBool(object key, Buffer res) {
                var item   = (bool)key;
                res.Length = 1;
                // avoid using LEAF_NODE_KEY_TERMINATOR
                res.Content[0] = LEAF_NODE_KEY_TERMINATOR >= 2 ?
                    (item ? (byte)1 : (byte)0) :
                    (item ? (byte)254 : (byte)255);
            }
            void EncodeFloatLE(object key, Buffer res) {
                var item       = (float)key;
                res.Length     = 4;
                var buffer     = res.Content;
                var value_uint = new UnionFloat() { Value = item }.Binary;
                buffer[0]      = unchecked((byte)(value_uint & 0xFF));
                buffer[1]      = unchecked((byte)((value_uint >> 8) & 0xFF));
                buffer[2]      = unchecked((byte)((value_uint >> 16) & 0xFF));
                buffer[3]      = unchecked((byte)((value_uint >> 24) & 0xFF));
            }
            void EncodeFloat(object key, Buffer res) {
                var item   = (float)key;
                res.Length = 4;
                var buffer = res.Content;
                var value  = BitConverter.GetBytes(item);
                buffer[0]  = value[0];
                buffer[1]  = value[1];
                buffer[2]  = value[2];
                buffer[3]  = value[3];
            }
            void EncodeDouble(object key, Buffer res) {
                var item   = unchecked((ulong)BitConverter.DoubleToInt64Bits((double)key));
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((item >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((item >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((item >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((item >> 56) & 0xFF));
            }
            void EncodeDecimal(object key, Buffer res) {
                var item   = (decimal)key;
                res.Length = 16;
                var buffer = res.Content;
                var bits   = decimal.GetBits(item);
    
                // technically could be compressed since theres some unused ranges
                // int[3] bits [30-24] and [0-15] are always zero
    
                int bit = bits[0];
                buffer[0] = unchecked((byte)((bit >> 0) & 0xFF));
                buffer[1] = unchecked((byte)((bit >> 8) & 0xFF));
                buffer[2] = unchecked((byte)((bit >> 16) & 0xFF));
                buffer[3] = unchecked((byte)((bit >> 24) & 0xFF));
                bit = bits[1];
                buffer[4] = unchecked((byte)((bit >> 0) & 0xFF));
                buffer[5] = unchecked((byte)((bit >> 8) & 0xFF));
                buffer[6] = unchecked((byte)((bit >> 16) & 0xFF));
                buffer[7] = unchecked((byte)((bit >> 24) & 0xFF));
                bit = bits[2];
                buffer[8] = unchecked((byte)((bit >> 0) & 0xFF));
                buffer[9] = unchecked((byte)((bit >> 8) & 0xFF));
                buffer[10] = unchecked((byte)((bit >> 16) & 0xFF));
                buffer[11] = unchecked((byte)((bit >> 24) & 0xFF));
                bit = bits[3];
                buffer[12] = unchecked((byte)((bit >> 0) & 0xFF));
                buffer[13] = unchecked((byte)((bit >> 8) & 0xFF));
                buffer[14] = unchecked((byte)((bit >> 16) & 0xFF));
                buffer[15] = unchecked((byte)((bit >> 24) & 0xFF));
            }
            void EncodeDateTime(object key, Buffer res) {
                var item   = unchecked((ulong)((DateTime)key).Ticks);
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((item >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((item >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((item >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((item >> 56) & 0xFF));
            }
            void EncodeTimeSpan(object key, Buffer res) {
                var item   = unchecked((ulong)((TimeSpan)key).Ticks);
                res.Length = 8;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)(item & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
                buffer[4]  = unchecked((byte)((item >> 32) & 0xFF));
                buffer[5]  = unchecked((byte)((item >> 40) & 0xFF));
                buffer[6]  = unchecked((byte)((item >> 48) & 0xFF));
                buffer[7]  = unchecked((byte)((item >> 56) & 0xFF));
            }
            void EncodeByteArray(object key, Buffer res) {
                var item   = (byte[])key;
                res.Length = item.Length;
                BlockCopy(item, 0, res.Content, 0, item.Length);
            }
        }
#endif
        [StructLayout(LayoutKind.Explicit)]
        private struct UnionFloat {
            [FieldOffset(0)] public float Value; // only works with BitConverter.IsLittleEndian
            [FieldOffset(0)] public uint Binary;
        }
        #endregion
        #region public static GetDefaultDecoder<T>()
#if USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE
        public static Func<byte[], int, int, T> GetDefaultDecoder<T>() {
            if(typeof(T) == typeof(string))   return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, string>(DecodeString));
            if(typeof(T) == typeof(char))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, char>(DecodeChar));
            if(typeof(T) == typeof(sbyte))    return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, sbyte>(DecodeInt8));
            if(typeof(T) == typeof(short))    return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, short>(DecodeInt16));
            if(typeof(T) == typeof(int))      return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, int>(DecodeInt32));
            if(typeof(T) == typeof(long))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, long>(DecodeInt64));
            if(typeof(T) == typeof(byte))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, byte>(DecodeUInt8));
            if(typeof(T) == typeof(ushort))   return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, ushort>(DecodeUInt16));
            if(typeof(T) == typeof(uint))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, uint>(DecodeUInt32));
            if(typeof(T) == typeof(ulong))    return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, ulong>(DecodeUInt64));
            if(typeof(T) == typeof(bool))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, bool>(DecodeBool));
            if(typeof(T) == typeof(float))    return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, float>(BitConverter.IsLittleEndian ? DecodeFloatLE : (Func<byte[], int, int, float>)DecodeFloat));
            if(typeof(T) == typeof(double))   return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, double>(DecodeDouble));
            if(typeof(T) == typeof(decimal))  return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, decimal>(DecodeDecimal));
            if(typeof(T) == typeof(DateTime)) return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, DateTime>(DecodeDateTime));
            if(typeof(T) == typeof(TimeSpan)) return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, TimeSpan>(DecodeTimeSpan));
            if(typeof(T) == typeof(byte[]))   return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, byte[]>(DecodeByteArray));
    
            // note: if you need to store live values that can change, store an index to it instead
            // ie: AdaptiveRadixTree<string, class_a> -> AdaptiveRadixTree<string, int> + Dictionary<int, class_a>
                
            return null;
    
            string DecodeString(byte[] buffer, int start, int len) {
                return Encoding.UTF8.GetString(buffer, start, len);
            }
            char DecodeChar(byte[] buffer, int start, int len) {
                var temp = new char[1];
                Encoding.UTF8.GetChars(buffer, start, len, temp, 0);
                return temp[0];
            }
            sbyte DecodeInt8(byte[] buffer, int start, int len) {
                return unchecked((sbyte)buffer[start]);
            }
            short DecodeInt16(byte[] buffer, int start, int len) {
                return unchecked((short)(
                    buffer[start + 0] |
                    (buffer[start + 1] << 8)));
            }
            int DecodeInt32(byte[] buffer, int start, int len) {
                return unchecked(
                    buffer[start + 0] |
                    (buffer[start + 1] << 8) |
                    (buffer[start + 2] << 16) |
                    (buffer[start + 3] << 24));
            }
            long DecodeInt64(byte[] buffer, int start, int len) {
                return unchecked(
                    buffer[start + 0] |
                    ((long)buffer[start + 1] << 8) |
                    ((long)buffer[start + 2] << 16) |
                    ((long)buffer[start + 3] << 24) |
                    ((long)buffer[start + 4] << 32) |
                    ((long)buffer[start + 5] << 40) |
                    ((long)buffer[start + 6] << 48) |
                    ((long)buffer[start + 7] << 56));
            }
            byte DecodeUInt8(byte[] buffer, int start, int len) {
                return buffer[start];
            }
            ushort DecodeUInt16(byte[] buffer, int start, int len) {
                return unchecked((ushort)(
                    buffer[start + 0] |
                    (buffer[start + 1] << 8)));
            }
            uint DecodeUInt32(byte[] buffer, int start, int len) {
                return unchecked(
                    buffer[start + 0] |
                    ((uint)buffer[start + 1] << 8) |
                    ((uint)buffer[start + 2] << 16) |
                    ((uint)buffer[start + 3] << 24));
            }
            ulong DecodeUInt64(byte[] buffer, int start, int len) {
                return unchecked(
                    buffer[start + 0] |
                    ((ulong)buffer[start + 1] << 8) |
                    ((ulong)buffer[start + 2] << 16) |
                    ((ulong)buffer[start + 3] << 24) |
                    ((ulong)buffer[start + 4] << 32) |
                    ((ulong)buffer[start + 5] << 40) |
                    ((ulong)buffer[start + 6] << 48) |
                    ((ulong)buffer[start + 7] << 56));
            }
            bool DecodeBool(byte[] buffer, int start, int len) {
                var b = buffer[start];
                // avoid using LEAF_NODE_KEY_TERMINATOR
                return LEAF_NODE_KEY_TERMINATOR >= 2 ?
                    b != 0 :
                    b != 255;
            }
            float DecodeFloatLE(byte[] buffer, int start, int len) {
                var value_uint = unchecked(
                    buffer[start + 0] |
                    ((uint)buffer[start + 1] << 8) |
                    ((uint)buffer[start + 2] << 16) |
                    ((uint)buffer[start + 3] << 24));
    
                return new UnionFloat() { Binary = value_uint }.Value;
            }
            float DecodeFloat(byte[] buffer, int start, int len) {
                return BitConverter.ToSingle(buffer, start);
            }
            double DecodeDouble(byte[] buffer, int start, int len) {
                return BitConverter.Int64BitsToDouble(unchecked(
                    buffer[start + 0] |
                    ((long)buffer[start + 1] << 8) |
                    ((long)buffer[start + 2] << 16) |
                    ((long)buffer[start + 3] << 24) |
                    ((long)buffer[start + 4] << 32) |
                    ((long)buffer[start + 5] << 40) |
                    ((long)buffer[start + 6] << 48) |
                    ((long)buffer[start + 7] << 56) ));
            }
            decimal DecodeDecimal(byte[] buffer, int start, int len) {
                var bits = new int[4];
    
                bits[0] =
                    (buffer[start + 0] << 0) |
                    (buffer[start + 1] << 8) |
                    (buffer[start + 2] << 16) |
                    (buffer[start + 3] << 24);
                bits[1] =
                    (buffer[start + 4] << 0) |
                    (buffer[start + 5] << 8) |
                    (buffer[start + 6] << 16) |
                    (buffer[start + 7] << 24);
                bits[2] =
                    (buffer[start + 8] << 0) |
                    (buffer[start + 9] << 8) |
                    (buffer[start + 10] << 16) |
                    (buffer[start + 11] << 24);
                bits[3] =
                    (buffer[start + 12] << 0) |
                    (buffer[start + 13] << 8) |
                    (buffer[start + 14] << 16) |
                    (buffer[start + 15] << 24);
    
                return new decimal(bits);
            }
            DateTime DecodeDateTime(byte[] buffer, int start, int len) {
                return new DateTime(unchecked(
                    buffer[start + 0] |
                    ((long)buffer[start + 1] << 8) |
                    ((long)buffer[start + 2] << 16) |
                    ((long)buffer[start + 3] << 24) |
                    ((long)buffer[start + 4] << 32) |
                    ((long)buffer[start + 5] << 40) |
                    ((long)buffer[start + 6] << 48) |
                    ((long)buffer[start + 7] << 56)));
            }
            TimeSpan DecodeTimeSpan(byte[] buffer, int start, int len) {
                return new TimeSpan(unchecked(
                    buffer[start + 0] |
                    ((long)buffer[start + 1] << 8) |
                    ((long)buffer[start + 2] << 16) |
                    ((long)buffer[start + 3] << 24) |
                    ((long)buffer[start + 4] << 32) |
                    ((long)buffer[start + 5] << 40) |
                    ((long)buffer[start + 6] << 48) |
                    ((long)buffer[start + 7] << 56)));
            }
            byte[] DecodeByteArray(byte[] buffer, int start, int len) {
                var res = new byte[len];
                BlockCopy(buffer, start, res, 0, len);
                return res;
            }
        }
#else
        public static Func<byte[], int, int, object> GetDefaultDecoder<T>() {
            if(typeof(T) == typeof(string))   return DecodeString;
            if(typeof(T) == typeof(char))     return DecodeChar;
            if(typeof(T) == typeof(sbyte))    return DecodeInt8;
            if(typeof(T) == typeof(short))    return DecodeInt16;
            if(typeof(T) == typeof(int))      return DecodeInt32;
            if(typeof(T) == typeof(long))     return DecodeInt64;
            if(typeof(T) == typeof(byte))     return DecodeUInt8;
            if(typeof(T) == typeof(ushort))   return DecodeUInt16;
            if(typeof(T) == typeof(uint))     return DecodeUInt32;
            if(typeof(T) == typeof(ulong))    return DecodeUInt64;
            if(typeof(T) == typeof(bool))     return DecodeBool;
            if(typeof(T) == typeof(float))    return BitConverter.IsLittleEndian ? DecodeFloatLE : (Func<byte[], int, int, object>)DecodeFloat;
            if(typeof(T) == typeof(double))   return DecodeDouble;
            if(typeof(T) == typeof(decimal))  return DecodeDecimal;
            if(typeof(T) == typeof(DateTime)) return DecodeDateTime;
            if(typeof(T) == typeof(TimeSpan)) return DecodeTimeSpan;
            if(typeof(T) == typeof(byte[]))   return DecodeByteArray;
    
            // note: if you need to store live values that can change, store an index to it instead
            // ie: AdaptiveRadixTree<string, class_a> -> AdaptiveRadixTree<string, int> + Dictionary<int, class_a>
                
            return null;
    
            object DecodeString(byte[] buffer, int start, int len) {
                return Encoding.UTF8.GetString(buffer, start, len);
            }
            object DecodeChar(byte[] buffer, int start, int len) {
                var temp = new char[1];
                Encoding.UTF8.GetChars(buffer, start, len, temp, 0);
                return temp[0];
            }
            object DecodeInt8(byte[] buffer, int start, int len) {
                return unchecked((sbyte)buffer[start]);
            }
            object DecodeInt16(byte[] buffer, int start, int len) {
                return unchecked((short)(
                    buffer[start + 0] |
                    (buffer[start + 1] << 8)));
            }
            object DecodeInt32(byte[] buffer, int start, int len) {
                return unchecked(
                    buffer[start + 0] |
                    (buffer[start + 1] << 8) |
                    (buffer[start + 2] << 16) |
                    (buffer[start + 3] << 24));
            }
            object DecodeInt64(byte[] buffer, int start, int len) {
                return unchecked(
                    buffer[start + 0] |
                    ((long)buffer[start + 1] << 8) |
                    ((long)buffer[start + 2] << 16) |
                    ((long)buffer[start + 3] << 24) |
                    ((long)buffer[start + 4] << 32) |
                    ((long)buffer[start + 5] << 40) |
                    ((long)buffer[start + 6] << 48) |
                    ((long)buffer[start + 7] << 56));
            }
            object DecodeUInt8(byte[] buffer, int start, int len) {
                return buffer[start];
            }
            object DecodeUInt16(byte[] buffer, int start, int len) {
                return unchecked((ushort)(
                    buffer[start + 0] |
                    (buffer[start + 1] << 8)));
            }
            object DecodeUInt32(byte[] buffer, int start, int len) {
                return unchecked(
                    buffer[start + 0] |
                    ((uint)buffer[start + 1] << 8) |
                    ((uint)buffer[start + 2] << 16) |
                    ((uint)buffer[start + 3] << 24));
            }
            object DecodeUInt64(byte[] buffer, int start, int len) {
                return unchecked(
                    buffer[start + 0] |
                    ((ulong)buffer[start + 1] << 8) |
                    ((ulong)buffer[start + 2] << 16) |
                    ((ulong)buffer[start + 3] << 24) |
                    ((ulong)buffer[start + 4] << 32) |
                    ((ulong)buffer[start + 5] << 40) |
                    ((ulong)buffer[start + 6] << 48) |
                    ((ulong)buffer[start + 7] << 56));
            }
            object DecodeBool(byte[] buffer, int start, int len) {
                var b = buffer[start];
                // avoid using LEAF_NODE_KEY_TERMINATOR
                return LEAF_NODE_KEY_TERMINATOR >= 2 ?
                    b != 0 :
                    b != 255;
            }
            object DecodeFloatLE(byte[] buffer, int start, int len) {
                var value_uint = unchecked(
                    buffer[start + 0] |
                    ((uint)buffer[start + 1] << 8) |
                    ((uint)buffer[start + 2] << 16) |
                    ((uint)buffer[start + 3] << 24));
    
                return new UnionFloat() { Binary = value_uint }.Value;
            }
            object DecodeFloat(byte[] buffer, int start, int len) {
                return BitConverter.ToSingle(buffer, start);
            }
            object DecodeDouble(byte[] buffer, int start, int len) {
                return BitConverter.Int64BitsToDouble(unchecked(
                    buffer[start + 0] |
                    ((long)buffer[start + 1] << 8) |
                    ((long)buffer[start + 2] << 16) |
                    ((long)buffer[start + 3] << 24) |
                    ((long)buffer[start + 4] << 32) |
                    ((long)buffer[start + 5] << 40) |
                    ((long)buffer[start + 6] << 48) |
                    ((long)buffer[start + 7] << 56) ));
            }
            object DecodeDecimal(byte[] buffer, int start, int len) {
                var bits = new int[4];
    
                bits[0] =
                    (buffer[start + 0] << 0) |
                    (buffer[start + 1] << 8) |
                    (buffer[start + 2] << 16) |
                    (buffer[start + 3] << 24);
                bits[1] =
                    (buffer[start + 4] << 0) |
                    (buffer[start + 5] << 8) |
                    (buffer[start + 6] << 16) |
                    (buffer[start + 7] << 24);
                bits[2] =
                    (buffer[start + 8] << 0) |
                    (buffer[start + 9] << 8) |
                    (buffer[start + 10] << 16) |
                    (buffer[start + 11] << 24);
                bits[3] =
                    (buffer[start + 12] << 0) |
                    (buffer[start + 13] << 8) |
                    (buffer[start + 14] << 16) |
                    (buffer[start + 15] << 24);
    
                return new decimal(bits);
            }
            object DecodeDateTime(byte[] buffer, int start, int len) {
                return new DateTime(unchecked(
                    buffer[start + 0] |
                    ((long)buffer[start + 1] << 8) |
                    ((long)buffer[start + 2] << 16) |
                    ((long)buffer[start + 3] << 24) |
                    ((long)buffer[start + 4] << 32) |
                    ((long)buffer[start + 5] << 40) |
                    ((long)buffer[start + 6] << 48) |
                    ((long)buffer[start + 7] << 56)));
            }
            object DecodeTimeSpan(byte[] buffer, int start, int len) {
                return new TimeSpan(unchecked(
                    buffer[start + 0] |
                    ((long)buffer[start + 1] << 8) |
                    ((long)buffer[start + 2] << 16) |
                    ((long)buffer[start + 3] << 24) |
                    ((long)buffer[start + 4] << 32) |
                    ((long)buffer[start + 5] << 40) |
                    ((long)buffer[start + 6] << 48) |
                    ((long)buffer[start + 7] << 56)));
            }
            object DecodeByteArray(byte[] buffer, int start, int len) {
                var res = new byte[len];
                BlockCopy(buffer, start, res, 0, len);
                return res;
            }
        }
#endif
        #endregion
        #region public class Buffer
        public sealed class Buffer {
            private const int DEFAULT_CAPACITY = 32;
    
            public byte[] Content;
            public int Length;
    
            public Buffer(int capacity = DEFAULT_CAPACITY) : this(new byte[capacity]){ }
            public Buffer(byte[] buffer) {
                this.Content = buffer ?? throw new ArgumentNullException(nameof(buffer));
            }
    
            /// <summary>
            ///     Ensures the buffer can contain the capacity requested.
            /// </summary>
            public void EnsureCapacity(int capacity) {
                if(this.Content.Length < capacity)
                    Array.Resize(ref this.Content, capacity);
            }
    
            public TKey GetKey(AdaptiveRadixTree<TKey, TValue> owner) {
                UnescapeLeafKeyTerminator(this.Content, 0, ref this.Length);
                return (TKey)owner.m_keyDecoder(this.Content, 0, this.Length);
            }
            internal TKey GetPartialKey(AdaptiveRadixTree<TKey, TValue> owner, ref int length) {
                UnescapeLeafKeyTerminator(this.Content, 0, ref this.Length, ref length);
                return (TKey)owner.m_keyDecoder(this.Content, 0, length);
            }
            public TValue GetValue(AdaptiveRadixTree<TKey, TValue> owner) {
                return (TValue)owner.m_valueDecoder(this.Content, 0, this.Length);
            }
            public KeyValuePair<TKey, TValue> GetItem(AdaptiveRadixTree<TKey, TValue> owner) {
                return new KeyValuePair<TKey, TValue>(this.GetKey(owner), this.GetValue(owner));
            }
        }
        #endregion
        #region private static EscapeLeafKeyTerminator()
        /// <summary>
        ///     Escapes LEAF_NODE_KEY_TERMINATOR
        ///     Call this whenever KeyEncoder might generate a LEAF_NODE_KEY_TERMINATOR
        ///     This is expected to be only used by KeyEncoder  (and not valueencoder).
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static void EscapeLeafKeyTerminator(Buffer result) {
            // LEAF_NODE_KEY_ESCAPE_CHAR = LEAF_NODE_KEY_ESCAPE_CHAR + LEAF_NODE_KEY_ESCAPE_CHAR
            // LEAF_NODE_KEY_TERMINATOR  = LEAF_NODE_KEY_ESCAPE_CHAR + LEAF_NODE_KEY_ESCAPE_CHAR2
    
            int length    = result.Length;
            int readIndex = 0;
            var buffer    = result.Content;
            while(length > 0) {
                var index = new ReadOnlySpan<byte>(buffer, readIndex, length).IndexOfAny(LEAF_NODE_KEY_TERMINATOR, LEAF_NODE_KEY_ESCAPE_CHAR);
    
                if(index < 0)
                    break;
    
                readIndex += index;
                length    -= index;
    
                var c = buffer[readIndex];
                if(c == LEAF_NODE_KEY_TERMINATOR)
                    buffer[readIndex] = LEAF_NODE_KEY_ESCAPE_CHAR;
                readIndex++;
    
                if(result.Length == buffer.Length) {
                    result.EnsureCapacity(result.Length * 2);
                    buffer = result.Content;
                }
    
                BlockCopy(buffer, readIndex, buffer, readIndex + 1, length);
    
                buffer[readIndex++] = c == LEAF_NODE_KEY_TERMINATOR ? LEAF_NODE_KEY_ESCAPE_CHAR2 : LEAF_NODE_KEY_ESCAPE_CHAR;
    
                result.Length++;
                length--;
            }
        }
        #endregion
        #region private static UnescapeLeafKeyTerminator()
        /// <summary>
        ///     Unescapes LEAF_NODE_KEY_TERMINATOR
        ///     Call this whenever KeyEncoder called EscapeLeafKeyTerminator()
        ///     This is expected to be only used by KeyDecoder (and not valueDecoder).
        ///     Returns the number of missing bytes to complete current.
        /// </summary>
        [MethodImpl(AggressiveInlining)]
        private static void UnescapeLeafKeyTerminator(byte[] buffer, int start, ref int len) {
            // intentionally copy, were basically ignoring this value
            int stopAt = len;
    
            UnescapeLeafKeyTerminator(buffer, start, ref len, ref stopAt);
        }
        /// <summary>
        ///     Unescapes LEAF_NODE_KEY_TERMINATOR
        ///     Call this whenever KeyEncoder called EscapeLeafKeyTerminator()
        ///     This is expected to be only used by KeyDecoder (and not valueDecoder).
        ///     Returns the number of missing bytes to complete current.
        /// </summary>
        /// <param name="stopAt">Should be smaller or equal to len. Specifies the stopping point, but allows it to increase up to len if it ends on an encoded character.</param>
        [MethodImpl(AggressiveInlining)]
        private static void UnescapeLeafKeyTerminator(byte[] buffer, int start, ref int len, ref int stopAt) {
            // note: cannot contain LEAF_NODE_KEY_TERMINATOR anywhere, so dont check
            // LEAF_NODE_KEY_ESCAPE_CHAR + LEAF_NODE_KEY_ESCAPE_CHAR  = LEAF_NODE_KEY_ESCAPE_CHAR
            // LEAF_NODE_KEY_ESCAPE_CHAR + LEAF_NODE_KEY_ESCAPE_CHAR2 = LEAF_NODE_KEY_TERMINATOR
    
            int length     = stopAt;
            int readIndex  = start;
            int writeIndex = start;
            while(length > 0) {
                var index = new ReadOnlySpan<byte>(buffer, readIndex, length).IndexOf(LEAF_NODE_KEY_ESCAPE_CHAR);
    
                if(readIndex < writeIndex)
                    BlockCopy(buffer, readIndex, buffer, writeIndex, index >= 0 ? index : length + (len - stopAt));
    
                if(index < 0)
                    break;
    
                readIndex  += index + 1;
                writeIndex += index;
                length     -= index + 1;
    
                // missing escaped character
                if(length <= 0 && stopAt <= len)
                    throw new FormatException("invalid escaping sequence.");
    
                var escapeSequence = buffer[readIndex++];
    
                if(escapeSequence == LEAF_NODE_KEY_ESCAPE_CHAR2)
                    buffer[writeIndex++] = LEAF_NODE_KEY_TERMINATOR;
                else if(escapeSequence == LEAF_NODE_KEY_ESCAPE_CHAR)
                    buffer[writeIndex++] = LEAF_NODE_KEY_ESCAPE_CHAR;
                else
                    throw new FormatException("invalid escaping sequence.");
    
                len--;
                stopAt--;
    
                if(len <= 0)
                    return;
            }
        }
        #endregion
            
        protected internal enum NodeType : byte { // ordering matters, and so does values
            Node4   = 1,
            Node8   = 2,
            Node16  = 3,
            Node32  = 4,
            Node64  = 5,
            Node128 = 6,
            Node256 = 7,
            Leaf    = 255,
        }
    
        #region protected struct NodePointer
        protected readonly struct NodePointer : IEquatable<NodePointer> {
            /// <summary>
            ///     The location of the pointer itself.
            /// </summary>
            public readonly long Address;
            /// <summary>
            ///     The location this pointer points towards.
            /// </summary>
            public readonly long Target;
    
            public NodePointer(long address, long target) : this() {
                this.Address = address;
                this.Target  = target;
            }
    
            public override int GetHashCode() {
                return (this.Address, this.Target).GetHashCode();
            }
    
            public bool Equals(NodePointer other) {
                return other.Address == this.Address && other.Target == this.Target;
            }
            public override bool Equals(object obj) {
                if(!(obj is NodePointer))
                    return false;
                return this.Equals((NodePointer)obj);
            }
    
            public override string ToString() {
                return $"(@{this.Address}) -> {this.Target}";
            }
        }
        #endregion
        #region private class FixedSizeMemoryManager
        /// <summary>
        ///     A very basic MemoryManager that runs in O(1).
        ///     Only allocates objects of a fixed size.
        ///     Automatic upsize and downsize memory usage.
        /// </summary>
        private sealed class FixedSizeMemoryManager {
            private const int SIZE = 8192 / sizeof(long);
    
            public readonly int AllocSize;
            public readonly int PreAllocChunk;
    
            private readonly long[] m_available = new long[SIZE];
            private int m_availableCount;
    
            private readonly Func<long, long> m_alloc;
            private readonly Action<long, long> m_free;
    
            public int TotalFree => m_availableCount * this.AllocSize;
    
            public FixedSizeMemoryManager(int allocSize, Func<long, long> alloc, Action<long, long> free) {
                this.AllocSize     = allocSize;
                this.PreAllocChunk = Math.Min(Math.Max(4096 / allocSize, 8), SIZE);
                m_alloc            = alloc;
                m_free             = free;
            }
    
            /// <summary>
            ///     O(1)
            /// </summary>
            public long Alloc() {
                if(m_availableCount > 0)
                    return m_available[--m_availableCount];
    
                // pre-alloc if we ran out
                var size = this.PreAllocChunk * this.AllocSize;
                var pos  = m_alloc(size);
                pos += size;
                for(int i = 0; i < this.PreAllocChunk; i++) {
                    pos -= this.AllocSize;
                    m_available[m_availableCount++] = pos;
                }
                return m_available[--m_availableCount];
            }
            /// <summary>
            ///     O(1)
            /// </summary>
            public void Free(long position) {
                // if we freed too many, then clear half
                if(m_availableCount == SIZE) {
                    // since we do want to downsize the stream, we favor releasing the furthest positions first
                    // this also helps cache locality
                    Array.Sort(m_available);
    
                    // group by adjacent
                    long start     = -1;
                    long end       = 0;
                    int startIndex = SIZE / 2;
    
                    for(int i = startIndex; i < SIZE; i++) {
                        var item = m_available[i];
                        if(start < 0) {
                            start = item;
                            end   = item;
                        } else if(item != end) {
                            m_free(start, end - start);
                            start = item;
                            end   = item;
                        }
                        end += this.AllocSize;
                    }
                    if(start >= 0)
                        m_free(start, end - start);
                    m_availableCount = startIndex;
                }
    
                m_available[m_availableCount++] = position;
            }
            /// <summary>
            ///     O(1)
            /// </summary>
            public void Clear() {
                m_availableCount = 0;
            }
        }
        #endregion
    
        #region explicit interface(s) implementations
        void ICollection.CopyTo(Array array, int index) {
            foreach(var kvp in this.Items)
                array.SetValue(kvp, index++);
        }
        IEnumerator IEnumerable.GetEnumerator() {
            return this.Items.GetEnumerator();
        }
    
        object ICollection.SyncRoot => this; // very cheap
        bool ICollection.IsSynchronized => false;
    
#if IMPLEMENT_DICTIONARY_INTERFACES
        void IDictionary<TKey, TValue>.Add(TKey key, TValue value) {
            this.Add(in key, in value);
        }
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) {
            this.Add(item.Key, item.Value);
        }
    
        bool IDictionary<TKey, TValue>.Remove(TKey key) {
            return this.Remove(in key);
        }
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) {
            if(!this.Contains(item.Key, item.Value))
                return false;
    
            return this.Remove(item.Key);
        }
    
        ICollection<TKey> IDictionary<TKey, TValue>.Keys {
            get {
                // ugh, should make the ICollection lazy loaded
                return this.Keys.ToList();
            }
        }
            
        ICollection<TValue> IDictionary<TKey, TValue>.Values {
            get {
                // ugh, should make the ICollection lazy loaded
                return this.Values.ToList();
            }
        }
            
        TValue IDictionary<TKey, TValue>.this[TKey key] {
            get => this[in key];
            set => this[in key] = value;
        }
        TValue IReadOnlyDictionary<TKey, TValue>.this[TKey key] {
            get => this[in key];
        }
    
        bool IDictionary<TKey, TValue>.TryGetValue(TKey key, out TValue value) {
            return this.TryGetValue(in key, out value);
        }
        bool IReadOnlyDictionary<TKey, TValue>.TryGetValue(TKey key, out TValue value) {
            return this.TryGetValue(in key, out value);
        }
    
        bool IDictionary<TKey, TValue>.ContainsKey(TKey key) {
            return this.ContainsKey(in key);
        }
        bool IReadOnlyDictionary<TKey, TValue>.ContainsKey(TKey key) {
            return this.ContainsKey(in key);
        }
        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) {
            return this.Contains(item.Key, item.Value);
        }
    
        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) {
            foreach(var kvp in this.Items)
                array[arrayIndex++] = kvp;
        }
    
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
    
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() {
            return this.Items.GetEnumerator();
        }
#endif
        #endregion
    }
    
    public enum SearchOption {
        /// <summary>
        ///     Only results matching the pattern length will be returned.
        /// </summary>
        ExactMatch,
        /// <summary>
        ///     All results starting with the pattern length will be returned.
        ///     Basically results are allowed to be longer than the pattern.
        /// </summary>
        StartsWith,
    }
    
    
    public class AdaptiveRadixTreeTest {
        #region public static GenerateTestKeys()
        /// <param name="max_random_entries_per_character">ex: 3, key='ABCD', result='ABBCCCD'</param>
        public static IEnumerable<string> GenerateTestKeys(long count, int max_random_entries_per_character = 3, uint seed = 0xBADC0FFE) {
            var random = new Random(unchecked((int)seed));
            for(long i = 0; i < count; i++) {
                var key = ChangeBase(i);
    
                if(max_random_entries_per_character <= 1)
                    yield return key;
                else {
                    var sb = new StringBuilder();
                    for(int j = 0; j < key.Length; j++) {
                        var c = key[j];
                        var dupes = j + 1 < key.Length && c == key[j + 1] ?
                            // consecutive character repeats have special rules in order to ensure they are unique
                            max_random_entries_per_character :
                            random.Next(max_random_entries_per_character) + 1;
                        sb.Append(c, dupes);
                    }
                    yield return sb.ToString();
                }
            }
        }
        #endregion
        #region private static ChangeBase()
        private static string ChangeBase(long value, string new_base = "ABCDEFGHIJKLMNOPQRSTUVWXYZ") {
            var current = Math.Abs(value);
    
            if(current >= 0 && current < new_base.Length) {
                return value >= 0 ?
                    new string(new_base[unchecked((int)value)], 1) :
                    new string(new char[2] { '-', new_base[unchecked((int)value)] });
            }
    
            char[] res;
            int new_base_size = new_base.Length;
            var size          = unchecked((int)Math.Ceiling(Math.Log(current + 1, new_base_size)));
                
            if(value > 0)
                res = new char[size];
            else {
                res = new char[size + 1];
                res[0] = '-';
            }
    
            int index = res.Length;
    
            do {
                res[--index] = new_base[unchecked((int)(current % new_base_size))];
                current     /= new_base_size;
            } while(current > 0);
    
            return new string(res);
        }
        #endregion
    }
}