namespace System.Collections.Specialized 
{
    /// <summary>
    ///     Not thread safe.
    ///     Allocates managed memory.
    ///     This is a generic managed MemoryAllocator which uses a BuddyMemoryAllocator and a AvlTreeMemoryAllocator under the hood.
    /// </summary>
    /// <remarks>
    ///     Motivation:
    ///     64 bits references in .NET have a 16 bytes overhead, as well as memory alignments of 8 bytes.
    ///     Consequently, the smallest class instance possible takes 24 bytes of memory.
    ///     When doing a lot of small allocs, this adds up quickly.
    ///     32 bits references have 8 bytes overhead, with 4 bytes alignment, resulting in 12 bytes being the smallest alloc possible.
    /// </remarks>
    public sealed class MemoryAllocator {
        private const int DEFAULT_CAPACITY = 4096;
        private const int BUDDY_ALLOC_SIZE_MAX = 16;

        private readonly BuddyMemoryAllocator m_buddyAllocator; // for small allocs
        private readonly AvlTreeMemoryAllocator m_avlAllocator; // for var-sized allocs

        #region constructors
        public MemoryAllocator(int capacity = DEFAULT_CAPACITY) {
            m_buddyAllocator = new BuddyMemoryAllocator(capacity);
            m_avlAllocator   = new AvlTreeMemoryAllocator(capacity);
        }
        #endregion

        #region Alloc()
        /// <summary>
        ///     The returned memory is not zeroed out.
        /// </summary>
        public PtrExtended Alloc(int size) {
            if(size <= BUDDY_ALLOC_SIZE_MAX)
                return m_buddyAllocator.Alloc(size);
            else
                return m_avlAllocator.Alloc(size);
        }
        #endregion
        #region Free()
        /// <summary>
        ///     Frees previously allocated memory.
        /// </summary>
        public void Free(Ptr memoryHandle) {
            if(memoryHandle.Allocator)
                m_avlAllocator.Free(memoryHandle);
            else
                m_buddyAllocator.Free(memoryHandle);
        }
        #endregion

        #region GetMemory()
        /// <summary>
        ///     Returns the memory backing the ptr.
        /// </summary>
        public byte[] GetMemory(in Ptr memoryHandle) {
            return memoryHandle.Allocator ?
                m_avlAllocator.GetMemory(memoryHandle) :
                m_buddyAllocator.GetMemory(memoryHandle);
        }
        #endregion

        /// <summary>
        /// MemoryHandle / Pointer
        /// </summary>
        public readonly struct Ptr : IEquatable<Ptr> {
            internal readonly int ChunkIdAndAllocator;
            public readonly int Address;

            internal bool Allocator => unchecked((this.ChunkIdAndAllocator & (int)0x80000000) != 0);
            internal int ChunkID => this.ChunkIdAndAllocator & 0x7FFFFFFF;

            #region constructors
            internal Ptr(bool allocator, int chunkID, int address) : this() {
                this.ChunkIdAndAllocator = unchecked((chunkID & 0x7FFFFFFF) | (allocator ? 0 : (int)0x80000000));
                this.Address             = address;
            }
            private Ptr(int chunkIdAndAllocator, int address) : this() {
                this.ChunkIdAndAllocator = chunkIdAndAllocator;
                this.Address             = address;
            }
            public Ptr(in BuddyMemoryAllocator.Ptr value) : this(false, value.ChunkIdAndLevel & 0x7FFFFFFF, value.Address) {
            }
            public Ptr(in AvlTreeMemoryAllocator.Ptr value) : this(true, value.ChunkID & 0x7FFFFFFF, value.Address) {
            }
            #endregion

            #region static Read()
            public static Ptr Read(byte[] buffer, int index) {
                var chunkIdAndAllocator = 
                    (buffer[index + 0] << 0) |
                    (buffer[index + 1] << 8) |
                    (buffer[index + 2] << 16) |
                    (buffer[index + 3] << 24);
                var address = 
                    (buffer[index + 4] << 0) |
                    (buffer[index + 5] << 8) |
                    (buffer[index + 6] << 16) |
                    (buffer[index + 7] << 24);
                return new Ptr(chunkIdAndAllocator, address);
            }
            #endregion
            #region Write()
            public void Write(byte[] buffer, int index) {
                buffer[index + 0] = unchecked((byte)((this.ChunkIdAndAllocator >> 0) & 0xFF));
                buffer[index + 1] = unchecked((byte)((this.ChunkIdAndAllocator >> 8) & 0xFF));
                buffer[index + 2] = unchecked((byte)((this.ChunkIdAndAllocator >> 16) & 0xFF));
                buffer[index + 3] = unchecked((byte)((this.ChunkIdAndAllocator >> 24) & 0xFF));
                buffer[index + 4] = unchecked((byte)((this.Address >> 0) & 0xFF));
                buffer[index + 5] = unchecked((byte)((this.Address >> 8) & 0xFF));
                buffer[index + 6] = unchecked((byte)((this.Address >> 16) & 0xFF));
                buffer[index + 7] = unchecked((byte)((this.Address >> 24) & 0xFF));
            }
            #endregion

            #region implicit casts
            public static implicit operator BuddyMemoryAllocator.Ptr(in Ptr value) {
                if(!value.Allocator)
                    return new BuddyMemoryAllocator.Ptr(value.ChunkID, value.Address);
                else
                    throw new InvalidCastException($"{nameof(value)} is not a {nameof(BuddyMemoryAllocator)}.{nameof(BuddyMemoryAllocator.Ptr)}.");
            }
            public static implicit operator AvlTreeMemoryAllocator.Ptr(in Ptr value) {
                if(value.Allocator)
                    return new AvlTreeMemoryAllocator.Ptr(value.ChunkID, value.Address);
                else
                    throw new InvalidCastException($"{nameof(value)} is not a {nameof(AvlTreeMemoryAllocator)}.{nameof(AvlTreeMemoryAllocator.Ptr)}.");
            }
            public static implicit operator Ptr(in BuddyMemoryAllocator.Ptr value) {
                return new Ptr(value);
            }
            public static implicit operator Ptr(in AvlTreeMemoryAllocator.Ptr value) {
                return new Ptr(value);
            }
            #endregion

            #region Equals()
            public bool Equals(Ptr other) {
                return this.Address == other.Address && this.ChunkIdAndAllocator == other.ChunkIdAndAllocator;
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
                return (this.Address, this.ChunkIdAndAllocator).GetHashCode();
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return string.Format("[{0} chunk:{1} @{2}]",
                    this.Allocator ? "Buddy" : "AvlTree",
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
            public PtrExtended(in BuddyMemoryAllocator.PtrExtended value) : this(value.Ptr, value.Memory) {
            }
            public PtrExtended(in AvlTreeMemoryAllocator.PtrExtended value) : this(value.Ptr, value.Memory) {
            }
            #endregion

            #region implicit casts
            public static implicit operator BuddyMemoryAllocator.PtrExtended(in PtrExtended value) {
                if(!value.Ptr.Allocator)
                    return new BuddyMemoryAllocator.PtrExtended(value.Ptr, value.Memory);
                else
                    throw new InvalidCastException($"{nameof(value)} is not a {nameof(BuddyMemoryAllocator)}.{nameof(BuddyMemoryAllocator.PtrExtended)}.");
            }
            public static implicit operator AvlTreeMemoryAllocator.PtrExtended(in PtrExtended value) {
                if(value.Ptr.Allocator)
                    return new AvlTreeMemoryAllocator.PtrExtended(value.Ptr, value.Memory);
                else
                    throw new InvalidCastException($"{nameof(value)} is not a {nameof(AvlTreeMemoryAllocator)}.{nameof(AvlTreeMemoryAllocator.PtrExtended)}.");
            }
            public static implicit operator PtrExtended(in BuddyMemoryAllocator.PtrExtended value) {
                return new PtrExtended(value);
            }
            public static implicit operator PtrExtended(in AvlTreeMemoryAllocator.PtrExtended value) {
                return new PtrExtended(value);
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