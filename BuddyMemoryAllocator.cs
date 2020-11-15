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
    ///     Very small overhead footprint. (about 1/8 memory, in fact ~2 bits per 2 bytes)
    ///     Memory alignments at exponents of 2. (i.e.: alloc(3) will take 4 bytes, alloc(6) will take 8 bytes, etc.)
    ///     Smallest memory alignment is 2 bytes.
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
    ///     This class is best suited for small structs types of either variable type or variable size.
    ///     If ultimate memory savings is the goal, using pure structs for everything works, as long as all the types are the same.
    ///     
    ///     Also, this class can only increase capacity by multiplying by 2. There is no finer granularity possible.
    /// </remarks>
    public sealed class BuddyMemoryAllocator {
        private const int MIN_ALLOC_SIZE   = 2; // must be an exponent of 2 (1,2,4,8,16,...)
        private const int DEFAULT_CAPACITY = 4096;

        private Level[] m_levels; // from smallest to biggest
        private ulong[] m_bitmaps;

        // not meant to be safe, just fast
        public byte[] Memory { get; private set; }
        public int Capacity => this.Memory.Length;

        #region constructors
        public BuddyMemoryAllocator(int capacity = DEFAULT_CAPACITY) {
            this.Init(capacity);
        }
        #endregion

        #region Alloc()
        /// <summary>
        /// log(n) worst case
        /// Allocates n bytes and returns a pointer to it.
        /// The returned memory is not zeroed out.
        /// </summary>
        public Ptr Alloc(int size) {
            return this.InternalAlloc(size);
        }
        private Ptr InternalAlloc(int size) {
            var level = CalculateLevel(size);

            if(level < m_levels.Length) {
                ref var level_instance = ref m_levels[level];

                if(level_instance.FreeCount != 0)
                    return AllocOnLevel(level, ref level_instance);

                // if no block exist of the requested size, then try and split a bigger block
                var bigger_level = level + 1;
                while(bigger_level < m_levels.Length && m_levels[bigger_level].FreeCount == 0)
                    bigger_level++;

                if(bigger_level < m_levels.Length) {
                    // split bigger blocks until we have one of the size we need
                    level_instance = ref m_levels[bigger_level];
                    var ptr = AllocOnLevel(bigger_level, ref level_instance);
                    var current_free_address = ptr.Address + level_instance.BlockSize;

                    for(int i = bigger_level - 1; i >= level; i--) {
                        level_instance = ref m_levels[i];
                        level_instance.FreeCount++; // = 1?
                        current_free_address -= level_instance.BlockSize;
                        var x = new Ptr(unchecked((byte)i), current_free_address);
                        var bitmap_index = x.GetBitmapIndex(m_levels);
                        ref var bitmap = ref m_bitmaps[bitmap_index.BitmapsIndex];
                        bitmap |= (ulong)1 << bitmap_index.Shift;
                    }

                    return new Ptr(unchecked((byte)level), ptr.Address);
                }
            }

            // if no free block exists that are bigger than size, then increase capacity
            // typically this will *2 capacity
            this.EnsureCapacity(this.Capacity + size);
            // this recursion will not go deeper than 1 level
            return this.InternalAlloc(size);

            Ptr AllocOnLevel(int lvl, ref Level level_instance) {
                var index = level_instance.BitmapIndex;
                var max   = Math.Max(1, level_instance.BlockCount >> 6);

                // find first free bit
                for(int i = 0; i < max; i++) {
                    ref var bitmap = ref m_bitmaps[index + i];

                    if(bitmap == 0) 
                        continue;

                    var free_bitmap_index = BitScanForward(bitmap);
                    bitmap &= ~((ulong)1 << free_bitmap_index); // mark as used
                    level_instance.FreeCount--;
                    return new Ptr(unchecked((byte)lvl), (i * 64 + free_bitmap_index) * level_instance.BlockSize);
                }
                // should be impossible to make it here
                //throw new ApplicationException($"{this.GetType().Name} state is invalid.");
                return new Ptr();
            }
        }
        #endregion
        #region Free()
        /// <summary>
        /// log(n) worst case
        /// Frees previously allocated memory.
        /// </summary>
        public void Free(Ptr memoryHandle) {
             InternalFree(memoryHandle, m_levels, m_bitmaps);
        }
        private static void InternalFree(Ptr memoryHandle, Level[] levels, ulong[] bitmaps) {
            while(true) {
                var index      = memoryHandle.GetBitmapIndex(levels);
                ref var bitmap = ref bitmaps[index.BitmapsIndex];
                var is_free    = ((bitmap >> index.Shift) & 1) == 1;

                // if freeing already free'd memory, ignore
                if(is_free)
                    return;

                ref var level = ref levels[memoryHandle.Level];

                // if top level, dont recurse
                if(memoryHandle.Level == levels.Length - 1) {
                    level.FreeCount++;
                    bitmap |= (ulong)1 << index.Shift;
                    return;
                }

                var buddy            = new Ptr(memoryHandle.Level, memoryHandle.Address ^ level.BlockSize);
                var buddy_index      = buddy.GetBitmapIndex(levels);
                ref var buddy_bitmap = ref bitmaps[buddy_index.BitmapsIndex];
                var buddy_is_free    = ((buddy_bitmap >> buddy_index.Shift) & 1) == 1;

                if(!buddy_is_free) {
                    // mark as free and were done, no compaction
                    bitmap |= (ulong)1 << index.Shift;
                    level.FreeCount++;
                    // no compaction needed
                    return;
                } else {
                    // if both current and buddy is free, then we need to free one level up instead

                    // mark buddy as used
                    buddy_bitmap &= ~((ulong)1 << buddy_index.Shift);
                    level.FreeCount--;
                    // recurse (eg: compaction)
                    memoryHandle = new Ptr(
                        unchecked((byte)(memoryHandle.Level + 1)), 
                        Math.Min(memoryHandle.Address, buddy.Address));
                }
            }
        }
        #endregion
        #region EnsureCapacity()
        /// <summary>
        /// Ensures at least n capacity is allocated.
        /// This will allocate capacity to powers of 2.
        /// </summary>
        public void EnsureCapacity(int capacity) {
            var level = CalculateLevel(capacity);

            // if remainder, double
            var block_size = GetBlockSize(level);
            if(capacity % block_size != 0) {
                level++;
                capacity = block_size << 1;
            }

            // already have capacity
            if(m_levels != null && this.Capacity >= capacity)
                return;

            level++; // add 1 because we want that level created too

            int bitmap_index = 0;
            var new_levels   = new Level[level];

            for(int i = 0; i < level; i++) {
                block_size         = GetBlockSize(i);
                var old_free_count = i < m_levels.Length ? m_levels[i].FreeCount : 0;
                var block_count    = Math.Max(1, capacity / block_size);
                new_levels[i]      = new Level(bitmap_index, block_size, block_count, old_free_count);
                bitmap_index      += Math.Max(1, block_count >> 6);
            }

            var new_bitmaps = new ulong[bitmap_index];

            // recopy bitmaps
            for(int i = 0; i < m_levels.Length; i++) {
                ref var current = ref m_levels[i];
                if(current.FreeCount == 0)
                    continue;
                Array.Copy(
                    m_bitmaps, 
                    current.BitmapIndex, 
                    new_bitmaps, 
                    current.BitmapIndex, 
                    Math.Max(1, current.BlockCount >> 6));
            }
            // at this point, the new bitmaps think all the new memory is allocated
            // we mark it as free in order to properly assign all values (level[x].FreeCount + bitmaps)
            var memory_freeing_pointer = this.Capacity; // start freeing after old capacity
            for(int i = 0; i < level - m_levels.Length; i++) {
                //  BEFORE             AFTER
                //                       8
                //                    /      \
                //                 4            9
                //               /   \        /   \
                //              2     5     10     13
                //             / \   / \   / \    /  \
                //  1         1   3 6   7 11  12 14  15
                // 
                // 1= old top of tree (/last level)
                // the calls were doing here, which will mark the new memory as free:
                // free(3)
                // free(5)
                // free(9)

                InternalFree(new Ptr(unchecked((byte)(m_levels.Length + i - 1)), memory_freeing_pointer), new_levels, new_bitmaps);
                memory_freeing_pointer += new_levels[m_levels.Length + i - 1].BlockSize;
            }

            var new_memory = new byte[capacity];
            Array.Copy(this.Memory, 0, new_memory, 0, this.Memory.Length);

            this.Memory = new_memory;
            m_levels    = new_levels;
            m_bitmaps   = new_bitmaps;
        }
        #endregion

        #region CalculateUsedMemory()
        public int CalculateUsedMemory() {
            int memory_alloc = this.Memory.Length;
            int free_memory  = this.InternalCalculateFreeMemory();

            return memory_alloc - free_memory;
        }
        #endregion
        #region CalculateFreeMemory()
        public int CalculateFreeMemory() {
            return this.InternalCalculateFreeMemory();
        }
        private int InternalCalculateFreeMemory() {
            int level_count = m_levels.Length;
            int free_memory = 0;

            for(int i = 0; i < level_count; i++) {
                var level    = m_levels[i];
                free_memory += level.FreeCount * level.BlockSize;
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

            // if remainder, double
            var block_size = GetBlockSize(level);
            if(capacity % block_size != 0) {
                level++;
                capacity = block_size << 1;
            }

            level++; // add 1 because we want that level created too

            int bitmap_index = 0;
            m_levels         = new Level[level];
            
            for(int i = 0; i < level; i++) {
                block_size      = GetBlockSize(i);
                var block_count = Math.Max(1, capacity / block_size);
                m_levels[i]     = new Level(bitmap_index, block_size, block_count);
                bitmap_index   += Math.Max(1, block_count >> 6);
            }

            m_bitmaps = new ulong[bitmap_index];

            // mark all as free
            ref var last_level = ref m_levels[m_levels.Length - 1];
            m_bitmaps[last_level.BitmapIndex] = 1;
            last_level.FreeCount = 1;
            this.Memory = new byte[capacity];
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
#if !ALLOW_CPU_INTRINSICS
            var zeroes = 32 - BitScanReverse(size);

            return Math.Max(0, zeroes - MIN_LEVEL - 1);
#else
            // (NETINTRINSICS_NUGET).System.Intrinsic.BitScan(value) or System.Numerics.Vector
            //System.Runtime.Intrinsics.X86.
            return Math.Max(0, );
            throw new NotImplementedException();
#endif
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
            int _bytes = 0;
            if((value & 0x0000_0000_FFFF_FFFFul) != 0) {
                if((value & 0x0000_0000_0000_FFFFul) != 0)
                    _bytes = (value & 0x0000_0000_0000_00FFul) != 0 ? 0 : 1;
                else
                    _bytes = (value & 0x0000_0000_00FF_0000ul) != 0 ? 2 : 3;
            } else {
                if((value & 0x0000_FFFF_0000_0000ul) != 0)
                    _bytes = (value & 0x0000_00FF_0000_0000ul) != 0 ? 4 : 5;
                else
                    _bytes = (value & 0x00FF_0000_0000_0000ul) != 0 ? 6 : 7;
            }
            
            int res = _bytes << 3;
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

        #region internal static Test()
        internal static void Test(int loops, int seed = unchecked((int)0xBADC0FFE)) {
            int sequence  = 0;
            var random    = new Random(seed);
            var allocator = new BuddyMemoryAllocator();
            var reference = new System.Collections.Generic.Dictionary<int, Ptr>();

            for(int i = 0; i < loops; i++) {
                if(i % 1000 == 0) {
                    Console.WriteLine(i);
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
                        allocator.Free(ptr);
                    }
                }
            }

            bool Verify() {
                if(allocator.CalculateUsedMemory() != reference.Count * 4)
                    return false;
                foreach(var item in reference) {
                    var read_value = Decode(item.Value);
                    if(read_value != item.Key)
                        return false;
                }
                return true;
            }
            void Encode(Ptr ptr, int value) {
                var buffer = allocator.Memory;
                buffer[ptr.Address + 0] = (byte)((value >> 0) & 0xFF);
                buffer[ptr.Address + 1] = (byte)((value >> 8) & 0xFF);
                buffer[ptr.Address + 2] = (byte)((value >> 16) & 0xFF);
                buffer[ptr.Address + 3] = (byte)((value >> 24) & 0xFF);
            }
            int Decode(Ptr ptr) {
                var buffer = allocator.Memory;
                return 
                    (buffer[ptr.Address + 0] << 0) |
                    (buffer[ptr.Address + 1] << 8) |
                    (buffer[ptr.Address + 2] << 16) |
                    (buffer[ptr.Address + 3] << 24);
            }
        }
        #endregion

        /// <summary>
        /// MemoryHandle / Pointer
        /// </summary>
        public readonly struct Ptr : IEquatable<Ptr> {
            internal readonly byte Level;
            public readonly int Address; // this is an int and not a long because the algorithm is not meant to handle large memory allocs

            #region constructors
            internal Ptr(byte level, int address) : this() {
                this.Level   = level;
                this.Address = address;
            }
            #endregion

            #region internal GetBitmapIndex()
            internal BitmapIndex GetBitmapIndex(Level[] levels) {
                ref var level = ref levels[this.Level];
                var temp = this.Address / level.BlockSize;
                return new BitmapIndex(
                    level.BitmapIndex + (temp >> 6),
                    unchecked((byte)(temp % 64)));
            }
            internal readonly ref struct BitmapIndex {
                public readonly int BitmapsIndex;
                public readonly byte Shift; // bitshift within ulong

                public BitmapIndex(int bitmapsIndex, byte shift) : this() {
                    this.BitmapsIndex = bitmapsIndex;
                    this.Shift        = shift;
                }
            }
            #endregion
            
            #region Equals()
            public bool Equals(Ptr other) {
                return this.Address == other.Address && this.Level == other.Level;
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
                return (this.Address, this.Level).GetHashCode();
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return string.Format("[{0}] {1}",
                    this.Level.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    this.Address.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            #endregion
        }

        internal struct Level {
            public readonly int BitmapIndex;
            public readonly int BlockSize;
            public readonly int BlockCount;
            public int FreeCount;

            #region constructors
            public Level(int bitmap_index, int block_size, int block_count) : this() {
                this.BitmapIndex = bitmap_index;
                this.BlockSize   = block_size;
                this.BlockCount  = block_count;
                //this.FreeCount = 0;
            }
            public Level(int bitmap_index, int block_size, int block_count, int free_count) : this(bitmap_index, block_size, block_count) {
                this.FreeCount = free_count;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return $"[{this.FreeCount}x {this.BlockSize} bytes] free ({this.BlockCount} blocks)";
            }
            #endregion
        }
    }
}
