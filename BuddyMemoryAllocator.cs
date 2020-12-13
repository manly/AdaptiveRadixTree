//#define ALLOW_CPU_INTRINSICS

namespace System.Collections.Specialized 
{
    /// <summary>
    ///     Not thread safe.
    ///     Allocates managed memory using the buddy memory allocation algorithm.
    ///     
    ///     This implementation is meant for small allocations (i.e.: 24- bytes) and ideally 2-8 bytes allocations.
    ///     While it would work on bigger allocations, a dynamic allocator would be better suited to avoid heavy fragmentation.
    ///     
    ///     log(n) alloc/free.
    ///     Very small overhead footprint. (about 1/8 memory, in fact ~2 bits per 2 bytes, or more precisely, ~2 bits per MIN_ALLOC_SIZE)
    ///     Memory alignments at exponents of 2. (i.e.: alloc(3) will take 4 bytes, alloc(6) will take 8 bytes, etc.)
    ///     Smallest alloc is 2 bytes.
    ///     
    ///     https://en.wikipedia.org/wiki/Buddy_memory_allocation
    /// </summary>
    /// <remarks>
    ///     Motivation:
    ///     64 bits references in .NET have a 16 bytes overhead, as well as memory alignments of 8 bytes.
    ///     Consequently, the smallest class instance possible takes 24 bytes of memory.
    ///     When doing a lot of small allocs, this adds up quickly.
    ///     32 bits references have 8 bytes overhead, with 4 bytes alignment, resulting in 12 bytes being the smallest alloc possible.
    ///     
    ///     This class is best suited for small structs, variable-sized structs, or dynamicly-typed structs.
    ///     If ultimate memory savings is the goal, using pure structs for everything works, as long as all the types are the same.
    /// </remarks>
    public sealed class BuddyMemoryAllocator {
        private const int MIN_ALLOC_SIZE              = 2; // must be an exponent of 2 (1,2,4,8,16,...)
        private const int FREE_BLOCKS_CACHE_PER_LEVEL = 4; // # of free blocks cached per level

        private Level[] m_levels; // from smallest to biggest
        private ulong[] m_bitmaps;
        private readonly FreeBlockCache m_freeBlockCache;

        private MemoryChunk[] m_chunks;
        private int m_chunkCount;

        public int Capacity { get; private set; }

        #region constructors
        public BuddyMemoryAllocator(int capacity = DEFAULT_MIN_CHUNK_SIZE) {
            m_freeBlockCache = new FreeBlockCache();
            this.Init(DEFAULT_MIN_CHUNK_SIZE); // do not change this unless you really know what you're doing
            this.EnsureCapacity(capacity);
        }
        #endregion

        #region Alloc()
        /// <summary>
        /// log(n) worst case
        /// Allocates n bytes and returns a pointer to it.
        /// The returned memory is not zeroed out.
        /// </summary>
        public PtrExtended Alloc(int size) {
            return this.InternalAlloc(size);
        }
        private PtrExtended InternalAlloc(int size) {
            var level = CalculateLevel(size) - 1;

            if(level < m_levels.Length) {
                ref var level_instance = ref m_levels[level];

                if(level_instance.FreeBlocks != 0)
                    return this.AllocOnLevel(level, ref level_instance);

                // if no block exist of the requested size, then try and split a bigger block
                var bigger_level = level + 1;
                while(bigger_level < m_levels.Length && m_levels[bigger_level].FreeBlocks == 0)
                    bigger_level++;

                if(bigger_level < m_levels.Length) {
                    // split bigger blocks until we have one of the size we need
                    level_instance           = ref m_levels[bigger_level];
                    var ptr                  = this.AllocOnLevel(bigger_level, ref level_instance);
                    var allocated_address    = m_chunks[ptr.Ptr.ChunkID].MemoryPosition + ptr.Ptr.Address;
                    var current_free_address = allocated_address + level_instance.BlockSize;

                    for(int i = bigger_level - 1; i >= level; i--) {
                        level_instance                = ref m_levels[i];
                        current_free_address         -= level_instance.BlockSize;
                        var temp                      = current_free_address / level_instance.BlockSize;
                        var bitmap_index_BitmapsIndex = level_instance.BitmapIndex + (temp >> 6);
                        var bitmap_index_Shift        = temp % 64;
                        ref var bitmap                = ref m_bitmaps[bitmap_index_BitmapsIndex];
                        bitmap                       |= (ulong)1 << bitmap_index_Shift;
                        m_freeBlockCache.Add(i, (bitmap_index_BitmapsIndex << 6) + bitmap_index_Shift);
                        level_instance.FreeBlocks++; // = 1?
                    }

                    var chunkID = this.BinarySearchChunk(level_instance.ChunkID, allocated_address, out var chunk);
                    var address = allocated_address - chunk.MemoryPosition;

                    return new PtrExtended(
                        new Ptr(level, chunkID, address),
                        chunk.Memory);
                }
            }

            // if no free block exists that are bigger than size, then increase capacity
            // dont use "this.EnsureCapacity(this.Capacity + size);" because it doesnt guarantee we will get all the allocated memory consecutively
            this.IncreaseCapacityUntilOneFreeLevelExists(level + 1);

            // this recursion will not go deeper than 1 level
            return this.InternalAlloc(size);
        }
        private PtrExtended AllocOnLevel(int lvl, ref Level level_instance) {
            // try cache first
            var cached_block = m_freeBlockCache.Pop(lvl);
            if(cached_block >= 0) {
                var memory_position = (cached_block - (level_instance.BitmapIndex << 6)) * level_instance.BlockSize;
                var chunkID         = this.BinarySearchChunk(level_instance.ChunkID, memory_position, out var chunk);
                var address         = memory_position - chunk.MemoryPosition;

                ref var bitmap = ref m_bitmaps[cached_block >> 6]; // level_instance.BitmapIndex + ...
                bitmap &= ~((ulong)1 << (cached_block % 64)); // mark as used
                //m_freeBlockCache.Remove(lvl, cached_block); // pop() removed it
                level_instance.FreeBlocks--;

                return new PtrExtended(
                    new Ptr(lvl, chunkID, address),
                    chunk.Memory);
            } else {
                var index = level_instance.BitmapIndex;
                var max   = Math.Max(1, level_instance.BlockCount >> 6);
                var chunk = m_chunks[level_instance.ChunkID]; // dont use "ref" here because we do "out chunk" later on
                int start = (chunk.MemoryPosition / level_instance.BlockSize) >> 6; // skip the parts where chunks cant even allocate the requested size

                // find first free bit
                for(int i = start; i < max; i++) {
                    ref var bitmap = ref m_bitmaps[index + i];
                    if(bitmap == 0) 
                        continue;

                    var free_bitmap_index = BitScanForward(bitmap);
                    bitmap &= ~((ulong)1 << free_bitmap_index); // mark as used
                    m_freeBlockCache.Remove(lvl, ((index + i) << 6) + free_bitmap_index);
                    level_instance.FreeBlocks--;

                    var memory_position = (i * 64 + free_bitmap_index) * level_instance.BlockSize;
                    var chunkID         = this.BinarySearchChunk(level_instance.ChunkID, memory_position, out chunk);
                    var address         = memory_position - chunk.MemoryPosition;

                    return new PtrExtended(
                        new Ptr(lvl, chunkID, address),
                        chunk.Memory);
                }
                // should be impossible to make it here
                //throw new ApplicationException($"{this.GetType().Name} state is invalid.");
                return new PtrExtended();
            }
        }
        private int BinarySearchChunk(int min, int memory_position, out MemoryChunk chunk) {
            int max = m_chunkCount - 1;

            while(min <= max) {
                int median     = (min + max) >> 1;
                var temp_chunk = m_chunks[median];
                var diff       = temp_chunk.MemoryPosition - memory_position;

                if(diff < 0)
                    min = median + 1;
                else if(diff > 0)
                    max = median - 1;
                else {
                    chunk = temp_chunk;
                    return median;
                }
            }

            var res = min - 1;
            chunk = m_chunks[res];
            return res;
        }
        #endregion
        #region Free()
        /// <summary>
        /// log(n) worst case
        /// Frees previously allocated memory.
        /// </summary>
        public void Free(Ptr memoryHandle) {
            InternalFree(memoryHandle, m_levels, m_bitmaps, m_chunks, m_freeBlockCache);
        }
        private static void InternalFree(Ptr memoryHandle, Level[] levels, ulong[] bitmaps, MemoryChunk[] chunks, FreeBlockCache freeBlockCache) {
            var chunkID = memoryHandle.ChunkID;
            var chunk   = chunks[chunkID];

            while(true) {
                ref var level          = ref levels[memoryHandle.Level];
                var temp               = (chunk.MemoryPosition + memoryHandle.Address) / level.BlockSize;
                var index_BitmapsIndex = level.BitmapIndex + (temp >> 6);
                var index_Shift        = temp % 64;
                ref var bitmap         = ref bitmaps[index_BitmapsIndex];
                var is_free            = ((bitmap >> index_Shift) & 1) == 1;

                // if freeing already free'd memory, ignore
                if(is_free)
                    return;

                // if top level, dont recurse
                if(memoryHandle.Level == levels.Length - 1) {
                    bitmap |= (ulong)1 << index_Shift;
                    freeBlockCache.Add(memoryHandle.Level, (index_BitmapsIndex << 6) + index_Shift);
                    level.FreeBlocks++;
                    return;
                }

                var memory_position        = chunk.MemoryPosition + memoryHandle.Address;
                var buddy_memory_position  = memory_position ^ level.BlockSize;
                var is_buddy_in_same_chunk = chunk.Contains(buddy_memory_position, level.BlockSize);

                if(is_buddy_in_same_chunk) {
                    temp                         = buddy_memory_position / level.BlockSize;
                    var buddy_index_BitmapsIndex = level.BitmapIndex + (temp >> 6);
                    var buddy_index_Shift        = temp % 64;
                    ref var buddy_bitmap         = ref bitmaps[buddy_index_BitmapsIndex];
                    var buddy_is_free            = ((buddy_bitmap >> buddy_index_Shift) & 1) == 1;

                    if(buddy_is_free) {
                        // if both current and buddy is free, then we need to free one level up instead

                        // mark buddy as used
                        buddy_bitmap &= ~((ulong)1 << buddy_index_Shift);
                        freeBlockCache.Remove(memoryHandle.Level, (buddy_index_BitmapsIndex << 6) + buddy_index_Shift);
                        level.FreeBlocks--;
                        // recurse (eg: compaction)
                        memoryHandle = new Ptr(
                            memoryHandle.Level + 1, 
                            chunkID,
                            Math.Min(memoryHandle.Address, buddy_memory_position - chunk.MemoryPosition));
                        continue;
                    }
                }

                // case "buddy_is_free == false"
                // mark as free and were done, no compaction
                bitmap |= (ulong)1 << index_Shift;
                freeBlockCache.Add(memoryHandle.Level, (index_BitmapsIndex << 6) + index_Shift);
                level.FreeBlocks++;
                // no compaction needed
                return;
            }
        }
        #endregion
        #region EnsureCapacity()
        /// <summary>
        /// Ensures at least n capacity is allocated.
        /// </summary>
        public void EnsureCapacity(int capacity) {
            this.IncreaseCapacityTo(capacity);
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

        #region CalculateUsedMemory()
        public long CalculateUsedMemory() {
            var memory_alloc = this.Capacity;
            var free_memory  = this.InternalCalculateFreeMemory();

            return memory_alloc - free_memory;
        }
        #endregion
        #region CalculateFreeMemory()
        public long CalculateFreeMemory() {
            return this.InternalCalculateFreeMemory();
        }
        private long InternalCalculateFreeMemory() {
            int level_count = m_levels.Length;
            long free_memory = 0;

            for(int i = 0; i < level_count; i++) {
                var level    = m_levels[i];
                free_memory += level.FreeBlocks * level.BlockSize;
            }

            return free_memory;
        }
        #endregion

        #region private Init()
        /// <summary>
        /// If memory allocated is less than capacity, then allocates to that.
        /// </summary>
        private void Init(int capacity) {
            var level = CalculateLevel(capacity);

            m_chunks     = new MemoryChunk[4];
            m_chunkCount = 1;
            m_chunks[0]  = new MemoryChunk(new byte[capacity], 0);

            int bitmap_index = 0;
            m_levels         = new Level[level];
            
            for(int i = 0; i < level; i++) {
                var block_size  = GetBlockSize(i);
                var block_count = Math.Max(1, capacity / block_size);
                m_levels[i]     = new Level(bitmap_index, block_size, block_count, 0, 0);
                bitmap_index   += Math.Max(1, block_count >> 6);
            }

            m_bitmaps = new ulong[bitmap_index];

            // mark all as free
            ref var last_level = ref m_levels[m_levels.Length - 1];
            m_bitmaps[last_level.BitmapIndex] = 1;
            last_level.FreeBlocks = 1;

            m_freeBlockCache.SupportLevel(m_levels.Length - 1);
            m_freeBlockCache.Add(m_levels.Length - 1, last_level.BitmapIndex << 6);

            this.Capacity = capacity;
        }
        #endregion
        #region private IncreaseCapacityBy()
        private void IncreaseCapacityBy(int capacity) {
            if(capacity < m_chunks[m_chunkCount - 1].Length)
                throw new InvalidOperationException($"{nameof(capacity)} ({capacity}) must be >= last chunk size ({m_chunks[m_chunkCount - 1].Length}).");

            // do this first to make sure no OutOfMemoryException()
            // create new chunk
            var new_memory = new byte[capacity];

            if(m_chunkCount == m_chunks.Length)
                Array.Resize(ref m_chunks, m_chunks.Length * 2);

            var chunk              = new MemoryChunk(new_memory, this.Capacity);
            m_chunks[m_chunkCount] = chunk;

            // recreate levels
            int bitmap_index   = 0;
            var level          = CalculateLevel(capacity);
            var new_levels     = new Level[level];
            var total_capacity = this.Capacity + capacity;

            m_freeBlockCache.SupportLevel(level);

            for(int i = 0; i < level; i++) {
                var block_size      = GetBlockSize(i);
                var old_free_blocks = i < m_levels.Length ? m_levels[i].FreeBlocks : 0;
                var chunkID         = i < m_levels.Length ? m_levels[i].ChunkID : m_chunkCount;
                var block_count     = Math.Max(1, total_capacity / block_size);
                new_levels[i]       = new Level(bitmap_index, block_size, block_count, chunkID, old_free_blocks);
                bitmap_index       += Math.Max(1, block_count >> 6);
            }

            var new_bitmaps = new ulong[bitmap_index];

            // recopy bitmaps
            for(int i = 0; i < m_levels.Length; i++) {
                ref var current = ref m_levels[i];
                if(current.FreeBlocks == 0)
                    continue;
                Array.Copy(
                    m_bitmaps, 
                    current.BitmapIndex, 
                    new_bitmaps, 
                    new_levels[i].BitmapIndex, 
                    Math.Max(1, current.BlockCount >> 6));
            }
            // at this point, the new bitmaps think all the new memory is allocated
            // we mark it as free in order to properly assign all values (level[x].FreeBlocks + bitmaps)
            var memory_freeing_pointer = this.Capacity; // start freeing after old capacity

            int current_level = level - 1;
            while(memory_freeing_pointer < total_capacity) {
                var address = memory_freeing_pointer - chunk.MemoryPosition;
                InternalFree(
                    new Ptr(current_level, m_chunkCount, address),
                    new_levels, 
                    new_bitmaps, 
                    m_chunks,
                    m_freeBlockCache);
                memory_freeing_pointer += new_levels[current_level].BlockSize;
                current_level--;
            }
            
            m_chunkCount++;
            m_levels       = new_levels;
            m_bitmaps      = new_bitmaps;
            this.Capacity += capacity;
        }
        #endregion
        #region private IncreaseCapacityTo()
        private void IncreaseCapacityTo(int capacity) {
            // this intentionally just creates new chunks of CalculateNewChunkSize() recommended growing algorithm
            // this will intentionally not create just the requested capacity size, since we want to pre-alloc memory anyway
            // the danger of just creating "GetBlockSize(CalculateLevel(capacity))" is that that will force rounding alloc to powers of 2
            // this is fine for small allocs, but when you request for example 33 MB, you dont want it to reserve 64 MB
            // also theres an indirect advantage to do "jagged" allocs like this makes a more spread out availability of blocks, 
            // which allows faster log(n) allocs/frees since it doesnt need to merge up as high

            // note that if you need consecutive memory, which this will not do, Alloc() will take care of that
            
            while(this.Capacity < capacity) {
                // by setting level 0, we're indicating that we dont need to reach a specific blocksize, but just give a standard capacity increase
                var chunk_size = this.CalculateNewChunkSize(0);
                this.IncreaseCapacityBy(chunk_size);
            }
        }
        #endregion
        #region private IncreaseCapacityUntilOneFreeLevelExists()
        /// <summary>
        /// Increase capacity until we get one blocksize available of the given level.
        /// </summary>
        private void IncreaseCapacityUntilOneFreeLevelExists(int level) {
            do {
                // try to increase to the requested level (ie: keep increasing capacity until the one block of the requested size exists
                var chunk_size = this.CalculateNewChunkSize(level);
                this.IncreaseCapacityBy(chunk_size);
            } while(m_levels.Length < level);
        }
        #endregion
        #region private CalculateNewChunkSize()
        private const int DEFAULT_MIN_CHUNK_SIZE = 4096;     // do not change this value unless you know what you're doing.
        private const int DEFAULT_MAX_CHUNK_SIZE = 16777216; // 16MB
        private const int DEFAULT_MIN_CHUNK_SIZE_SHIFT = 12; // 4096

        private int CalculateNewChunkSize(int level) {
            // the chunk grow algorithm has to follow the chunk size and level blocksize together
            // otherwise we get alignment issues between chunks and levels, and that makes it complicated to use well afterwards
            // ie: chunk(4096), chunk(8192), then the 8192 blocksize level will start in the middle of the 2nd chunk

            // chunk size must always be >= last chunk size because levels expect further chunks to be able to contain their level

            // in short, the goal is to create this:
            // chunk[0] = 4096
            // chunk[1] = 4096
            // chunk[2] = 8192
            // chunk[3] = 16384
            // ...

            var block_size = GetBlockSize(level);
            var last_chunk = m_chunks[m_chunkCount - 1];

            var requested = Math.Max(
                block_size,
                last_chunk.Length);

            var suggested = Math.Min(
                DEFAULT_MIN_CHUNK_SIZE << Math.Min(m_chunkCount - 1, 30 - DEFAULT_MIN_CHUNK_SIZE_SHIFT),
                DEFAULT_MAX_CHUNK_SIZE);

            if(suggested >= requested)
                return suggested;
            else if(block_size <= last_chunk.Length)
                return requested;
            else { // ie: level >= m_levels.Length
                // if were requesting a blocksize bigger than the next level, then we need to gradually ease into it
                // in other words, we need to ensure the data aligns between chunk size and level

                // keep in mind, due to 16MB default limit, we could have multiple 16MB blocks without necessarily a level for 32MB
                // because of this we need to be mindful of alignments

                var last_level = m_levels[m_levels.Length - 1];

                if(last_level.BlockCount % 2 == 0)
                    // if we dont have alignment issues, then create a new level
                    return last_chunk.Length * 2;
                else
                    // if we will have alignment issue making the new level, then we can't increase level until we finish alignment on this current level
                    // in other words, we can't create a 32K chunk if we currently only have one 16K chunk
                    return last_chunk.Length;
            }
        }
        #endregion

        #region private static CalculateLevel()
        // this gets optimized into a const anyway. 
        // Any value greater than 16 don't make sense as they would take more than a normal .NET alloc.
        private const int MIN_LEVEL = 
            MIN_ALLOC_SIZE == 1 ? 0 : 
            MIN_ALLOC_SIZE == 2 ? 1 : 
            MIN_ALLOC_SIZE == 4 ? 2 : 
            MIN_ALLOC_SIZE == 8 ? 3 : 
            MIN_ALLOC_SIZE == 16 ? 4 : -1;

        private static int CalculateLevel(int size) {
            var non_zeroes = 32 - BitScanReverse(size);
            
            // if we have size=3, then non_zeroes=2 (which means the 2 least significant bits are used)
            // normally this means 1 bit is used for level (due to level -1 below)
            // in this case we want to signal to interpret it back to a 2 bits used since we need to round up
            var mask = (1 << (non_zeroes - 1)) - 1;
            if((size & mask) != 0)
                non_zeroes++;

            return Math.Max(0, non_zeroes - MIN_LEVEL);

            //#if !ALLOW_CPU_INTRINSICS
            //// code above
            //#else
            //// (NETINTRINSICS_NUGET).System.Intrinsic.BitScan(value) or System.Numerics.Vector
            ////System.Runtime.Intrinsics.X86.
            //return Math.Max(0, );
            //throw new NotImplementedException();
            //#endif
        }
        #endregion
        #region private static GetBlockSize()
        private static int GetBlockSize(int level) {
            return (int)1 << (level + MIN_LEVEL);
        }
        #endregion

        #region private static BitScanReverse()
        /// <summary>
        ///     Counts the number of bits containing zeroes in the most (higher/left side) significant parts of the value.
        ///     ex: 0x0000_FF00_0000_0000 = 16.
        /// </summary>
        private static int BitScanReverse(int value) {
#if ALLOW_CPU_INTRINSICS
            return System.Intrinsic.BitScanReverse(value);
#else
            int level = 0;

            while(value > 0) { // ">0" and not "!=0" because it would infiniteloop on negative values
                value >>= 1;
                level++;
            }
            
            return 32 - level;
#endif
        }
        #endregion
        #region private static BitScanForward()
        /// <summary>
        ///     Counts the number of bits containing zeroes in the least (lower/right side) significant parts of the value.
        ///     ex: 0x0000_FF00_0000_0000 = 40.
        /// </summary>
        private static int BitScanForward(ulong value) {
#if ALLOW_CPU_INTRINSICS
            return System.Intrinsic.BitScanForward(value);
#else
            int _bits = 0;
            if((value & 0x0000_0000_FFFF_FFFFul) != 0) {
                if((value & 0x0000_0000_0000_FFFFul) != 0)
                    _bits = (value & 0x0000_0000_0000_00FFul) != 0 ? 0 : 8;
                else
                    _bits = (value & 0x0000_0000_00FF_0000ul) != 0 ? 16 : 24;
            } else {
                if((value & 0x0000_FFFF_0000_0000ul) != 0)
                    _bits = (value & 0x0000_00FF_0000_0000ul) != 0 ? 32 : 40;
                else
                    _bits = (value & 0x00FF_0000_0000_0000ul) != 0 ? 48 : 56;
            }
            
            int res = _bits;
            value >>= res;

            // use for(8) to let the compiler unroll this
            for(int i = 0; i < 8; i++) {
                if((value & 1) == 1)
                    break;
                value >>= 1;
                res++;
            }

            return res;
#endif
        }
        #endregion

        #region static Test()
        public static void Test(int loops, int seed = unchecked((int)0xBADC0FFE)) {
            int sequence  = 0;
            var random    = new Random(seed);
            var allocator = new BuddyMemoryAllocator();
            var reference = new System.Collections.Generic.Dictionary<int, PtrExtended>();

            //allocator.EnsureCapacity(100000);
            //allocator.Alloc(100000);

            var start = DateTime.UtcNow;

            for(int i = 0; i < loops; i++) {
                if(i % 1000 == 0) {
                    Console.WriteLine($"{i} {DateTime.UtcNow - start}");
                    if(!Verify())
                        System.Diagnostics.Debugger.Break();
                }

                var rng = random.NextDouble();
                if(rng <= 0.60) {
                    var ptr = allocator.Alloc(4);
                    reference.Add(sequence, ptr);
                    Encode(ptr, sequence++);
                } else if(reference.Count > 0) {
                    var rng_key = random.Next(0, sequence);
                    if(reference.TryGetValue(rng_key, out var ptr)) {
                        reference.Remove(rng_key);
                        allocator.Free(ptr.Ptr);
                    }
                }
            }

            Console.WriteLine($"{loops} {DateTime.UtcNow - start}");

            bool Verify() {
                if(allocator.CalculateUsedMemory() != reference.Count * 4)
                    return false;
                foreach(var item in reference) {
                    var read_value = Decode(item.Value);
                    if(read_value != item.Key)
                        return false;
                }
                // check cache
                foreach(var item in allocator.m_freeBlockCache.GetItems()) {
                    var level_instance  = allocator.m_levels[item.level];
                    var memory_position = (item.cached_block - (level_instance.BitmapIndex << 6)) * level_instance.BlockSize;

                    var bitmap  = allocator.m_bitmaps[item.cached_block >> 6]; // level_instance.BitmapIndex + ...
                    var is_free = (bitmap & ((ulong)1 << (item.cached_block % 64))) != 0;
                    if(!is_free)
                        return false;
                }
                return true;
            }
            void Encode(in PtrExtended ptr, int value) {
                var buffer = ptr.Memory;
                buffer[ptr.Ptr.Address + 0] = (byte)((value >> 0) & 0xFF);
                buffer[ptr.Ptr.Address + 1] = (byte)((value >> 8) & 0xFF);
                buffer[ptr.Ptr.Address + 2] = (byte)((value >> 16) & 0xFF);
                buffer[ptr.Ptr.Address + 3] = (byte)((value >> 24) & 0xFF);
            }
            int Decode(in PtrExtended ptr) {
                var buffer = ptr.Memory;
                return 
                    (buffer[ptr.Ptr.Address + 0] << 0) |
                    (buffer[ptr.Ptr.Address + 1] << 8) |
                    (buffer[ptr.Ptr.Address + 2] << 16) |
                    (buffer[ptr.Ptr.Address + 3] << 24);
            }
        }
        #endregion

        #region private struct Level
        private struct Level {
            /// <summary>
            /// Index in m_bitmaps where this level blocks bitarray starts
            /// </summary>
            public readonly int BitmapIndex;
            /// <summary>
            /// The size of blocks (ie: allocated size)
            /// </summary>
            public readonly int BlockSize;
            /// <summary>
            /// Number of blocks
            /// </summary>
            public readonly int BlockCount;
            /// <summary>
            /// The first index in m_chunks where chunk.Length >= BlockSize
            /// </summary>
            public readonly int ChunkID;

            /// <summary>
            /// Number of free blocks
            /// </summary>
            public int FreeBlocks;

            #region constructors
            public Level(int bitmap_index, int block_size, int block_count, int chunkID, int free_blocks) : this() {
                this.BitmapIndex = bitmap_index;
                this.BlockSize   = block_size;
                this.BlockCount  = block_count;
                this.ChunkID     = chunkID;
                this.FreeBlocks  = free_blocks;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return $"[{this.FreeBlocks}x {this.BlockSize} bytes] free ({this.BlockCount} blocks)";
            }
            #endregion
        }
        #endregion
        #region private struct MemoryChunk
        private readonly struct MemoryChunk {
            public readonly byte[] Memory;
            /// <summary>
            /// The cumulative position within the memory chunks.
            /// Essentially sum(previous_chunks.Length)
            /// </summary>
            public readonly int MemoryPosition;
            /// <summary>
            /// Memory.Length
            /// </summary>
            public readonly int Length;

            #region constructors
            public MemoryChunk(byte[] memory, int memory_position) {
                this.Memory         = memory;
                this.Length         = memory.Length;
                this.MemoryPosition = memory_position;
            }
            #endregion
            #region Contains()
            public bool Contains(int memory_position, int length) {
                return memory_position >= this.MemoryPosition && 
                    memory_position + length < this.MemoryPosition + this.Length;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return $"@{this.MemoryPosition} ({this.Length} bytes)";
            }
            #endregion
        }
        #endregion
        #region private class FreeBlockCache
        private class FreeBlockCache {
            private int[] m_cache;

            public FreeBlockCache() : base(){
                m_cache = new int[FREE_BLOCKS_CACHE_PER_LEVEL * 16];
                for(int i = 0; i < m_cache.Length; i++)
                    m_cache[i] = -1;
            }

            public void SupportLevel(int levels) {
                var size = levels * FREE_BLOCKS_CACHE_PER_LEVEL;
                if(m_cache.Length < size) {
                    var old_size = m_cache.Length;
                    Array.Resize(ref m_cache, size);
                    for(int i = old_size; i < size; i++)
                        m_cache[i] = -1;
                }
            }

            public void Add(int level, int block) {
                var index = level * FREE_BLOCKS_CACHE_PER_LEVEL;
                for(int i = 0; i < FREE_BLOCKS_CACHE_PER_LEVEL; i++) {
                    ref var x = ref m_cache[index + i];
                    if(x < 0) { // if free
                        x = block;
                        break;
                    }
                }
            }
            public void Remove(int level, int block) {
                var index = level * FREE_BLOCKS_CACHE_PER_LEVEL;
                for(int i = 0; i < FREE_BLOCKS_CACHE_PER_LEVEL; i++) {
                    ref var x = ref m_cache[index + i];
                    if(x == block) {
                        x = -1;
                        break;
                    }
                }
            }
            public int Pop(int level) {
                var index = level * FREE_BLOCKS_CACHE_PER_LEVEL;
                for(int i = 0; i < FREE_BLOCKS_CACHE_PER_LEVEL; i++) {
                    ref var x = ref m_cache[index + i];
                    if(x >= 0) {
                        var res = x;
                        x = -1;
                        return res;
                    }
                }
                return -1;
            }
            internal System.Collections.Generic.IEnumerable<(int level, int cached_block)> GetItems() {
                for(int i = 0; i < m_cache.Length; i++) {
                    var x = m_cache[i];
                    if(x >= 0)
                        yield return (i / FREE_BLOCKS_CACHE_PER_LEVEL, x);
                }
            }
        }
        #endregion

        /// <summary>
        /// MemoryHandle / Pointer
        /// </summary>
        public readonly struct Ptr : IEquatable<Ptr> {
            internal readonly int ChunkIdAndLevel;
            public readonly int Address;

            internal int Level => this.ChunkIdAndLevel >> 24;          // the allocated size
            internal int ChunkID => this.ChunkIdAndLevel & 0x00FFFFFF; // the level containing the memory

            #region constructors
            internal Ptr(int level, int chunkID, int address) : this() {
                this.ChunkIdAndLevel = (chunkID & 0x00FFFFFF) | ((level & 0xFF) << 24);
                this.Address         = address;
            }
            internal Ptr(int chunkIDAndLevel, int address) : this() {
                this.ChunkIdAndLevel = chunkIDAndLevel;
                this.Address         = address;
            }
            #endregion

            #region static Read()
            public static Ptr Read(byte[] buffer, int index) {
                var chunkIdAndLevel = 
                    (buffer[index + 0] << 0) |
                    (buffer[index + 1] << 8) |
                    (buffer[index + 2] << 16) |
                    (buffer[index + 3] << 24);
                var address = 
                    (buffer[index + 4] << 0) |
                    (buffer[index + 5] << 8) |
                    (buffer[index + 6] << 16) |
                    (buffer[index + 7] << 24);
                return new Ptr(chunkIdAndLevel, address);
            }
            #endregion
            #region Write()
            public void Write(byte[] buffer, int index) {
                buffer[index + 0] = unchecked((byte)((this.ChunkIdAndLevel >> 0) & 0xFF));
                buffer[index + 1] = unchecked((byte)((this.ChunkIdAndLevel >> 8) & 0xFF));
                buffer[index + 2] = unchecked((byte)((this.ChunkIdAndLevel >> 16) & 0xFF));
                buffer[index + 3] = unchecked((byte)((this.ChunkIdAndLevel >> 24) & 0xFF));
                buffer[index + 4] = unchecked((byte)((this.Address >> 0) & 0xFF));
                buffer[index + 5] = unchecked((byte)((this.Address >> 8) & 0xFF));
                buffer[index + 6] = unchecked((byte)((this.Address >> 16) & 0xFF));
                buffer[index + 7] = unchecked((byte)((this.Address >> 24) & 0xFF));
            }
            #endregion

            #region Equals()
            public bool Equals(Ptr other) {
                return this.Address == other.Address && this.ChunkIdAndLevel == other.ChunkIdAndLevel;
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
                return (this.Address, this.ChunkIdAndLevel).GetHashCode();
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return string.Format("[level:{0}, chunk:{1}] {2}",
                    this.Level.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
                return this.Ptr == other.Ptr; // && this.Memory == other.Memory
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
    }
}
