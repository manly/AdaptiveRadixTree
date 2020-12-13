// performance optimisation
// see IMPLEMENTATION NOTES for details
#define USE_UNCOMMITTED_WRITES

#if DEBUG
// enable only for debugging
//#define THROW_WHEN_WRITING_ON_EVICTED_NODE
#endif

using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.Specialized 
{
    /// <summary>
    ///     Not thread safe.
    ///     Allocates managed memory using an in-place AVL tree.
    ///     This allows flexible size allocs, but free()s have to free all of a previous alloc (ie: cant free() fractions of allocs).
    /// 
    ///     This class is best suited for allocs of 16+ bytes, and especially good for var-sized structs.
    ///     
    ///     * All alloc() incur 8 bytes overhead.
    ///     * Any free() less than Node.SIZEOF (~24 bytes) will not be usable memory until adjacent memory is free.
    /// 
    ///     log(n) worst case alloc.
    ///     log(n) worst case free.
    /// </summary>
    /// <remarks>
    ///     Motivation:
    ///     64 bits references in .NET have a 16 bytes overhead, as well as memory alignments of 8 bytes.
    ///     Consequently, the smallest class instance possible takes 24 bytes of memory.
    ///     When doing a lot of small allocs, this adds up quickly.
    ///     32 bits references have 8 bytes overhead, with 4 bytes alignment, resulting in 12 bytes being the smallest alloc possible.
    /// </remarks>
    public sealed class AvlTreeMemoryAllocator {
        #region ENCODING FORMAT
        // FORMAT
        // ==============================
        // We have to use in-place structures within the memory, since that speeds up cache lines.
        // This happens to be well suited since we can use the Address of alloc data as part of our AVL tree without having the Address take memory.
        //
        // MEMORY CHUNK
        // **********************************************************
        //
        //     <------------memory chunk------->            MemorySegments
        //     [0][1]////////////////////////[2]            [0] used (0 bytes)  * always there
        //                                                  [1] free
        //                                                  [2] used (0 bytes)  * always there
        //
        //     always mark start/end of MemoryChunk as used memory, in order to make sure they never get moved/merged, 
        //     as that complicates the code and makes it slowed due to extra checks
        //
        // MEMORY SEGMENTS
        // **********************************************************
        //
        //     <------------memory chunk------->            example
        //        segment       segment
        //     [0][1]///////////[2]//////////[3]            [0,20]<20 bytes free data>[20,30]<30 bytes free data>
        //     
        //     MemorySegment (header) struct:
        //        * int  PrevBytes              number of bytes in previous memory segment
        //        * int  NextBytes              number of bytes in current memory segment
        //        * bool IsFree                 true = free memory, false = used memory
        //        * bool HasPrecedingFreeBytes  if true, means the byte pre-pending the MemorySegment will contain 
        //                                      the size of freed bytes preceding the segment, including the PrecedingFreeBytes itself.
        //                                      This value can only be true if IsFree=false and preceding MemorySegment.IsFree=false
        //     
        //     acts like doubly linked list, for efficient free()ing/alloc()ing nearby segments merging
        //
        //     All alloc() will incur MemorySegmentHeader.SIZEOF bytes reserved
        //
        // USED MEMORY SEGMENT
        // **********************************************************
        // 
        //     Returned MemoryHandles/Ptr will point directly to where the allocated memory begins
        //
        //     i.e.:
        //     [0,12]////////****[12,0]
        //                      ^ [byte] PrecedingFreeBytes = 4
        //                   ^ 4 free bytes
        //           ^ 8 allocated bytes
        //
        // FREE MEMORY SEGMENT
        // **********************************************************
        // 
        //     [header]/////////////
        //             [1]
        //
        //     [1] is a AVL-tree node
        //     the remainder of the memory is free and unused
        //
        //     AvlTreeNode struct:
        //        * MemoryHandle ParentNode
        //        * MemoryHandle LeftNode
        //        * MemoryHandle RightNode
        //        * 2_bits       AvlState
        //
        //     AVL trees order themselves by the size of free memory, thus allowing a quick log(n) lookup of the first section with n available bytes.
        //
        //     If the AvlTreeNode cannot fit in the available space of the free node, then that free memory cannot be used/allocated to
        //     until an adjacent MemorySegment is free()ed.
        //
        //     MemoryHandles do point across MemoryChunks, and arent limited per .NET consecutively allocated bytes.
        //     This allows a "global" AvlTree that spans across all reserved memory.
        //
        // AVL ROOT NODE
        // **********************************************************
        // 
        //     AVL tree needs a header/root node that isn't taking space itself.
        //     This will always be in the first MemorySegment of the first MemoryChunk.
        #endregion
        #region IMPLEMENTATION NOTES
        // There are multiple ways to implement this code, all with different challenges:
        //
        //    1- write unsafe code  (ie: read/writes nodes directly to/from storage)
        //       this means casting directly byte[] -> NodeStruct, and "NodeStruct.Balance = x" would directly write to storage
        //       if you do this, remove AvlNodeMRUCache since you would just read/write directly the memory
        //
        //    2- run a cache that maps addresses with Node instances and do writes only on evicts
        //       ie: Dictionary<address, Node> 
        //       USE_UNCOMMITTED_WRITES uses this strategy
        //       this strategy maintains efficiency (by using cached node instances) and minimizes writes to storage (only done upon evicts)
        //       the downside is that you can't support non-memory storage as Node property writes are uncommitted (until evicted)
        //
        //       if you want to support non-memory storage (like say, use memory mapped files and be able to reload this later on)
        //       then you need to commit all writes. in short, all Node property writes must also write to storage.
        //       
        //    3- don't use cache
        //       this is a lot less efficient as that means every Node property accesses must read/write from storage
        //       you *cannot* do committed writes only (every Node property write is written to storage). 
        //       you must absolutely also do reads from storage on every Node property reads.
        //
        //       in other words, you cannot rely on your AvlTree Nodes being already loaded (so they have instances of each other)
        //       and skip reading from storage because logically you would expect them to contain the up-to-date data.
        //       this would fail in insiduous ways because when you call MemoryAllocator.Free(), you load the Node *directly*, 
        //       without loading the tree leading up to it. this can and will result in loading a partial tree to remove from the AvlTree
        //       and thus you would write to new instances of Nodes that are at the same Address as the ones loaded from your AvlTree that starts from root.
        //       think of it this way: your AvlTree Nodes would not read/be aware of new changes following the MemoryAllocator.Free(), with the storage
        //       containing different data than your Nodes.
        //
        //       if you take this approach, check AvlCompare() and change it to compare addresses instead of pointers
        //
        //       those issues are solved if you use caching dict<address, node>, which is option #2.
        //       
        //       if you want to support non-memory storage, then this would work immediately.
        //
        // About NODE_CACHE_CAPACITY
        // 
        //    This value must be >= 256. 
        //    
        //    NODE_CACHE_CAPACITY >= avl_tree_height * 3 + extra_nodes_read_on_rebalance
        //    extra_nodes_read_on_rebalance = unknown max
        //
        //    AVL tree height:   Math.Log(n + 2, 1.6180339887) - 1.3277       max possible height of the AVL tree (binary tree)
        //                           29 for 1M   items
        //                           34 for 10M  items
        //                           ...
        //                           64 for 30 000 000 000 000 items
        //                           ...
        //                           256 for 10^53 items
        // 
        //    The NODE_CACHE_CAPACITY must be >= to the max number of nodes read for one operation. This includes:
        //       - the path to a node (avl_tree_height)
        //       - the left/right nodes for each node in the path (hence the * 3)
        //       - the extra nodes read for rebalancing
        //
        //    While I do not have a good estimate for accessed nodes on a rebalance, instead I opted for simply choosing safe margins.
        //    
        //    Any value smaller than that run the risk of a alloc()/free() using a copy of an evicted node, which would result in uncommitted writes.
        //
        //    THROW_WHEN_WRITING_ON_EVICTED_NODE will detect that case
        #endregion

        private const int NODE_CACHE_CAPACITY = 256;
        private const int AVL_TREE_NODE_ADDRESS = MemorySegmentHeader.SIZEOF;

        private MemoryChunk[] m_chunks = new MemoryChunk[4];
        private int m_chunkCount = 0;

        private AvlTree m_avl;
        private readonly AvlNodeMRUCache m_cache;

        #region constructors
        static AvlTreeMemoryAllocator() {
            if(NODE_CACHE_CAPACITY < 256)
                throw new ArgumentOutOfRangeException(nameof(NODE_CACHE_CAPACITY), "The value must be >= 256. See implementation notes for details.");
        }
        public AvlTreeMemoryAllocator(int capacity = 4096) {
            m_cache          = new AvlNodeMRUCache(this, NODE_CACHE_CAPACITY);
            m_avl            = new AvlTree(this);
            var memory_chunk = this.ExpandCapacityWithoutAvlNode(capacity - MIN_POSSIBLE_CHUNK_SIZE); // this will make the chunk match the capacity requested
            // add avl node
            m_avl.Add(
                new MemoryPtr(memory_chunk.ChunkID, memory_chunk.FreeMemoryAddress),
                memory_chunk.FreeMemorySize);
        }
        #endregion

        #region Alloc()
        /// <summary>
        /// log(n) worst case
        /// Allocates n bytes and returns a pointer to it.
        /// The returned memory is not zeroed out.
        /// </summary>
        public PtrExtended Alloc(int size) {
            var bsr = m_avl.BinarySearch_GreaterOrEqualTo(size);

            // this will return the closest match to allocate to
            // (ie: the smallest available memory >= size)
            if(bsr.Diff <= 0) {
                var memory_chunk = m_chunks[bsr.Node.Address.ChunkID];

                this.ChangeMemorySegmentFromFreeToUsed(
                    memory_chunk,
                    bsr.Node.Address.ChunkID,
                    bsr.Node.Address.Address,
                    size,
                    true);

                return new PtrExtended(
                    new Ptr(bsr.Node.Address.ChunkID, bsr.Node.Address.Address),
                    memory_chunk.Memory);
            } else {
                // no available memory exists that is >= size
                var memory_chunk = this.ExpandCapacityWithoutAvlNode(size);
                // creates the AVL node too
                this.ChangeMemorySegmentFromFreeToUsed(memory_chunk.Chunk, memory_chunk.ChunkID, memory_chunk.FreeMemoryAddress, size, false);

                return new PtrExtended(
                    new Ptr(memory_chunk.ChunkID, memory_chunk.FreeMemoryAddress),
                    memory_chunk.Chunk.Memory);
            }
        }
        private void ChangeMemorySegmentFromFreeToUsed(MemoryChunk chunk, int chunkID, int free_mem_address, int alloc_size, bool remove_from_avl) {
            // if were allocating memory, it means we are in this case:  [used][free][used]
            var header_address = free_mem_address - MemorySegmentHeader.SIZEOF;
            var header         = MemorySegmentHeader.ReadWithoutPrecedingBytes(chunk.Memory, header_address);
            header.IsFree      = false;

            // if there was enough space for AVL node, then remove from tree
            if(remove_from_avl && header.NextBytes >= Node.SIZEOF)
                m_avl.Remove(new MemoryPtr(chunkID, free_mem_address), chunk.Memory);

            var remaining = header.NextBytes - alloc_size;
            if(remaining >= MemorySegmentHeader.SIZEOF) {
                var next_address = free_mem_address + header.NextBytes;

                header.NextBytes = alloc_size;
                header.Write(chunk.Memory, header_address);

                var new_free_address = free_mem_address + alloc_size;
                var new_free_size    = remaining - MemorySegmentHeader.SIZEOF;
                var new_free = new MemorySegmentHeader(){
                    IsFree             = true,
                    PrevBytes          = alloc_size,
                    NextBytes          = new_free_size,
                    PrecedingFreeBytes = 0,
                };
                new_free.Write(chunk.Memory, new_free_address);

                MemorySegmentHeader.WritePrevBytes(chunk.Memory, next_address, new_free_size);

                // if theres enough space, then add this new free space in AVL tree
                if(new_free_size >= Node.SIZEOF) {
                    var ptr = new MemoryPtr(chunkID, new_free_address + MemorySegmentHeader.SIZEOF);
                    m_avl.Add(ptr, new_free_size);
                }
            } else { 
                // if there is no space for a MemorySegmentHeader for the free memory
                header.Write(chunk.Memory, header_address);
                MemorySegmentHeader.WritePrecedingFreeBytes(chunk.Memory, free_mem_address + header.NextBytes, unchecked((byte)remaining));
            }
        }
        #endregion
        #region Free()
        /// <summary>
        /// log(n) worst case
        /// Frees previously allocated memory.
        /// </summary>
        public void Free(in Ptr memoryHandle) {
            var mem = this.GetMemory(memoryHandle);
            this.ChangeMemorySegmentFromUsedToFree(memoryHandle, mem);
        }
        /// <summary>
        /// log(n) worst case
        /// Frees previously allocated memory.
        /// </summary>
        public void Free(in PtrExtended memoryHandle) {
            this.ChangeMemorySegmentFromUsedToFree(memoryHandle.Ptr, memoryHandle.Memory);
        }
        private void ChangeMemorySegmentFromUsedToFree(in Ptr memoryHandle, byte[] memory) {
            var header_address = memoryHandle.Address - MemorySegmentHeader.SIZEOF;
            var header         = MemorySegmentHeader.Read(memory, header_address);
            
            System.Diagnostics.Debug.Assert(!header.IsFree);
            
            header.IsFree = true;

            var prev_header_address = header_address - header.PrevBytes - MemorySegmentHeader.SIZEOF;
            var next_header_address = memoryHandle.Address + header.NextBytes;
            var prev_header         = MemorySegmentHeader.ReadWithoutPrecedingBytes(memory, prev_header_address);
            var next_header         = MemorySegmentHeader.Read(memory, next_header_address);

            // remove avl nodes
            if(prev_header.IsFree && prev_header.NextBytes >= Node.SIZEOF)
                m_avl.Remove(new MemoryPtr(memoryHandle.ChunkID, prev_header_address + MemorySegmentHeader.SIZEOF), memory);
            if(next_header.IsFree && next_header.NextBytes >= Node.SIZEOF)
                m_avl.Remove(new MemoryPtr(memoryHandle.ChunkID, next_header_address + MemorySegmentHeader.SIZEOF), memory);

            int avl_address;
            int avl_size;

            if(!prev_header.IsFree && !next_header.IsFree) {
                // case [used][used->free][used]
                var prev_dist         = header.PrevBytes - header.PrecedingFreeBytes;
                prev_header.NextBytes = prev_dist;
                prev_header.WriteNextBytes(memory, prev_header_address);

                header.PrevBytes          = prev_dist;
                header.NextBytes         += header.PrecedingFreeBytes; // + next_header.PrecedingFreeBytes (already counted)
                header_address           -= header.PrecedingFreeBytes;
                header.PrecedingFreeBytes = 0;
                header.Write(memory, header_address);

                next_header.PrevBytes          = header.NextBytes;
                next_header.PrecedingFreeBytes = 0;
                next_header.Write(memory, next_header_address);

                avl_address = header_address + MemorySegmentHeader.SIZEOF;
                avl_size    = header.NextBytes;
            } else if(!prev_header.IsFree && next_header.IsFree) {
                // case [used][used->free][free]{used}

                var new_free_memory = header.PrecedingFreeBytes + header.NextBytes + MemorySegmentHeader.SIZEOF + next_header.NextBytes;

                var prev_dist         = header.PrevBytes - header.PrecedingFreeBytes;
                prev_header.NextBytes = prev_dist;
                prev_header.WriteNextBytes(memory, prev_header_address);

                header_address           -= header.PrecedingFreeBytes;
                header.PrecedingFreeBytes = 0;
                header.PrevBytes          = prev_dist;
                header.NextBytes          = new_free_memory;
                header.Write(memory, header_address);

                var next_next_header_address = next_header_address + MemorySegmentHeader.SIZEOF + next_header.NextBytes;
                var next_next_header         = MemorySegmentHeader.ReadWithoutPrecedingBytes(memory, next_next_header_address);

                next_next_header.PrevBytes = new_free_memory;
                next_next_header.WritePrevBytes(memory, next_next_header_address);

                avl_address = header_address + MemorySegmentHeader.SIZEOF;
                avl_size    = new_free_memory;
            } else if(prev_header.IsFree && !next_header.IsFree) {
                // case {used}[free][used->free][used]

                var new_free_memory = prev_header.NextBytes + MemorySegmentHeader.SIZEOF + header.NextBytes;
                
                prev_header.NextBytes = new_free_memory;
                prev_header.WriteNextBytes(memory, prev_header_address);

                next_header.PrecedingFreeBytes = 0;
                next_header.PrevBytes          = new_free_memory;
                next_header.Write(memory, next_header_address);

                avl_address = prev_header_address + MemorySegmentHeader.SIZEOF;
                avl_size    = new_free_memory;
            } else { // prev_header.IsFree && next_header.IsFree
                // case {used}[free][used->free][free]{used}

                var new_free_memory = prev_header.NextBytes + MemorySegmentHeader.SIZEOF + header.NextBytes + MemorySegmentHeader.SIZEOF + next_header.NextBytes;

                prev_header.NextBytes = new_free_memory;
                prev_header.WriteNextBytes(memory, prev_header_address);

                var next_next_header_address = next_header_address + MemorySegmentHeader.SIZEOF + next_header.NextBytes;
                var next_next_header         = MemorySegmentHeader.ReadWithoutPrecedingBytes(memory, next_next_header_address);

                next_next_header.PrevBytes = new_free_memory;
                next_next_header.WritePrevBytes(memory, next_next_header_address);

                avl_address = prev_header_address + MemorySegmentHeader.SIZEOF;
                avl_size    = new_free_memory;
            }

            if(avl_size >= Node.SIZEOF) {
                var ptr = new MemoryPtr(memoryHandle.ChunkID, avl_address);
                m_avl.Add(ptr, avl_size);
            }
        }
        #endregion

        #region GetMemory()
        /// <summary>
        /// Returns the memory backing the ptr.
        /// </summary>
        public byte[] GetMemory(in Ptr memoryHandle) {
            return m_chunks[memoryHandle.ChunkID].Memory;
        }
        #endregion

        #region private ExpandCapacityWithoutAvlNode()
        /// <summary>
        /// Creates a new MemoryChunk that can accomodate at least the requested alloc size.
        /// The new available memory does not have an AVL node created for it.
        /// </summary>
        private NewMemoryChunkAlloc ExpandCapacityWithoutAvlNode(int alloc_size) {
            var new_memory_chunk_size = this.CalculateNewChunkSize(alloc_size);

            var chunkID          = m_chunkCount++;
            var memory           = new byte[new_memory_chunk_size];
            var new_memory_chunk = new MemoryChunk(memory);
            var free_memory      = this.InitEmptyMemoryChunk(memory, chunkID); // doesnt create an avl node
            
            if(m_chunks.Length == chunkID)
                Array.Resize(ref m_chunks, m_chunks.Length * 2);
            
            m_chunks[chunkID] = new_memory_chunk;

            return new NewMemoryChunkAlloc(){
                Chunk             = new_memory_chunk,
                ChunkID           = chunkID,
                FreeMemoryAddress = free_memory.FreeMemoryAddress,
                FreeMemorySize    = free_memory.FreeMemorySize,
            };
        }
        private struct NewMemoryChunkAlloc {
            public MemoryChunk Chunk;
            public int ChunkID;
            public int FreeMemoryAddress;
            public int FreeMemorySize;
        }
        /// <summary>
        /// Writes an empty memory chunk, without adding an AVLNODE.
        /// The entire memory will be marked as free.
        /// </summary>
        private InitEmptyMemoryChunkResult InitEmptyMemoryChunk(byte[] chunk, int chunkID) {
            bool include_avl_tree_root = chunkID == 0;

            // special segment
            var first_segment = new MemorySegmentHeader() {
                IsFree             = false,
                PrevBytes          = 0,
                NextBytes          = !include_avl_tree_root ? 0 : Node.SIZEOF,
                PrecedingFreeBytes = 0,
            };
            first_segment.Write(chunk, 0);

            if(include_avl_tree_root)
                m_avl.SetHeaderNode(new MemoryPtr(chunkID, AVL_TREE_NODE_ADDRESS), chunk);

            var last_segment_address = chunk.Length - MemorySegmentHeader.SIZEOF;
            var free_memory_size     = last_segment_address - MemorySegmentHeader.SIZEOF - first_segment.NextBytes - MemorySegmentHeader.SIZEOF;

            // mark all as free memory
            var segment_free = new MemorySegmentHeader() {
                IsFree             = true,
                PrevBytes          = first_segment.NextBytes,
                NextBytes          = free_memory_size,
                PrecedingFreeBytes = 0,
            };
            segment_free.Write(chunk, MemorySegmentHeader.SIZEOF + first_segment.NextBytes);

            // special segment
            var last_segment = new MemorySegmentHeader() {
                IsFree             = false,
                PrevBytes          = free_memory_size,
                NextBytes          = 0,
                PrecedingFreeBytes = 0,
            };
            last_segment.Write(chunk, last_segment_address);

            return new InitEmptyMemoryChunkResult() {
                FreeMemoryAddress = MemorySegmentHeader.SIZEOF + first_segment.NextBytes + MemorySegmentHeader.SIZEOF,
                FreeMemorySize    = free_memory_size,
            };
        }
        private struct InitEmptyMemoryChunkResult {
            public int FreeMemoryAddress;
            public int FreeMemorySize;
        }
        #endregion
        #region private CalculateNewChunkSize()
        private const int DEFAULT_MIN_CHUNK_SIZE = 4096;
        private const int DEFAULT_MAX_CHUNK_SIZE = 16777216; // 16MB

        private const int MIN_POSSIBLE_CHUNK_SIZE = 3 * MemorySegmentHeader.SIZEOF;

        private const int DEFAULT_MIN_CHUNK_SIZE_SHIFT = // this looks horrible, but gets compiled into the proper const
            DEFAULT_MIN_CHUNK_SIZE == (1 << 12) ? 12 : // 4096
            DEFAULT_MIN_CHUNK_SIZE == (1 << 13) ? 13 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 14) ? 14 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 15) ? 15 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 16) ? 16 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 17) ? 17 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 18) ? 18 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 19) ? 19 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 20) ? 20 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 21) ? 21 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 22) ? 22 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 23) ? 23 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 24) ? 24 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 25) ? 25 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 26) ? 26 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 27) ? 27 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 28) ? 28 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 29) ? 29 :
            DEFAULT_MIN_CHUNK_SIZE == (1 << 30) ? 30 : int.MinValue;

        private int CalculateNewChunkSize(int request) {
            var min_possible_chunk = request + MIN_POSSIBLE_CHUNK_SIZE;
            var suggested = Math.Min(
                DEFAULT_MIN_CHUNK_SIZE << Math.Min(m_chunkCount, 30 - DEFAULT_MIN_CHUNK_SIZE_SHIFT),
                DEFAULT_MAX_CHUNK_SIZE);
            return Math.Max(suggested, min_possible_chunk);
        }
        #endregion


        #region static Test()
        public static void Test(int loops, int seed = unchecked((int)0xBADC0FFE)) {
            int sequence  = 0;
            var random    = new Random(seed);
            var allocator = new AvlTreeMemoryAllocator();
            var reference = new Dictionary<int, (PtrExtended ptr, int size)>();

            var start = DateTime.UtcNow;

            for(int i = 0; i < loops; i++) {
                if(i % 100000 == 0) {
                    Console.WriteLine($"{i} {DateTime.UtcNow - start}");
                    if(!Verify())
                        System.Diagnostics.Debugger.Break();
                }

                var rng = random.NextDouble();
                if(rng <= 0.60) {
                    var size = random.Next(4, 252);
                    var ptr = allocator.Alloc(size);
                    reference.Add(sequence, (ptr, size));
                    Encode(ptr, sequence++, size);
                } else if(reference.Count > 0) {
                    var rng_key = random.Next(0, sequence);
                    if(reference.TryGetValue(rng_key, out var ptr)) {
                        reference.Remove(rng_key);
                        allocator.Free(ptr.ptr.Ptr);
                    }
                }
            }

            Console.WriteLine($"{loops} {DateTime.UtcNow - start}");

            bool Verify() {
                var mem_layout = allocator.GetMemoryLayout();
                foreach(var layout in mem_layout.Layout)
                    if(layout.Error != null)
                        return false;
                foreach(var item in reference) {
                    var read_value = Decode(item.Value.ptr, item.Value.size);
                    if(read_value != item.Key)
                        return false;
                }
                //// TEMPORARY CHECK if wrongful cache address specified
                //foreach(var layout in mem_layout.Layout)
                //    if(layout.Type == MemoryLayoutItemType.MemorySegmentHeader && layout.SegmentHeader.IsFree && layout.Owner.m_cache.TryGetValue(new MemoryPtr(layout.ChunkID, layout.Address), out _))
                //        return false;
                return true;
            }
            void Encode(in PtrExtended ptr, int value, int size) {
                var buffer = ptr.Memory;
                buffer[ptr.Ptr.Address + 0] = (byte)((value >> 0) & 0xFF);
                buffer[ptr.Ptr.Address + 1] = (byte)((value >> 8) & 0xFF);
                buffer[ptr.Ptr.Address + 2] = (byte)((value >> 16) & 0xFF);
                buffer[ptr.Ptr.Address + 3] = (byte)((value >> 24) & 0xFF);
                for(int i = 4; i < size; i++)
                    buffer[ptr.Ptr.Address + i] = (byte)(i % 256);
            }
            int Decode(in PtrExtended ptr, int size) {
                var buffer = ptr.Memory;
                var res =
                    (buffer[ptr.Ptr.Address + 0] << 0) |
                    (buffer[ptr.Ptr.Address + 1] << 8) |
                    (buffer[ptr.Ptr.Address + 2] << 16) |
                    (buffer[ptr.Ptr.Address + 3] << 24);
                for(int i = 4; i < size; i++)
                    if(buffer[ptr.Ptr.Address + i] != (byte)(i % 256))
                        return -1;
                return res;
            }
        }
        #endregion

        #region PrintMemoryLayout()
        /// <summary>
        /// Returns the memory layout.
        /// Intended for debugging purposes.
        /// </summary>
        public string PrintMemoryLayout() {
            var sb = new System.Text.StringBuilder();
            var layout = this.GetMemoryLayout();

            sb.AppendLine(layout.Metrics.ToString());
            sb.AppendLine();

            var errors = layout.Layout.Count(o => o.Error != null);
            if(errors > 0) {
                sb.AppendLine($"Detected {errors} errors.");
                sb.AppendLine();
            }

            for(int i = 0; i < layout.Layout.Count; i++) {
                var item = layout.Layout[i];
                sb.AppendLine(item.ToString());
                if(item.Error != null)
                    sb.AppendLine($"ERROR: {item.Error}");

                if(item.Type == MemoryLayoutItemType.MemorySegmentHeader && !item.SegmentHeader.IsFree)
                    this.DumpAllocatedItemMemory(sb, layout, i);
            }

            return sb.ToString();
        }

        private void DumpAllocatedItemMemory(Text.StringBuilder sb, MemoryLayoutResult layout, int i) {
            try {
                var item = layout.Layout[i];
                var mem_size = item.SegmentHeader.NextBytes -
                    (layout.Layout.Skip(i + 1).FirstOrDefault(o => o.Type == MemoryLayoutItemType.MemorySegmentHeader)?.SegmentHeader.PrecedingFreeBytes ?? 0);
                if(mem_size > 0) {
                    sb.Append("   ");
                    var chunk = m_chunks[item.ChunkID].Memory;
                    for(int j = 0; j < mem_size; j++) {
                        if(j > 0 && j % 4 == 0)
                            sb.Append(' ');
                        if(j > 0 && j % 16 == 0)
                            sb.Append(' ');
                        if(j > 0 && j % 32 == 0) {
                            sb.AppendLine();
                            sb.Append("   ");
                        }
                        var b = chunk[item.Address + MemorySegmentHeader.SIZEOF + j];
                        sb.Append(b.ToString("X2"));
                    }
                    sb.AppendLine();
                }
            } catch(Exception ex) {
                sb.AppendLine(ex.ToString());
            }
        }
        #endregion
        #region private GetMemoryLayout()
        /// <summary>
        /// Returns the memory layout.
        /// Intended for debugging purposes.
        /// </summary>
        private MemoryLayoutResult GetMemoryLayout() { 
            var items = this.GetInternalMemoryLayout().ToList();

            var res = new MemoryLayoutResult(){
                Layout  = items,
                Metrics = new MemoryLayoutMetrics(){
                    Capacity = m_chunks.Take(m_chunkCount).Sum(o => o.Memory.Length),
                }
            };

            foreach(var item in items) {
                switch(item.Type) {
                    case MemoryLayoutItemType.MemorySegmentHeader:
                        if(item.SegmentHeader.IsFree) {
                            res.Metrics.MemorySegmentHeaderFreeCount++;
                            res.Metrics.TotalAvailableMemory += item.SegmentHeader.NextBytes;
                        } else {
                            res.Metrics.MemorySegmentHeaderUsedCount++;
                            res.Metrics.TotalUsedMemory += item.SegmentHeader.NextBytes;
                        }
                        break;
                    case MemoryLayoutItemType.AvlNode:
                        res.Metrics.AvlTreeNodes++;
                        break;
                    case MemoryLayoutItemType.AvlNodeTooSmall:
                        break;
                    case MemoryLayoutItemType.MemoryChunk: break;
                    default:
                        throw new NotImplementedException();
                }
            }

            res.Metrics.MemorySegmentHeaderSizes = (res.Metrics.MemorySegmentHeaderFreeCount + res.Metrics.MemorySegmentHeaderUsedCount) * MemorySegmentHeader.SIZEOF;
            res.Metrics.AvlTreeNodesSize         = res.Metrics.AvlTreeNodes * Node.SIZEOF;

            this.ValidateMemoryLayout(res);

            return res;
        }
        private IEnumerable<MemoryLayoutItem> GetInternalMemoryLayout() {
            for(int i = 0; i < m_chunkCount; i++) {
                var mem     = m_chunks[i];
                int address = 0;
                int max     = mem.Memory.Length;

                yield return new MemoryLayoutItem() {
                    Owner     = this,
                    Type      = MemoryLayoutItemType.MemoryChunk,
                    ChunkID   = i,
                    ChunkSize = max,
                    Address   = address,
                };

                while(address < max) {
                    if(max - address < MemorySegmentHeader.SIZEOF) {
                        yield return new MemoryLayoutItem() {
                            Owner         = this,
                            Type          = MemoryLayoutItemType.MemorySegmentHeader,
                            ChunkID       = i,
                            ChunkSize     = max,
                            Address       = address,
                            SegmentHeader = default,
                            Error         = "MemorySegmentHeader cannot be read; not enough space remaining",
                        };
                        break;
                    }

                    var memory_segment_header = MemorySegmentHeader.Read(mem.Memory, address);
                    
                    yield return new MemoryLayoutItem() {
                        Owner         = this,
                        Type          = MemoryLayoutItemType.MemorySegmentHeader,
                        ChunkID       = i,
                        ChunkSize     = max,
                        Address       = address,
                        SegmentHeader = memory_segment_header,
                    };

                    address += MemorySegmentHeader.SIZEOF;

                    if(memory_segment_header.IsFree) {
                        if(memory_segment_header.NextBytes >= Node.SIZEOF){
                            var avl_node = new Node(this, new MemoryPtr(i, address));
                            avl_node.Read(mem.Memory);

                            yield return new MemoryLayoutItem() {
                                Owner         = this,
                                Type          = MemoryLayoutItemType.AvlNode,
                                ChunkID       = i,
                                ChunkSize     = max,
                                Address       = address,
                                AvlNode       = avl_node,
                                SegmentHeader = memory_segment_header,
                            };
                        } else {
                            yield return new MemoryLayoutItem() {
                                Owner         = this,
                                Type          = MemoryLayoutItemType.AvlNodeTooSmall,
                                ChunkID       = i,
                                ChunkSize     = max,
                                Address       = address,
                                SegmentHeader = memory_segment_header,
                            };
                        }
                    } else if(i == 0 && address == AVL_TREE_NODE_ADDRESS) {
                        var avl_node = new Node(this, new MemoryPtr(i, address));
                        avl_node.Read(mem.Memory);

                        yield return new MemoryLayoutItem() {
                            Owner         = this,
                            Type          = MemoryLayoutItemType.AvlNode,
                            ChunkID       = i,
                            ChunkSize     = max,
                            Address       = address,
                            AvlNode       = avl_node,
                            IsRootNode    = true,
                            SegmentHeader = memory_segment_header,
                        };
                    }

                    //if(!memory_segment_header.IsFree)
                    address += memory_segment_header.NextBytes;
                }
            }
        }
        private class MemoryLayoutResult {
            public List<MemoryLayoutItem> Layout;
            public MemoryLayoutMetrics Metrics;
        }
        private class MemoryLayoutMetrics {
            public long Capacity;
            public int AvlTreeNodes;
            public long AvlTreeNodesSize;             // excluding MemorySegmentHeaders
            public long MemorySegmentHeaderSizes;
            public long MemorySegmentHeaderFreeCount;
            public long MemorySegmentHeaderUsedCount;
            public long TotalAvailableMemory;         // excluding MemorySegmentHeaders
            public long TotalUsedMemory;              // excluding MemorySegmentHeaders

            public override string ToString() {
                var sb = new System.Text.StringBuilder();

                sb.AppendLine("METRICS");
                sb.AppendLine("========");
                foreach(var field in this.GetType().GetFields())
                    sb.AppendLine($"{field.Name}: {field.GetValue(this)}");

                return sb.ToString();
            }
        }
        private enum MemoryLayoutItemType {
            MemoryChunk,
            MemorySegmentHeader,
            AvlNodeTooSmall,
            AvlNode,
        }
        private class MemoryLayoutItem {
            public AvlTreeMemoryAllocator Owner;
            public int ChunkID;
            public int ChunkSize;
            public MemoryLayoutItemType Type;
            public MemorySegmentHeader SegmentHeader;
            public Node AvlNode;
            public bool IsRootNode;
            public int Address;
            public string Error;

            public override string ToString() {
                switch(this.Type) {
                    case MemoryLayoutItemType.MemoryChunk:
                        return string.Format("{1}MANAGED MEMORY CHUNK ID #{0} ({2} bytes){1}==========================", this.ChunkID, Environment.NewLine, this.ChunkSize);
                    case MemoryLayoutItemType.MemorySegmentHeader:
                        string preceding_free_bytes = this.SegmentHeader.PrecedingFreeBytes == 0 ? 
                            null :
                            string.Format("[chunk:{0} @{1}] {2} preceding free bytes{3}", this.ChunkID, this.Address, this.SegmentHeader.PrecedingFreeBytes, Environment.NewLine);
                        return string.Format("{2}[chunk:{0} @{1}  prev:{3} next:{4}] {5}", this.ChunkID, this.Address, preceding_free_bytes, this.SegmentHeader.PrevBytes, this.SegmentHeader.NextBytes, this.SegmentHeader.IsFree ? "FREE MEMORY" : "USED MEMORY");
                    case MemoryLayoutItemType.AvlNodeTooSmall:
                        return string.Format("   [@{1}] AVLNODE TOO SMALL", this.ChunkID, this.Address);
                    case MemoryLayoutItemType.AvlNode:
                        var ptr = new MemoryPtr(this.ChunkID, this.Address);
                        string dump = string.Format("   [@{1}] NON-CACHED {5}AVLNODE left:{2} right:{3} parent:{4} balance:{6}", this.ChunkID, this.Address, ToString(this.AvlNode.LeftPtr), ToString(this.AvlNode.RightPtr), ToString(this.AvlNode.ParentPtr), this.IsRootNode ? "ROOT " : "", this.AvlNode.Balance.ToString());

                        if(this.Owner.m_cache.TryGetValue(ptr, out var cached_node))
                            dump = string.Format("   [@{1}] CACHED     {5}AVLNODE left:{2} right:{3} parent:{4} balance:{6}\r\n{7}", this.ChunkID, this.Address, ToString(cached_node.LeftPtr), ToString(cached_node.RightPtr), ToString(cached_node.ParentPtr), this.IsRootNode ? "ROOT " : "", cached_node.Balance.ToString(), dump);

                        return dump;
                    default:
                        throw new NotImplementedException();
                }
                string ToString(MemoryPtr ptr) {
                    if(ptr.ChunkID < 0)
                        return "[null]";
                    return $"[{ptr.ChunkID} @{ptr.Address}]";
                }
            }
            public void AddError(string error) {
                if(this.Error == null)
                    this.Error = error;
                else
                    this.Error += Environment.NewLine + error;
            }
        }
        #endregion
        #region private ValidateMemoryLayout()
        private void ValidateMemoryLayout(MemoryLayoutResult memory_layout) {
            var chunks = memory_layout.Layout
                .GroupBy(o => o.ChunkID);

            foreach(var chunk in chunks) {
                var items = chunk.Where(o => o.Type == MemoryLayoutItemType.MemorySegmentHeader).ToList();

                for(int i = 1; i < items.Count; i++) {
                    var current = items[i - 1];
                    var next    = items[i];

                    if(current.SegmentHeader.NextBytes != next.SegmentHeader.PrevBytes)
                        current.AddError("mismatch between current.NextBytes and next.PrevBytes");
                    if(next.Address != current.Address + current.SegmentHeader.NextBytes + MemorySegmentHeader.SIZEOF)
                        next.AddError("mismatch between current.Address and prev.Address + prev.NextBytes + MemorySegmentHeader.SIZEOF");
                    if(current.SegmentHeader.IsFree && next.SegmentHeader.IsFree)
                        current.AddError("current.IsFree && next.IsFree. The 2 segments should be merged.");
                    if(current.SegmentHeader.IsFree && current.SegmentHeader.PrecedingFreeBytes != 0)
                        current.AddError("current.IsFree && current.PrecedingFreeBytes != 0. PrecendingFreeBytes is only allowed on Used memory.");
                    if(current.SegmentHeader.PrevBytes < 0)
                        current.AddError("invalid prevbytes");
                    if(current.SegmentHeader.NextBytes < 0)
                        current.AddError("invalid nextbytes");
                    if(!current.SegmentHeader.IsFree && current.SegmentHeader.NextBytes == 0 && i > 1)
                        current.AddError("invalid reserved memory -- reserved 0 bytes");
                    if(current.SegmentHeader.PrecedingFreeBytes > 0 && current.SegmentHeader.PrevBytes <= current.SegmentHeader.PrecedingFreeBytes)
                        current.AddError("illogical case; current.PrecedingFreeBytes > current.PrevBytes");
                }
            }

            // verify avl node potentially here
        }
        #endregion

        /// <summary>
        /// MemoryHandle / Pointer
        /// </summary>
        public readonly struct Ptr : IEquatable<Ptr> {
            internal readonly int ChunkID;
            public readonly int Address;

            #region constructors
            internal Ptr(int chunkID, int address) : this() {
                this.ChunkID = chunkID;
                this.Address = address;
            }
            #endregion

            #region static Read()
            public static Ptr Read(byte[] buffer, int index) {
                var chunkId = 
                    (buffer[index + 0] << 0) |
                    (buffer[index + 1] << 8) |
                    (buffer[index + 2] << 16) |
                    (buffer[index + 3] << 24);
                var address = 
                    (buffer[index + 4] << 0) |
                    (buffer[index + 5] << 8) |
                    (buffer[index + 6] << 16) |
                    (buffer[index + 7] << 24);
                return new Ptr(chunkId, address);
            }
            #endregion
            #region Write()
            public void Write(byte[] buffer, int index) {
                buffer[index + 0] = unchecked((byte)((this.ChunkID >> 0) & 0xFF));
                buffer[index + 1] = unchecked((byte)((this.ChunkID >> 8) & 0xFF));
                buffer[index + 2] = unchecked((byte)((this.ChunkID >> 16) & 0xFF));
                buffer[index + 3] = unchecked((byte)((this.ChunkID >> 24) & 0xFF));
                buffer[index + 4] = unchecked((byte)((this.Address >> 0) & 0xFF));
                buffer[index + 5] = unchecked((byte)((this.Address >> 8) & 0xFF));
                buffer[index + 6] = unchecked((byte)((this.Address >> 16) & 0xFF));
                buffer[index + 7] = unchecked((byte)((this.Address >> 24) & 0xFF));
            }
            #endregion

            #region Equals()
            public bool Equals(Ptr other) {
                return this.Address == other.Address && this.ChunkID == other.ChunkID;
            }
            public override bool Equals(object obj) {
                if(obj is Ptr x)
                    return this.Equals(x);
                return false;
            }

            public static bool operator ==(Ptr x, Ptr y) {
                return x.Equals(y);
            }
            public static bool operator !=(Ptr x, Ptr y) {
                return !(x == y);
            }
            #endregion
            #region GetHashCode()
            public override int GetHashCode() {
                return (this.Address, this.ChunkID).GetHashCode();
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return string.Format("[{0}] {1}",
                    this.ChunkID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    this.Address.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            #endregion
        }
        /// <summary>
        /// MemoryHandle / Pointer and its associated memory.
        /// </summary>
        public readonly struct PtrExtended : IEquatable<PtrExtended> {
            public readonly Ptr Ptr;
            public readonly byte[] Memory;

            #region constructors
            internal PtrExtended(Ptr memoryHandle, byte[] memory) : this() {
                this.Ptr    = memoryHandle;
                this.Memory = memory;
            }
            #endregion

            #region Equals()
            public bool Equals(PtrExtended other) {
                return this.Ptr == other.Ptr; // && this.Memory == other.Memory;
            }
            public override bool Equals(object obj) {
                if(obj is PtrExtended x)
                    return this.Equals(x);
                return false;
            }

            public static bool operator ==(PtrExtended x, PtrExtended y) {
                return x.Equals(y);
            }
            public static bool operator !=(PtrExtended x, PtrExtended y) {
                return !(x == y);
            }
            #endregion
            #region GetHashCode()
            public override int GetHashCode() {
                return (this.Ptr).GetHashCode(); // , this.Memory
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return this.Ptr.ToString();
            }
            #endregion
        }

        #region private struct MemoryChunk
        private readonly struct MemoryChunk {
            public readonly byte[] Memory;
            public MemoryChunk(byte[] memory) : this() {
                this.Memory = memory;
            }
        }
        #endregion
        #region private struct MemorySegmentHeader
        private struct MemorySegmentHeader {
            /// <summary>
            /// The "sizeof(this)" in the sense of "if I am at the end of a MemorySegmentHeader, how many bytes do I need to go back to read it?"
            /// This is closer to the storage space taken when serialized, excluding the optional extended pre-pended bytes.
            /// </summary>
            public const int SIZEOF = 8;

            public int PrevBytes;
            public int NextBytes;
            public bool IsFree;
            public byte PrecedingFreeBytes;

            public static MemorySegmentHeader Read(byte[] buffer, int index) {
                var prev =
                    (buffer[index + 0] << 0) |
                    (buffer[index + 1] << 8) |
                    (buffer[index + 2] << 16) |
                    (buffer[index + 3] << 24);
                var next =
                    (buffer[index + 4] << 0) |
                    (buffer[index + 5] << 8) |
                    (buffer[index + 6] << 16) |
                    (buffer[index + 7] << 24);
                return new MemorySegmentHeader(){
                    PrevBytes          = prev & 0x7FFF_FFFF,
                    NextBytes          = next & 0x7FFF_FFFF,
                    IsFree             = (prev & 0x8000_0000) != 0,
                    PrecedingFreeBytes = (next & 0x8000_0000) == 0 ? (byte)0 : buffer[index - 1],
                };
            }
            public static MemorySegmentHeader ReadWithoutPrecedingBytes(byte[] buffer, int index) {
                var prev =
                    (buffer[index + 0] << 0) |
                    (buffer[index + 1] << 8) |
                    (buffer[index + 2] << 16) |
                    (buffer[index + 3] << 24);
                var next =
                    (buffer[index + 4] << 0) |
                    (buffer[index + 5] << 8) |
                    (buffer[index + 6] << 16) |
                    (buffer[index + 7] << 24);
                return new MemorySegmentHeader(){
                    PrevBytes          = prev & 0x7FFF_FFFF,
                    NextBytes          = next & 0x7FFF_FFFF,
                    IsFree             = (prev & 0x8000_0000) != 0,
                    PrecedingFreeBytes = (next & 0x8000_0000) == 0 ? (byte)0 : (byte)1,
                };
            }
            public void Write(byte[] buffer, int index) {
                var prevBytes = unchecked(this.PrevBytes | (this.IsFree ? (int)0x8000_0000 : 0));
                var nextBytes = unchecked(this.NextBytes | (this.PrecedingFreeBytes != 0 ? (int)0x8000_0000 : 0));

                buffer[index + 0] = unchecked((byte)((prevBytes >> 0) & 0xFF));
                buffer[index + 1] = unchecked((byte)((prevBytes >> 8) & 0xFF));
                buffer[index + 2] = unchecked((byte)((prevBytes >> 16) & 0xFF));
                buffer[index + 3] = unchecked((byte)((prevBytes >> 24) & 0xFF));
                buffer[index + 4] = unchecked((byte)((nextBytes >> 0) & 0xFF));
                buffer[index + 5] = unchecked((byte)((nextBytes >> 8) & 0xFF));
                buffer[index + 6] = unchecked((byte)((nextBytes >> 16) & 0xFF));
                buffer[index + 7] = unchecked((byte)((nextBytes >> 24) & 0xFF));

                if(this.PrecedingFreeBytes != 0)
                    buffer[index - 1] = this.PrecedingFreeBytes;
            }
            public static void WritePrecedingFreeBytes(byte[] buffer, int index, byte precedingFreeBytes) {
                if(precedingFreeBytes == 0) 
                    buffer[index + 7] &= 0x7F;
                else {
                    buffer[index - 1] = precedingFreeBytes;
                    buffer[index + 7] = unchecked((byte)((buffer[index + 7] & 0x7F) | 0x80));
                }
            }
            public static void WritePrevBytes(byte[] buffer, int index, int prevBytes) {
                var is_free = (buffer[index + 3] & 0x80) != 0;
                prevBytes = unchecked((prevBytes & 0x7FFF_FFFF) | (is_free ? (int)0x8000_0000 : 0));

                buffer[index + 0] = unchecked((byte)((prevBytes >> 0) & 0xFF));
                buffer[index + 1] = unchecked((byte)((prevBytes >> 8) & 0xFF));
                buffer[index + 2] = unchecked((byte)((prevBytes >> 16) & 0xFF));
                buffer[index + 3] = unchecked((byte)((prevBytes >> 24) & 0xFF));
            }
            public void WritePrevBytes(byte[] buffer, int index) {
                int prevBytes = unchecked((this.PrevBytes & 0x7FFF_FFFF) | (this.IsFree ? (int)0x8000_0000 : 0));

                buffer[index + 0] = unchecked((byte)((prevBytes >> 0) & 0xFF));
                buffer[index + 1] = unchecked((byte)((prevBytes >> 8) & 0xFF));
                buffer[index + 2] = unchecked((byte)((prevBytes >> 16) & 0xFF));
                buffer[index + 3] = unchecked((byte)((prevBytes >> 24) & 0xFF));
            }
            //public static void WriteNextBytes(byte[] buffer, int index, int nextBytes) {
            //    var contains_preceding_free_bytes = (buffer[index + 7] & 0x80) != 0;
            //    nextBytes = unchecked((nextBytes & 0x7FFF_FFFF) | (contains_preceding_free_bytes ? (int)0x8000_0000 : 0));
            //
            //    buffer[index + 4] = unchecked((byte)((nextBytes >> 0) & 0xFF));
            //    buffer[index + 5] = unchecked((byte)((nextBytes >> 8) & 0xFF));
            //    buffer[index + 6] = unchecked((byte)((nextBytes >> 16) & 0xFF));
            //    buffer[index + 7] = unchecked((byte)((nextBytes >> 24) & 0xFF));
            //}
            public void WriteNextBytes(byte[] buffer, int index) {
                var nextBytes = unchecked((this.NextBytes & 0x7FFF_FFFF) | (this.PrecedingFreeBytes != 0 ? (int)0x8000_0000 : 0));

                buffer[index + 4] = unchecked((byte)((nextBytes >> 0) & 0xFF));
                buffer[index + 5] = unchecked((byte)((nextBytes >> 8) & 0xFF));
                buffer[index + 6] = unchecked((byte)((nextBytes >> 16) & 0xFF));
                buffer[index + 7] = unchecked((byte)((nextBytes >> 24) & 0xFF));
            }
            public static int ReadNextBytes(byte[] buffer, int index) {
                return 
                    (buffer[index + 4] << 0) |
                    (buffer[index + 5] << 8) |
                    (buffer[index + 6] << 16) |
                    ((buffer[index + 7] & 0x7F) << 24);
            }
        }
        #endregion
        #region private struct MemoryPtr
        /// <summary>
        /// A pointer to managed memory.
        /// </summary>
        private readonly struct MemoryPtr {
            public const int SIZEOF = 8;

            public static readonly MemoryPtr Null = new MemoryPtr(-1, 0);

            public readonly int ChunkID;
            public readonly int Address;
            public readonly bool ExtraBit;

            public MemoryPtr(int chunkID, int address, bool extraBit = false) : this() {
                this.ChunkID  = chunkID;
                this.Address  = address;
                this.ExtraBit = extraBit;
            }

            public MemoryPtr SetExtraBit(bool extraBit) {
                if(extraBit == this.ExtraBit)
                    return this;
                else
                    return new MemoryPtr(this.ChunkID, this.Address, extraBit);
            }

            public static MemoryPtr Read(byte[] buffer, int index) {
                var chunkID = 
                    (buffer[index + 0] << 0) |
                    (buffer[index + 1] << 8) |
                    (buffer[index + 2] << 16) |
                    (buffer[index + 3] << 24);
                var address = 
                    (buffer[index + 4] << 0) |
                    (buffer[index + 5] << 8) |
                    (buffer[index + 6] << 16) |
                    (buffer[index + 7] << 24);
                var extraBit = unchecked((chunkID & (int)0x8000_0000) != 0);
                var x = chunkID & 0x7FFF_FFFF;
                return new MemoryPtr(
                    x != 0x7FFF_FFFF ? x : -1, // special case for chunk -1
                    address,
                    extraBit);
            }
            public void Write(byte[] buffer, int index) {
                var chunkID = (this.ChunkID & 0x7FFF_FFFF) | (this.ExtraBit ? unchecked((int)0x8000_0000) : 0);

                buffer[index + 0] = unchecked((byte)((chunkID >> 0) & 0xFF));
                buffer[index + 1] = unchecked((byte)((chunkID >> 8) & 0xFF));
                buffer[index + 2] = unchecked((byte)((chunkID >> 16) & 0xFF));
                buffer[index + 3] = unchecked((byte)((chunkID >> 24) & 0xFF));
                buffer[index + 4] = unchecked((byte)((this.Address >> 0) & 0xFF));
                buffer[index + 5] = unchecked((byte)((this.Address >> 8) & 0xFF));
                buffer[index + 6] = unchecked((byte)((this.Address >> 16) & 0xFF));
                buffer[index + 7] = unchecked((byte)((this.Address >> 24) & 0xFF));
            }


            public bool Equals(MemoryPtr other) {
                return this.Address == other.Address && this.ChunkID == other.ChunkID; // intentionally ignore "&& this.ExtraBit == other.ExtraBit"
            }
            public override bool Equals(object obj) {
                if(obj is MemoryPtr x)
                    return this.Equals(x);
                return false;
            }

            public static bool operator ==(MemoryPtr x, MemoryPtr y) {
                return x.Equals(y);
            }
            public static bool operator !=(MemoryPtr x, MemoryPtr y) {
                return !(x == y);
            }
            public override int GetHashCode() {
                return (this.Address, this.ChunkID).GetHashCode(); // intentionally ignore "this.ExtraBit"
            }
            public override string ToString() {
                if(this.ChunkID < 0)
                    return "[null]";

                return string.Format("[{0} @{1} ({2})]",
                    this.ChunkID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    this.Address.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    this.ExtraBit);
            }
        }
        #endregion

        // AVL-tree node specific methods
        #region private static AvlCompareLength()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int AvlCompareLength(int length, int x) {
            return length - x;
        }
        #endregion
        #region private static AvlCompareLengthAndAddress()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int AvlCompareLengthAndAddress(in MemoryPtr memoryHandle, int key, Node y) {
            int diff = key - y.Key;
            if(diff != 0)
                return diff;
            diff = memoryHandle.ChunkID - y.Address.ChunkID;
            if(diff != 0)
                return diff;
            return memoryHandle.Address - y.Address.Address;
        }
        #endregion
        #region private static AvlCompare()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AvlCompare(Node x, Node y) {
            return x == y;

            // if you dont have a node cache (dict<address, node>), then usually comparing by address makes more sense
            //return x.Address == y.Address;
        }
        #endregion
        #region private static AvlIsNull()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AvlIsNull(in MemoryPtr memoryHandle) {
            //return memoryHandle == MemoryPtr.Null;
            return memoryHandle.ChunkID < 0;
        }
        #endregion
        #region private static AvlIsLeftNull()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AvlIsLeftNull(Node node) {
            return AvlIsNull(node.LeftPtr);
        }
        #endregion
        #region private static AvlIsRightNull()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AvlIsRightNull(Node node) {
            return AvlIsNull(node.RightPtr);
        }
        #endregion

        // AVL node + MRU cache
        #region private GetOrAddNodeFromCache()
        /// <summary>
        ///     Loads a Node from the address, using the cache/registering to cache if not.
        ///     Bumps up the node in MRU cache.
        /// </summary>
        private Node GetOrAddNodeFromCache(in MemoryPtr address) {
            var res = m_cache.GetOrAdd(address, NewNode);
            return res;

            Node NewNode(MemoryPtr ptr) {
                var _new = new Node(this, ptr);
                _new.Read();
                return _new;
            }
        }
        /// <summary>
        ///     Loads a Node from the address, using the cache/registering to cache if not.
        ///     Bumps up the node in MRU cache.
        /// </summary>
        private Node GetOrAddNodeFromCache(in MemoryPtr address, byte[] chunk_memory) {
            var res = m_cache.GetOrAdd(address, NewNode);
            return res;

            Node NewNode(MemoryPtr ptr) {
                var _new = new Node(this, ptr);
                _new.Read(chunk_memory);
                return _new;
            }
        }
        #endregion
        #region private RemoveFromCache()
        private void RemoveFromCache(in MemoryPtr address) {
            m_cache.Remove(address);
        }
        #endregion
        #region private CreateNode()
        /// <summary>
        ///     Creates a new Node and adds it to the MRU cache.
        ///     Bumps up the node in MRU cache.
        /// </summary>
        private Node CreateNode(in MemoryPtr address, int key) {
            var _new = new Node(this, address, key);
            m_cache.Add(address, _new);
            return _new;
        }
        /// <summary>
        ///     Creates a new Node and adds it to the MRU cache.
        ///     Bumps up the node in MRU cache.
        /// </summary>
        private Node CreateNode(in MemoryPtr address) {
            var _new = new Node(this, address);
            m_cache.Add(address, _new);
            return _new;
        }
        #endregion
        #region private BumpNodeInCache()
        /// <summary>
        ///     Bumps up the node in MRU cache.
        /// </summary>
        private void BumpNodeInCache(in MemoryPtr address) {
            // if reading non-null AVLNode property (.Left/.Right/.Parent), bump those nodes up in the MRU
            // leads to a better usage of MRU cache at the cost of extra O(1) bookkeeping
            //
            // this code is actually necessary to maintain integrity, otherwise the ordering of evicted items
            // could mean an old un-bumped node gets evicted that is part of the path were currently modifying
            // and the code writes to the evicted node properties without knowing it was evicted, leading to 
            // uncommitted writes

            m_cache.Bump(address);
        }
        #endregion

        #region private OnAvlNodeEvicted()
        private void OnAvlNodeEvicted(Node value) {
#if USE_UNCOMMITTED_WRITES
            // if the avlnode gets evicted, rather than deleted, then we need to write it
            // the reason for this is because that means we removed it from the MRU cache, and thus, 
            // the next time we try to read the node from memory, we want to have it stored
            
            // see IMPLEMENTATION NOTES for details

            value.Write();

            // then remove the item from being loaded elsewhere
            value.Detach();
#endif
        }
        #endregion

        // MRU cache
        #region private class AvlNodeMRUCache
        private sealed class AvlNodeMRUCache : MRUCache<MemoryPtr, Node> {
            private readonly AvlTreeMemoryAllocator m_owner;

            public AvlNodeMRUCache(AvlTreeMemoryAllocator owner, int capacity) : base(capacity){ 
                m_owner = owner;
            }

            /// <summary>
            ///     Invoked whenever an item gets evicted 
            ///     (ie: removed without Remove()/Clear() called).
            ///     
            ///     Add()/SetCapacity() can invoke this, if this.Count > this.Capacity.
            /// </summary>
            /// <param name="key">The key being evicted.</param>
            /// <param name="value">The value being evicted.</param>
            protected override void OnItemEvicted(in MemoryPtr key, in Node value) {
                m_owner.OnAvlNodeEvicted(value);
            }
        }
        #endregion
        #region private class MRUCache<TKey, TValue>
        /// <summary>
        ///    Implements a MRU (Most Recently Used) Dictionary with a limited number of entries.
        ///    Entries are evicted in Least-Recently-Used order.
        /// </summary>
        private abstract class MRUCache<TKey, TValue> where TKey : struct {
            private readonly Dictionary<TKey, NodePointer> m_dict;
            private readonly Node[] m_nodes; // circular double linked list

            private int m_mostRecentlyUsedIndex;
 
            public int Count { get; private set; }
            public int Capacity { get; private set; }
 
            #region constructors
            public MRUCache(int capacity) { 
                m_dict        = new Dictionary<TKey, NodePointer>(capacity);
                m_nodes       = new Node[capacity];
                this.Capacity = capacity;

                this.Init();
                this.Count = 0;
            }
            #endregion

            #region LeastRecentlyUsed
            public TKey LeastRecentlyUsed {
                get {
                    if(this.Count == 0)
                        throw new KeyNotFoundException("Collection is empty.");

                    var most_recently_used  = m_nodes[m_mostRecentlyUsedIndex];
                    var least_recently_used = m_nodes[most_recently_used.Prev];
                    return least_recently_used.Key;
                }
            }
            #endregion

            #region Add()
            public void Add(in TKey key, in TValue value) {
                if(!this.TryAdd(key, value))
                    throw new ArgumentException("An element with the same key already exists", nameof(key));
            }
            #endregion
            #region Remove()
            public bool Remove(in TKey key) {
                return this.TryRemove(key, out _);
            }
            #endregion
            #region Clear()
            public void Clear() {
                // make sure to clear the keys as they could contain pointers
                int max = this.Count;
                for(int i = 0; i < max; i++) {
                    ref var node = ref m_nodes[i];
                
                    //var key    = node.Key;
                    //var value  = m_dict[key].Value;

                    node.Key = default;
                
                    //this.OnItemEvicted(key, value);
                }

                m_dict.Clear();
            
                this.Init();
                this.Count = 0;
            }
            #endregion

            #region TryAdd()
            public bool TryAdd(in TKey key, in TValue value) {
                if(m_dict.TryGetValue(key, out var nodePointer))
                    return false;

                if(this.Count >= this.Capacity) {
                    // evict LRU
                    var lru_key = this.LeastRecentlyUsed;
                    if(this.TryRemove(lru_key, out var lru_value))
                        this.OnItemEvicted(lru_key, lru_value);
                }

                // note: if count==0, then m_mostRecentlyUsedIndex=0, and m_nodes[0].Prev/Next = 0

                // always add at the end
                nodePointer.Index = this.Count;
                nodePointer.Value = value;
            
                ref var mru  = ref m_nodes[m_mostRecentlyUsedIndex];
                ref var _new = ref m_nodes[nodePointer.Index];
            
                _new.Key               = key;
                _new.Next              = m_mostRecentlyUsedIndex;
                _new.Prev              = mru.Prev;
                m_nodes[mru.Prev].Next = nodePointer.Index;
                mru.Prev               = nodePointer.Index;

                m_dict.Add(key, nodePointer);
                m_mostRecentlyUsedIndex = nodePointer.Index;

                this.Count++;
                return true;
            }
            #endregion
            #region TryRemove()
            public bool TryRemove(in TKey key, out TValue value) {
                if(!m_dict.TryGetValue(key, out var nodePointer)) {
                    value = default;
                    return false;
                }

                value = nodePointer.Value;
                m_dict.Remove(key);

                ref var old = ref m_nodes[nodePointer.Index];
            
                // remove from circular double linked list
                m_nodes[old.Prev].Next = old.Next;
                m_nodes[old.Next].Prev = old.Prev;
                old.Key                = default;

                if(m_mostRecentlyUsedIndex == nodePointer.Index)
                    m_mostRecentlyUsedIndex = old.Next;

                if(nodePointer.Index < this.Count - 1) {
                    // this code is a lot more complex than expected, mostly because we need to make no entries are at this.Count(+) in m_nodes[]
                    // this is because we dont want to search for what index are available when adding a new item

                    ref var last = ref m_nodes[this.Count - 1];

                    // copy last unto the deleted entry
                    m_nodes[nodePointer.Index] = m_nodes[this.Count - 1];
                    if(m_mostRecentlyUsedIndex == this.Count - 1)
                        m_mostRecentlyUsedIndex = nodePointer.Index;

                    var x = m_dict[last.Key];
                    x.Index = nodePointer.Index;
                    m_dict[last.Key] = x;

                    last.Key = default;
                    m_nodes[last.Prev].Next = nodePointer.Index;
                    m_nodes[last.Next].Prev = nodePointer.Index;
                } else if(this.Count == 1)
                    this.Init();

                this.Count--;
                return true;
            }
            #endregion
            #region TryGetValue()
            public bool TryGetValue(in TKey key, out TValue value) {
                if(!m_dict.TryGetValue(key, out var nodePointer)) {
                    value = default;
                    return false;
                }
            
                value = nodePointer.Value;
                this.BumpValidAndExisting(nodePointer.Index);
                return true;
            }
            #endregion

            #region GetOrAdd()
            public TValue GetOrAdd(in TKey key, Func<TKey, TValue> valueFactory) {
                //if(valueFactory == null)
                //    throw new ArgumentNullException(nameof(valueFactory));

                if(this.TryGetValue(key, out var res))
                    return res;

                var value = valueFactory(key);
                this.TryAdd(key, value);
                return value;
            }
            #endregion

            #region Bump()
            public bool Bump(in TKey key) {
                if(!m_dict.TryGetValue(key, out var nodePointer))
                    return false;
            
                this.BumpValidAndExisting(nodePointer.Index);
                return true;
            }
            private void BumpValidAndExisting(int index) {
                if(index == m_mostRecentlyUsedIndex) 
                    return;
            
                ref var old = ref m_nodes[index];
            
                // remove old MRU from circular double linked list
                m_nodes[old.Prev].Next = old.Next;
                m_nodes[old.Next].Prev = old.Prev;

                ref var mru  = ref m_nodes[m_mostRecentlyUsedIndex];
            
                // bump up
                old.Prev               = mru.Prev;
                old.Next               = m_mostRecentlyUsedIndex;
                m_nodes[mru.Prev].Next = index;
                mru.Prev               = index;

                m_mostRecentlyUsedIndex = index;
            }
            #endregion

            #region protected abstract void OnItemEvicted()
            /// <summary>
            ///     Invoked whenever an item gets evicted 
            ///     (ie: removed without Remove()/Clear() called).
            ///     
            ///     Add()/SetCapacity() can invoke this, if this.Count > this.Capacity.
            /// </summary>
            protected abstract void OnItemEvicted(in TKey key, in TValue value);
            #endregion

            #region private Init()
            private void Init() {
                // special case: first item must always have its prev/next set to itself
                m_mostRecentlyUsedIndex = 0; // important, this lets add() behave properly

                ref var first = ref m_nodes[0];
                first.Next = 0;
                first.Prev = 0;
            }
            #endregion

            private struct NodePointer {
                public TValue Value;
                public int Index;
            }
            // use a struct in order to make efficient use of cache lines
            // basically we're forcing the items to be allocated nearby, since we know the capacity should mostly not change ever, 
            // which the default .NET allocator cannot know
            // also this saves 20 bytes per item vs using a class;
            //     "class Node{TKey,TValue,Prev,Next}" = 32 bytes + sizeof(TKey) + sizeof(TValue)    (because 16 bytes overhead)
            //     "struct NodePointer/Node"           = 12 bytes + sizeof(TKey) + sizeof(TValue)
            private struct Node {
                public int Prev;
                public int Next;
                public TKey Key;
            }
        }
        #endregion

        #region private enum State 
        private enum State : byte {
            Balanced  = 0,
            Header    = 1,
            LeftHigh  = 2,
            RightHigh = 3,
        }
        #endregion
        #region private class Node
        /// <summary>
        /// AVL node
        /// Represents a consecutive chunk of free memory.
        /// </summary>
        private sealed class Node {
            /// <summary>
            /// The serialized size of this struct, excluding the "Key" component.
            /// </summary>
            public const int SIZEOF = 3 * MemoryPtr.SIZEOF; // +1

            private readonly AvlTreeMemoryAllocator m_owner;
            private readonly MemoryPtr m_address;

            internal Node m_left;
            internal Node m_right;
            internal Node m_parent;
            internal MemoryPtr m_leftPtr;
            internal MemoryPtr m_rightPtr;
            internal MemoryPtr m_parentPtr;
            private State m_balance = State.Balanced;
                
            public MemoryPtr Address => m_address;

            /// <summary>
            /// The "Key" is the FreeMemorySize that starts from the AvlNode start position 
            /// (the AvlNode gets overwritten if allocating that memory).
            /// </summary>
            public int Key;

#if THROW_WHEN_WRITING_ON_EVICTED_NODE
            private bool m_evicted = false;
#endif

            public Node Left {
                get {
                    if(m_left == null && !AvlIsNull(m_leftPtr))
                        m_left = m_owner.GetOrAddNodeFromCache(m_leftPtr);
                    else
                        m_owner.BumpNodeInCache(m_leftPtr);

                    return m_left;
                }
                set {
                    m_left    = value;
                    m_leftPtr = value != null ? value.Address : MemoryPtr.Null;
#if !USE_UNCOMMITTED_WRITES
                    this.WriteLeftPtr();
#endif
                    this.ThrowIfEvicted();
                }
            }
            public Node Right {
                get {
                    if(m_right == null && !AvlIsNull(m_rightPtr)) 
                        m_right = m_owner.GetOrAddNodeFromCache(m_rightPtr);
                    else
                        m_owner.BumpNodeInCache(m_rightPtr);

                    return m_right;
                }
                set {
                    m_right    = value;
                    m_rightPtr = value != null ? value.Address : MemoryPtr.Null;
#if !USE_UNCOMMITTED_WRITES
                    this.WriteRightPtr();
#endif
                    this.ThrowIfEvicted();
                }
            }
            public Node Parent {
                get {
                    if(m_parent == null && !AvlIsNull(m_parentPtr))
                        m_parent = m_owner.GetOrAddNodeFromCache(m_parentPtr);
                    else
                        m_owner.BumpNodeInCache(m_parentPtr);

                    return m_parent;
                }
                set {
                    m_parent    = value;
                    m_parentPtr = value != null ? value.Address : MemoryPtr.Null;
#if !USE_UNCOMMITTED_WRITES
                    this.WriteParentPtr();
#endif
                    this.ThrowIfEvicted();
                }
            }
            public State Balance {
                get => m_balance;
                set {
                    m_balance = value;
                        
                    //ref var left_ptr = ref m_leftPtr;
                    //m_leftPtr.ExtraBit  = ((int)this.Balance & 1) != 0;
                    //ref var right_ptr = ref m_rightPtr;
                    //right_ptr.ExtraBit = ((int)this.Balance & 2) != 0;
#if !USE_UNCOMMITTED_WRITES
                    this.WriteBalance();
#endif
                    this.ThrowIfEvicted();
                }
            }
            public MemoryPtr LeftPtr {
                get => m_leftPtr;
                set {
                    m_leftPtr = value;
                    m_left    = null;
#if !USE_UNCOMMITTED_WRITES
                    this.WriteLeftPtr();
#endif
                    this.ThrowIfEvicted();
                }
            }
            public MemoryPtr RightPtr {
                get => m_rightPtr;
                set {
                    m_rightPtr = value;
                    m_right    = null;
#if !USE_UNCOMMITTED_WRITES
                    this.WriteRightPtr();
#endif
                    this.ThrowIfEvicted();
                }
            }

            public MemoryPtr ParentPtr {
                get => m_parentPtr;
                set {
                    m_parentPtr = value;
                    m_parent    = null;
#if !USE_UNCOMMITTED_WRITES
                    this.WriteParentPtr();
#endif
                    this.ThrowIfEvicted();
                }
            }

            #region constructors
            public Node(AvlTreeMemoryAllocator owner, in MemoryPtr address) {
                m_owner     = owner;
                m_address   = address;
                m_leftPtr   = MemoryPtr.Null;
                m_rightPtr  = MemoryPtr.Null;
                m_parentPtr = MemoryPtr.Null;
                this.Key    = -1;
            }
            public Node(AvlTreeMemoryAllocator owner, in MemoryPtr address, int key) : this(owner, address) {
                this.Key = key;
            }
            #endregion

            #region Detach()
            /// <summary>
            ///     Detach the instance of this class from the tree.
            ///     This is meant to be used in order to clear memory.
            ///     this works because the parent/left/right pointers arent removed, so they can rebuild this node
            /// </summary>
            public void Detach() {
                if(m_parent != null) {
                    if(m_parent.m_left == this)
                        m_parent.m_left = null;
                    else if(m_parent.m_right == this) // if that check doesnt work, your tree is messed up
                        m_parent.m_right = null;
                }
                if(m_left != null) 
                    m_left.m_parent = null;
                if(m_right != null) 
                    m_right.m_parent = null;

#if THROW_WHEN_WRITING_ON_EVICTED_NODE
                m_evicted = true;
#endif
            }
            #endregion

            [System.Diagnostics.Conditional("THROW_WHEN_WRITING_ON_EVICTED_NODE")]
            private void ThrowIfEvicted() {
#if THROW_WHEN_WRITING_ON_EVICTED_NODE
                if(m_evicted) {
                    //m_owner.m_cache.Add(this.Address, this);
                    throw new NotSupportedException();
                }
#endif
            }

            #region ToString()
            public override string ToString() {
                return string.Format("{{{0} left:{1} right:{2} parent:{3} state:{4}}}",
                    ToStringPtr(this.Address),
                    ToStringPtr(m_leftPtr),
                    ToStringPtr(m_rightPtr),
                    ToStringPtr(m_parentPtr),
                    this.Balance.ToString());
                string ToStringPtr(MemoryPtr ptr) {
                    return $"[chunk:{ptr.ChunkID} @{ptr.Address}]";
                }
            }
            #endregion

            public void Read() {
                var buffer = m_owner.m_chunks[m_address.ChunkID].Memory;
                this.Read(buffer);
            }
            public void Read(byte[] buffer) {
                var index = m_address.Address;

                this.LeftPtr   = MemoryPtr.Read(buffer, index);
                this.RightPtr  = MemoryPtr.Read(buffer, index + MemoryPtr.SIZEOF);
                this.ParentPtr = MemoryPtr.Read(buffer, index + 2 * MemoryPtr.SIZEOF);
                //this.Balance = (State)buffer[index + 3 * MemoryPtr.SIZEOF];

                if(this.Key < 0)
                    this.Key = MemorySegmentHeader.ReadNextBytes(buffer, index - MemorySegmentHeader.SIZEOF);
                    
                this.Balance = (State)( (this.LeftPtr.ExtraBit ? 1 : 0) | (this.RightPtr.ExtraBit ? 2 : 0) );
            }
            public void Write() {
                var buffer = m_owner.m_chunks[m_address.ChunkID].Memory;
                this.Write(buffer);
            }
            public void Write(byte[] buffer) {
                var index     = m_address.Address;
                var left_ptr  = m_leftPtr.SetExtraBit(((int)this.Balance & 1) != 0);
                var right_ptr = m_rightPtr.SetExtraBit(((int)this.Balance & 2) != 0);

                left_ptr.Write(buffer, index);
                right_ptr.Write(buffer, index + MemoryPtr.SIZEOF);
                this.ParentPtr.Write(buffer, index + 2 * MemoryPtr.SIZEOF);
                //buffer[index + 3 * MemoryPtr.SIZEOF] = (byte)this.Balance;
                    
                // cant write this.Key
            }
#if !USE_UNCOMMITTED_WRITES
            public void WriteLeftPtr() {
                var buffer = m_owner.m_chunks[m_address.ChunkID].Memory;
                var index  = m_address.Address;
                var left_ptr  = m_leftPtr.SetExtraBit(((int)this.Balance & 1) != 0);
                left_ptr.Write(buffer, index);
            }
            public void WriteRightPtr() {
                var buffer = m_owner.m_chunks[m_address.ChunkID].Memory;
                var index  = m_address.Address;
                var right_ptr = m_rightPtr.SetExtraBit(((int)this.Balance & 2) != 0);
                right_ptr.Write(buffer, index + MemoryPtr.SIZEOF);
            }
            public void WriteParentPtr() {
                var buffer = m_owner.m_chunks[m_address.ChunkID].Memory;
                var index  = m_address.Address;
                m_parentPtr.Write(buffer, index + 2 * MemoryPtr.SIZEOF);
            }
            public void WriteBalance() {
                var buffer = m_owner.m_chunks[m_address.ChunkID].Memory;
                var index  = m_address.Address;

                var left_ptr  = m_leftPtr.SetExtraBit(((int)this.Balance & 1) != 0);
                var right_ptr = m_rightPtr.SetExtraBit(((int)this.Balance & 2) != 0);

                left_ptr.Write(buffer, index);
                right_ptr.Write(buffer, index + MemoryPtr.SIZEOF);
            }
#endif
        }
        #endregion

        // AVL tree
        #region private class AvlTree
        /// <summary>
        ///    Implements an AVL tree (Adelson-Velsky and Landis).
        ///    This is a self-balancing binary search tree that takes 2 extra bits per node over a binary search tree.
        ///    Search/Insert/Delete() run in O(log n).
        ///    Despite many claims to the contrary, practical tests show much better performance from this over Red-Black Tree==s.
        /// </summary>
        /// <remarks>
        ///    More strictly balanced than Red-Black Trees, leading to better lookup times.
        ///    Typically, AvlTrees are wrongly considered slower because they enforce stricter balance, or because they require more balancing operations.
        ///    Empyrical testing shows that number_of_rotations is a poor measure of performance, as it yields little difference.
        ///    
        ///    So the overall benefit from more strictly enforced tree height results in more overall benefits than the cost of the rotations, 
        ///    making AvlTree better suited for general use than Red-Black Trees. The only difference is the one extra bit required, 
        ///    but .NET memory alignments prevent that bit saving to come into effect anyway.
        ///    
        ///    worst case       |   AVL tree      |   RB tree
        ///    =======================================
        ///    height           | 1.44 log n      | 2 log(n + 1)
        ///    update           | log n           | log n
        ///    lookup           | log n  (faster) | log n
        ///    insert rotations | 2               | 2               (very poor measure of performance)
        ///    delete rotations | log n           | 3               (very poor measure of performance)
        /// </remarks>
        private sealed class AvlTree {
            private Node m_header; // note: root = m_header.Parent
            private readonly AvlTreeMemoryAllocator m_owner;

            #region constructors
            public AvlTree(AvlTreeMemoryAllocator owner) {
                m_owner = owner;
            }
            #endregion

            #region Add()
            /// <summary>
            ///     O(log n)
            /// </summary>
            public void Add(in MemoryPtr memoryHandle, int key) {
                m_owner.BumpNodeInCache(m_header.Address);

                var node = m_header.Parent;
                if(node != null) {
                    while(true) {
                        var diff = AvlCompareLengthAndAddress(memoryHandle, key, node);

                        if(diff > 0) {
                            if(!AvlIsRightNull(node))
                                node = node.Right;
                            else {
                                this.CreateRightNodeRare(memoryHandle, key, node);
                                break;
                            }
                        } else if(diff < 0) {
                            if(!AvlIsLeftNull(node))
                                node = node.Left;
                            else {
                                this.CreateLeftNodeRare(memoryHandle, key, node);
                                break;
                            }
                        }
                        // not possible since all keys are {length, address}, so guaranteed unique
                        //else throw new ArgumentException($"Duplicate key ({key}).", nameof(key));
                    }
                } else
                    this.CreateRootNodeRare(memoryHandle, key);
            }

            private void CreateLeftNodeRare(in MemoryPtr memoryHandle, int key, Node parent) {
                var _new = m_owner.CreateNode(memoryHandle, key);
                _new.Parent  = parent;
                _new.Balance = State.Balanced;

                parent.Left = _new;

                BalanceSet(parent, Direction.Left);
            }
            private void CreateRightNodeRare(in MemoryPtr memoryHandle, int key, Node parent) {
                var _new = m_owner.CreateNode(memoryHandle, key);
                _new.Parent  = parent;
                _new.Balance = State.Balanced;

                parent.Right = _new;

                BalanceSet(parent, Direction.Right);
            }
            private void CreateRootNodeRare(in MemoryPtr memoryHandle, int key) {
                var _new = m_owner.CreateNode(memoryHandle, key);
                _new.Parent  = m_header;
                _new.Balance = State.Balanced;

                m_header.Parent = _new;
            }

            /// <summary>
            ///     Balance the tree by walking the tree upwards.
            /// </summary>
            private static void BalanceSet(Node node, Direction direction) {
                var is_taller = true;

                while(is_taller) {
                    var parent = node.Parent;
                    var next   = AvlCompare(parent.Left, node) ? Direction.Left : Direction.Right;

                    if(direction == Direction.Left) {
                        switch(node.Balance) {
                            case State.LeftHigh:
                                if(parent.Balance == State.Header) {
                                    //BalanceLeft(ref parent.Parent);
                                    var x = parent.Parent;
                                    BalanceLeft(ref x);
                                    parent.Parent = x;
                                } else if(AvlCompare(parent.Left, node)) {
                                    //BalanceLeft(ref parent.Left);
                                    var x = parent.Left;
                                    BalanceLeft(ref x);
                                    parent.Left = x;
                                } else {
                                    //BalanceLeft(ref parent.Right);
                                    var x = parent.Right;
                                    BalanceLeft(ref x);
                                    parent.Right = x;
                                }
                                return;

                            case State.Balanced:
                                node.Balance = State.LeftHigh;
                                break;

                            case State.RightHigh:
                                node.Balance = State.Balanced;
                                return;
                        }
                    } else {
                        switch(node.Balance) {
                            case State.LeftHigh:
                                node.Balance = State.Balanced;
                                return;

                            case State.Balanced:
                                node.Balance = State.RightHigh;
                                break;

                            case State.RightHigh:
                                if(parent.Balance == State.Header) {
                                    //BalanceRight(ref parent.Parent);
                                    var x = parent.Parent;
                                    BalanceRight(ref x);
                                    parent.Parent = x;
                                } else if(AvlCompare(parent.Left, node)) {
                                    //BalanceRight(ref parent.Left);
                                    var x = parent.Left;
                                    BalanceRight(ref x);
                                    parent.Left = x;
                                } else {
                                    //BalanceRight(ref parent.Right);
                                    var x = parent.Right;
                                    BalanceRight(ref x);
                                    parent.Right = x;
                                }
                                return;
                        }
                    }

                    if(is_taller) {
                        if(parent.Balance == State.Header)
                            return;

                        node      = parent;
                        direction = next;
                    }
                }
            }
            #endregion
            #region Remove()
            /// <summary>
            ///     Average:  O(1)
            ///     Worst:    O(log n)
            /// </summary>
            public void Remove(in MemoryPtr memoryHandle, byte[] chunk_memory) {
                // it may look strange to load into MRU cache the item were about to delete
                // but consider that the rebalances that follow are going to load it into cache anyway
                var node = m_owner.GetOrAddNodeFromCache(memoryHandle, chunk_memory);

                if(!AvlIsLeftNull(node) && !AvlIsRightNull(node)) {
                    var replacement = node.Left;
                    while(!AvlIsRightNull(replacement))
                        replacement = replacement.Right;
                    SwapNodes(node, replacement);
                }

                var parent    = node.Parent;
                var direction = AvlCompare(parent.Left, node) ? Direction.Left : Direction.Right;

                if(AvlIsLeftNull(node)) {
                    if(AvlCompare(parent, m_header))
                        m_header.Parent = node.Right;
                    else if(AvlCompare(parent.Left, node))
                        parent.Left = node.Right;
                    else
                        parent.Right = node.Right;

                    if(!AvlIsRightNull(node))
                        node.Right.Parent = parent;
                } else {
                    if(AvlCompare(parent, m_header))
                        m_header.Parent = node.Left;
                    else if(AvlCompare(parent.Left, node))
                        parent.Left = node.Left;
                    else
                        parent.Right = node.Left;

                    if(!AvlIsLeftNull(node))
                        node.Left.Parent = parent;
                }

                BalanceSetRemove(parent, direction);

                m_owner.RemoveFromCache(memoryHandle);
            }
            private static void SwapNodes(Node x, Node y) {
                if(AvlCompare(x.Left, y)) {
                    if(!AvlIsLeftNull(y))  y.Left.Parent  = x;
                    if(!AvlIsRightNull(y)) y.Right.Parent = x;
                    if(!AvlIsRightNull(x)) x.Right.Parent = y;
 
                    if(x.Parent.Balance != State.Header) {
                        if(AvlCompare(x.Parent.Left, x))
                            x.Parent.Left = y;
                        else
                            x.Parent.Right = y;
                    } else
                        x.Parent.Parent = y;
 
                    y.Parent = x.Parent;
                    x.Parent = y;
                    x.Left   = y.Left;
                    y.Left   = x;
 
                    Swap(ref x.m_right, ref x.m_rightPtr, ref y.m_right, ref y.m_rightPtr); // Swap(ref x.Right, ref y.Right);
#if !USE_UNCOMMITTED_WRITES
                    x.WriteRightPtr();
                    y.WriteRightPtr();
#endif
                } else if(AvlCompare(x.Right, y)) {
                    if(!AvlIsRightNull(y)) y.Right.Parent = x;
                    if(!AvlIsLeftNull(y))  y.Left.Parent  = x;
                    if(!AvlIsLeftNull(x))  x.Left.Parent  = y;
 
                    if(x.Parent.Balance != State.Header) {
                        if(AvlCompare(x.Parent.Left, x))
                            x.Parent.Left = y;
                        else
                            x.Parent.Right = y;
                    } else
                        x.Parent.Parent = y;
 
                    y.Parent = x.Parent;
                    x.Parent = y;
                    x.Right  = y.Right;
                    y.Right  = x;
 
                    Swap(ref x.m_left, ref x.m_leftPtr, ref y.m_left, ref y.m_leftPtr); // Swap(ref x.Left, ref y.Left);
#if !USE_UNCOMMITTED_WRITES
                    x.WriteLeftPtr();
                    y.WriteLeftPtr();
#endif
                } else if(AvlCompare(x, y.Left)) {
                    if(!AvlIsLeftNull(x))  x.Left.Parent  = y;
                    if(!AvlIsRightNull(x)) x.Right.Parent = y;
                    if(!AvlIsRightNull(y)) y.Right.Parent = x;
 
                    if(y.Parent.Balance != State.Header) {
                        if(AvlCompare(y.Parent.Left, y))
                            y.Parent.Left = x;
                        else
                            y.Parent.Right = x;
                    } else
                        y.Parent.Parent = x;
 
                    x.Parent = y.Parent;
                    y.Parent = x;
                    y.Left   = x.Left;
                    x.Left   = y;
 
                    Swap(ref x.m_right, ref x.m_rightPtr, ref y.m_right, ref y.m_rightPtr); // Swap(ref x.Right, ref y.Right);
#if !USE_UNCOMMITTED_WRITES
                    x.WriteRightPtr();
                    y.WriteRightPtr();
#endif
                } else if(AvlCompare(x, y.Right)) {
                    if(!AvlIsRightNull(x)) x.Right.Parent = y;
                    if(!AvlIsLeftNull(x))  x.Left.Parent  = y;
                    if(!AvlIsLeftNull(y))  y.Left.Parent  = x;
 
                    if(y.Parent.Balance != State.Header) {
                        if(AvlCompare(y.Parent.Left, y))
                            y.Parent.Left = x;
                        else
                            y.Parent.Right = x;
                    } else
                        y.Parent.Parent = x;
 
                    x.Parent = y.Parent;
                    y.Parent = x;
                    y.Right  = x.Right;
                    x.Right  = y;
 
                    Swap(ref x.m_left, ref x.m_leftPtr, ref y.m_left, ref y.m_leftPtr); // Swap(ref x.Left, ref y.Left);
#if !USE_UNCOMMITTED_WRITES
                    x.WriteLeftPtr();
                    y.WriteLeftPtr();
#endif
                } else {
                    if(AvlCompare(x.Parent, y.Parent)) {
                        Swap(ref x.Parent.m_left, ref x.Parent.m_leftPtr, ref x.Parent.m_right, ref x.Parent.m_rightPtr); // Swap(ref x.Parent.Left, ref x.Parent.Right);
#if !USE_UNCOMMITTED_WRITES
                        x.Parent.Write();
                        //x.Parent.WriteLeftPtr();
                        //x.Parent.WriteRightPtr();
#endif
                    } else {
                        if(x.Parent.Balance != State.Header) {
                            if(AvlCompare(x.Parent.Left, x))
                                x.Parent.Left = y;
                            else
                                x.Parent.Right = y;
                        } else
                            x.Parent.Parent = y;

                        if(y.Parent.Balance != State.Header) {
                            if(AvlCompare(y.Parent.Left, y))
                                y.Parent.Left = x;
                            else
                                y.Parent.Right = x;
                        } else
                            y.Parent.Parent = x;
                    }
 
                    if(!AvlIsLeftNull(y))  y.Left.Parent  = x;
                    if(!AvlIsRightNull(y)) y.Right.Parent = x;
                    if(!AvlIsLeftNull(x))  x.Left.Parent  = y;
                    if(!AvlIsRightNull(x)) x.Right.Parent = y;
 
                    Swap(ref x.m_left, ref x.m_leftPtr, ref y.m_left, ref y.m_leftPtr);         // Swap(ref x.Left, ref y.Left);
                    Swap(ref x.m_right, ref x.m_rightPtr, ref y.m_right, ref y.m_rightPtr);     // Swap(ref x.Right, ref y.Right);
                    Swap(ref x.m_parent, ref x.m_parentPtr, ref y.m_parent, ref y.m_parentPtr); // Swap(ref x.Parent, ref y.Parent);
#if !USE_UNCOMMITTED_WRITES
                    x.Write();
                    y.Write();
#endif
                }
 
                var balance = x.Balance;
                x.Balance   = y.Balance;
                y.Balance   = balance;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Swap(ref Node x, ref MemoryPtr x_ptr, ref Node y, ref MemoryPtr y_ptr) {
                var temp = x;
                x        = y;
                y        = temp;
                var temp2 = x_ptr;
                x_ptr     = y_ptr;
                y_ptr     = temp2;

                // note: this isnt writing the values on owning node
                //#if !USE_UNCOMMITTED_WRITES
                //x_owner.Write()
                //y_owner.Write()
                //#endif
            }
            /// <summary>
            ///     Balance the tree by walking the tree upwards.
            /// </summary>
            private static void BalanceSetRemove(Node node, Direction direction) {
                if(node.Balance == State.Header)
                    return;
 
                var is_shorter = true;
 
                while(is_shorter) {
                    var parent = node.Parent;
                    var next   = AvlCompare(parent.Left, node) ? Direction.Left : Direction.Right;
 
                    if(direction == Direction.Left) {
                        switch(node.Balance) {
                            case State.LeftHigh:
                                node.Balance = State.Balanced;
                                break;
 
                            case State.Balanced:
                                node.Balance = State.RightHigh;
                                return;
 
                            case State.RightHigh:
                                if(node.Right.Balance == State.Balanced)
                                    is_shorter = false;

                                if(parent.Balance == State.Header) {
                                    //BalanceRight(ref parent.Parent);
                                    var x = parent.Parent;
                                    BalanceRight(ref x);
                                    parent.Parent = x;
                                } else if(AvlCompare(parent.Left, node)) {
                                    //BalanceRight(ref parent.Left);
                                    var x = parent.Left;
                                    BalanceRight(ref x);
                                    parent.Left = x;
                                } else {
                                    //BalanceRight(ref parent.Right);
                                    var x = parent.Right;
                                    BalanceRight(ref x);
                                    parent.Right = x;
                                }
                                break;
                        }
                    } else {
                        switch(node.Balance) {
                            case State.RightHigh:
                                node.Balance = State.Balanced;
                                break;
 
                            case State.Balanced:
                                node.Balance = State.LeftHigh;
                                return;
 
                            case State.LeftHigh:
                                if(node.Left.Balance == State.Balanced)
                                    is_shorter = false;

                                if(parent.Balance == State.Header) {
                                    //BalanceLeft(ref parent.Parent);
                                    var x = parent.Parent;
                                    BalanceLeft(ref x);
                                    parent.Parent = x;
                                } else if(AvlCompare(parent.Left, node)) {
                                    //BalanceLeft(ref parent.Left);
                                    var x = parent.Left;
                                    BalanceLeft(ref x);
                                    parent.Left = x;
                                } else {
                                    //BalanceLeft(ref parent.Right);
                                    var x = parent.Right;
                                    BalanceLeft(ref x);
                                    parent.Right = x;
                                }
                                break;
                        }
                    }
 
                    if(is_shorter) {
                        if(parent.Balance == State.Header)
                            return;
                     
                        direction = next;
                        node      = parent;
                    }
                }
            }
            #endregion
            #region SetHeaderNode()
            public void SetHeaderNode(in MemoryPtr address, byte[] buffer) {
                var header     = m_owner.CreateNode(address);
                header.Balance = State.Header;
                header.Left    = header;
                header.Right   = header;
                header.Write(buffer);

                m_header = header;
            }
            #endregion

            #region BinarySearch_GreaterOrEqualTo()
            /// <summary>
            ///    O(log n)
            ///    
            ///    Search the nearest match to your key.
            ///    Returns the smallest result that is at least "key" size.
            ///    
            ///    Returns "1 diff" if not found.
            /// </summary>
            public BinarySearchResult BinarySearch_GreaterOrEqualTo(int key) {
                // this is basically an inlined version of AvlTree + node.Next() to avoid re-reads
                // code intent:
                //     var bsr = this.BinarySearch(key);
                //     if(bsr.Diff <= 0) return bsr;
                //     var node = bsr.Node.Next();
                //     if(m_comparer(key, node.Key) < 0) return new BinarySearchResult(node, -1);
                //     return new BinarySearchResult(null, 1); // not found
                
                m_owner.BumpNodeInCache(m_header.Address);

                var current           = m_header.Parent;
                var prev              = current;
                var prev_diff         = 0;
                var last_greater_than = (Node)null;

                while(current != null) {
                    prev      = current;
                    prev_diff = AvlCompareLength(key, current.Key);
 
                    if(prev_diff > 0)
                        current = current.Right;
                    else if(prev_diff < 0) {
                        last_greater_than = current;
                        current           = current.Left;
                    } else
                        return new BinarySearchResult(current, 0);
                }

                if(prev_diff < 0) // dont do == 0 because this would cover .Count==0 case
                    return new BinarySearchResult(prev, prev_diff);
                else
                    // if all stored values are smaller: last_greater_than == null
                    return new BinarySearchResult(last_greater_than, last_greater_than != null ? -1 : 1);
            }
            public readonly ref struct BinarySearchResult {
                /// <summary>
                ///    -1: key &lt; node.key
                ///     0: key ==   node.key
                ///     1: key &gt; node.key (not found)
                /// </summary>
                public readonly int Diff;
                public readonly Node Node;
                public BinarySearchResult(Node node, int diff) : this() {
                    this.Node = node;
                    this.Diff = diff;
                }
            }
            #endregion

            #region private static BalanceLeft()
            private static void BalanceLeft(ref Node node) {
                var left = node.Left;
 
                switch(left.Balance) {
                    case State.LeftHigh:
                        left.Balance = State.Balanced;
                        node.Balance = State.Balanced;
                        RotateRight(ref node);
                        break;
 
                    case State.RightHigh: 
                        var sub_right = left.Right;
                        switch(sub_right.Balance) {
                            case State.Balanced:
                                left.Balance = State.Balanced;
                                node.Balance = State.Balanced;
                                break;
 
                            case State.RightHigh:
                                left.Balance = State.LeftHigh;
                                node.Balance = State.Balanced;
                                break;
 
                            case State.LeftHigh:
                                left.Balance = State.Balanced;
                                node.Balance = State.RightHigh;
                                break;
                        }
                        sub_right.Balance = State.Balanced;
                        RotateLeft(ref left);
                        node.Left = left;
                        RotateRight(ref node);
                        break;
 
                    case State.Balanced:
                        left.Balance = State.RightHigh;
                        node.Balance = State.LeftHigh;
                        RotateRight(ref node);
                        break;
                }
            }
            #endregion
            #region private static BalanceRight()
            private static void BalanceRight(ref Node node) {
                var right = node.Right;
 
                switch(right.Balance) {
                    case State.RightHigh:
                        right.Balance = State.Balanced;
                        node.Balance  = State.Balanced;
                        RotateLeft(ref node);
                        break;
 
                    case State.LeftHigh:
                        var sub_left = right.Left;
                        switch(sub_left.Balance) {
                            case State.Balanced:
                                right.Balance = State.Balanced;
                                node.Balance  = State.Balanced;
                                break;
 
                            case State.LeftHigh:
                                right.Balance = State.RightHigh;
                                node.Balance  = State.Balanced;
                                break;
 
                            case State.RightHigh:
                                right.Balance = State.Balanced;
                                node.Balance  = State.LeftHigh;
                                break;
                        }
                        sub_left.Balance = State.Balanced;
                        RotateRight(ref right);
                        node.Right = right;
                        RotateLeft(ref node);
                        break;
 
                    case State.Balanced:
                        right.Balance = State.LeftHigh;
                        node.Balance  = State.RightHigh;
                        RotateLeft(ref node);
                        break;
                }
            }
            #endregion
            #region private static RotateLeft()
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void RotateLeft(ref Node node) {
                var right    = node.Right;
                var parent   = node.Parent;
 
                right.Parent = parent;
                node.Parent  = right;
                if(!AvlIsLeftNull(right))
                    right.Left.Parent = node;
 
                node.Right = right.Left;
                right.Left = node;
                node       = right;
            }
            #endregion
            #region private static RotateRight()
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void RotateRight(ref Node node) {
                var left    = node.Left;
                var parent  = node.Parent;
 
                left.Parent = parent;
                node.Parent = left;
                if(!AvlIsRightNull(left))
                    left.Right.Parent = node;
 
                node.Left  = left.Right;
                left.Right = node;
                node       = left;
            }
            #endregion

            #region private enum Direction
            private enum Direction : byte {
                Left,
                Right
            }
            #endregion
        }
        #endregion
    }
}