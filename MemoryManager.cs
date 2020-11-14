using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;


namespace System.Collections.Specialized 
{
    /// <summary>
    ///     Manages memory allocation without allocating memory.
    ///     Handles infinite (2^63) memory.
    ///     Dynamically resizes itselfs based on use, both upwards and downwards.
    ///     Has no memory alignment (no roundings, etc.) and allows free()ing fractions of previous allocs.
    /// </summary>
    /// <remarks>
    ///     Potential alternatives: use a native memory manager (https://github.com/allisterb/jemalloc.NET)
    ///     
    ///     design notes
    ///     ============
    ///     This class intentionally uses 2 BinarySearchTree variants, with different goals.
    ///     The first one maps the available memory chunks and is ordered by (len, pos).
    ///     This allows quickly searching the smallest possible chunk when calling alloc() while having fast insert speeds (unlike sortedlist).
    ///     Most importantly though, the vast majority of requests are BinarySearch() without any insert()/remove() because we just resize existing data.
    ///     This is why an AvlTree is preferred to a RedBlackTree.
    ///     The second one is a B+Tree mapping available memory chunks and is ordered by (pos, len). 
    ///     This allows a quick item.previous()/next() in order to determine if we have consecutive memory chunks we need to merge.
    ///     Crucially, the B+Tree also needs to find the closest match to a memory location that we are freeing, since it will never be found in its list of available memory (ie: not freeing already freed memory).
    ///     Also, need quick insert()/remove() operations.
    /// </remarks>
    public sealed class MemoryManager {
        private readonly AvlTree<MemorySegment> m_avail;     // len, pos
        private readonly BTree<long, long> m_availAddresses; // pos, len

        /// <summary>
        ///     The total capacity, including allocated and free memory.
        ///     This value cannot be set as it increases/decreases as more memory is needed/freed.
        ///     The capacity always ends on allocated memory, and never on freed memory.
        /// </summary>
        public long Capacity { get; private set; }
        public long TotalAllocated => this.Capacity - this.TotalFree;
        public long TotalFree { get; private set; }

        #region constructors
        public MemoryManager() {
            m_avail          = new AvlTree<MemorySegment>(CompareMemorySegments);
            m_availAddresses = new BTree<long, long>();
        }
        #endregion

        #region Alloc()
        /// <summary>
        ///     Worst: O(4 log n)
        /// </summary>
        public long Alloc(long length) {
            if(length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            if(m_avail.Count > 0) {
                // search for an available memory segment of at least length bytes, returning the smallest result that is >= length
                var res = m_avail.BinarySearch_GreaterOrEqualTo(new MemorySegment(0, length), CompareMemorySegmentsSizeOnly);
                
                if(res.Diff <= 0) { // if available_memory_segment >= length
                    var x           = res.Node.Key;
                    var bsr         = m_availAddresses.BinarySearch(x.Address);
                    if(bsr.Index < 0)
                        bsr = bsr.BitwiseNot();
                    
                    m_avail.Remove(res.Node);
                    var address     = x.Address;
                    x               = new MemorySegment(address + length, x.Length - length);
                    res.Node.UpdateKey(x);
                    this.TotalFree -= length;

                    if(x.Length > 0) {
                        bsr.Update(x.Address, x.Length);
                        m_avail.Add(x);
                    } else
                        m_availAddresses.Remove(bsr);

                    return address;
                }
            }

            // if theres not enough consecutive memory, then add at the end
            var end       = this.Capacity;
            this.Capacity = end + length;
                
            return end;
        }
        #endregion
        #region Free()
        /// <summary>
        ///     O(log n)
        ///     Throws if trying to free() unallocated memory.
        /// </summary>
        public void Free(long address, long length) {
            if(length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if(address < 0)
                throw new ArgumentOutOfRangeException(nameof(address));

            var res = m_availAddresses.BinarySearch(address);

            if(res.Items != null) {
                if(res.Index < 0) {
                    res      = res.BitwiseNot();
                    var prev = res.Previous();
                    BTree<long, long>.BinarySearchResult next = default;

                    if(res.Index != 0 || prev.Items != null) {
                        if(res.Index < res.NodeCount || (next = res.Next()).Items != null) {
                            if(res.Index == res.NodeCount)
                                res = next;
                            this.Free_TypicalCase(address, length, in prev, in res);
                        } else {
                            if(res.Index == res.Items.Length)
                                res = prev;
                            this.RareFree_AfterLastAvailMemorySegment(address, length, in res);
                        }
                    } else
                        this.RareFree_BeforeFirstAvailMemorySegment(address, length, in res);
                } else
                    throw new ArgumentException($"Attempting to free() already freed memory at {address} ({length} bytes).", nameof(address));
            } else
                this.RareFree_NoFreeMemory(address, length);
        }
        private void Free_TypicalCase(long address, long length, in BTree<long, long>.BinarySearchResult bsr, in BTree<long, long>.BinarySearchResult next) {
            //       bsr                          next
            // [---free mem---][***in use***][---free mem---]
            var diff1 = address - (bsr.Item.Key + bsr.Item.Value);
            var diff2 = (address + length) - next.Item.Key;
            if(diff1 < 0 || diff2 > 0)
                throw new ArgumentException($"Attempting to free() already freed memory at {address} ({length} bytes).", nameof(address));

            var adjacent_prev = diff1 == 0;
            var adjacent_next = diff2 == 0;

            if(!adjacent_prev) {
                if(!adjacent_next) {
                    m_availAddresses.Add(address, length);
                    m_avail.Add(new MemorySegment(address, length));
                } else { // !adjacent_prev && adjacent_next
                    var _new = new MemorySegment(address, next.Item.Value + length);
                    m_avail.Remove(new MemorySegment(next.Item.Key, next.Item.Value));
                    m_avail.Add(_new);
                    next.Update(_new.Address, _new.Length);
                }
            } else if(!adjacent_next) { // adjacent_prev && !adjacent_next
                var _new = new MemorySegment(bsr.Item.Key, bsr.Item.Value + length);
                m_avail.Remove(new MemorySegment(bsr.Item.Key, bsr.Item.Value));
                m_avail.Add(_new);
                bsr.UpdateValue(_new.Length);
            } else { // adjacent_prev && adjacent_next
                var _new = new MemorySegment(bsr.Item.Key, bsr.Item.Value + length + next.Item.Value);
                m_avail.Remove(new MemorySegment(bsr.Item.Key, bsr.Item.Value));
                m_avail.Remove(new MemorySegment(next.Item.Key, next.Item.Value));
                m_avail.Add(_new);
                m_availAddresses.Remove(next);
                bsr.UpdateValue(_new.Length);
            }
            this.TotalFree += length;
        }
        private void RareFree_AfterLastAvailMemorySegment(long address, long length, in BTree<long, long>.BinarySearchResult bsr) {
            //       bsr                     
            // [---free mem---][***in use***]
            var diff1 = address - (bsr.Item.Key + bsr.Item.Value);
            var diff2 = (address + length) - this.Capacity;
            if(diff1 < 0 || diff2 > 0)
                throw new ArgumentException($"Attempting to free() already freed memory at {address} ({length} bytes).", nameof(address));

            var adjacent_prev = diff1 == 0;
            var adjacent_next = diff2 == 0; // this being true means we reduce the Capacity instead

            if(!adjacent_prev) {
                if(!adjacent_next) {
                    m_availAddresses.Add(address, length);
                    m_avail.Add(new MemorySegment(address, length));
                    this.TotalFree += length;
                } else { // !adjacent_prev && adjacent_next
                    this.Capacity -= length;
                    //this.TotalFree -= length;
                }
            } else if(!adjacent_next) { // adjacent_prev && !adjacent_next
                var _new = new MemorySegment(bsr.Item.Key, bsr.Item.Value + length);
                m_avail.Remove(new MemorySegment(bsr.Item.Key, bsr.Item.Value));
                m_avail.Add(_new);
                bsr.UpdateValue(_new.Length);
                this.TotalFree += length;
            } else { // adjacent_prev && adjacent_next
                m_avail.Remove(new MemorySegment(bsr.Item.Key, bsr.Item.Value));
                m_availAddresses.Remove(bsr.Item.Key);
                this.Capacity -= bsr.Item.Value + length;
                //this.TotalFree -= length;
            }
        }
        private void RareFree_BeforeFirstAvailMemorySegment(long address, long length, in BTree<long, long>.BinarySearchResult bsr) {
            //                    bsr
            // [***in use***][---free mem---]
            var firstAvailAddress = bsr.Item.Key;
            if(address + length < firstAvailAddress) {
                m_availAddresses.Add(address, length);
                m_avail.Add(new MemorySegment(address, length));
                this.TotalFree += length;
            } else if(address + length == firstAvailAddress) {
                var _new = new MemorySegment(address, bsr.Item.Value + length);
                m_avail.Remove(new MemorySegment(firstAvailAddress, bsr.Item.Value));
                m_avail.Add(_new);
                bsr.Update(_new.Address, _new.Length);
                this.TotalFree += length;
            } else
                throw new ArgumentOutOfRangeException(nameof(length), $"Trying to free() already freed memory ({address} + {length} > {firstAvailAddress}).");
        }
        private void RareFree_NoFreeMemory(long address, long length) {
            // [***in use***]
            if(address + length < this.Capacity) {
                m_availAddresses.Add(address, length);
                m_avail.Add(new MemorySegment(address, length));
                this.TotalFree += length;
            } else if(address + length == this.Capacity) {
                this.Capacity -= length;
                //this.TotalFree -= length;
            } else
                throw new ArgumentOutOfRangeException(nameof(length), $"Trying to free() past the allocated region ({address} + {length} > {this.Capacity}).");
        }
        #endregion
        #region Clear()
        /// <summary>
        ///     O(1)
        /// </summary>
        /// <param name="free">Default: null. If set, allows a custom free method for every allocated memory segments. free(long address, long length)</param>
        public void Clear(Action<long, long> free = null) {
            if(free != null) {
                var allocatedMemory = this.GetAllocatedMemory().ToList();
                foreach(var (address, length) in allocatedMemory) {
                    free(address, length);
                    //this.Free(address, length);
                }
            }

            m_avail.Clear();
            m_availAddresses.Clear();
            this.TotalFree = 0;
            this.Capacity  = 0;
        }
        #endregion

        #region Load()
        public void Load(IEnumerable<(long address, long length)> allocatedMemory) {
            this.Clear();

            long current              = 0;
            long allocatedMemoryTotal = 0;

            // speed optimisation: use an appender to avoid O(log n) inserts
            var appender = m_availAddresses.GetAppender();
            
            foreach(var (address, length) in allocatedMemory.OrderBy(o => o.address)) { // orderby() must match the btree<> comparer
                allocatedMemoryTotal += length;

                var diff = address - current;
                if(diff > 0) {
                    appender.AddOrdered(current, diff); //m_availAddresses.Add(current, diff);
                    m_avail.Add(new MemorySegment(current, diff));
                }

                current  = address + length;
            }

            this.TotalFree = current - allocatedMemoryTotal;
            this.Capacity  = current;
        }
        #endregion
        #region GetAllocatedMemory()
        /// <summary>
        ///     Returns the list of all allocated segments in positional order.
        /// </summary>
        public IEnumerable<(long address, long length)> GetAllocatedMemory() {
            long current = 0;
            long diff;

            foreach(var item in m_availAddresses.Items) {
                diff = item.Key - current;

                if(diff > 0)
                    yield return (current, diff);

                current = item.Key + item.Value;
            }

            diff = this.Capacity - current;
            if(diff > 0)
                yield return (current, diff);
        }
        #endregion
        #region GetAvailableMemory()
        /// <summary>
        ///     Returns the list of all available memory segments in positional order.
        /// </summary>
        public IEnumerable<(long address, long length)> GetAvailableMemory() {
            foreach(var item in m_availAddresses.Items)
                yield return (item.Key, item.Value);
        }
        #endregion

        #region private static CompareMemorySegments()
        private static int CompareMemorySegments(MemorySegment item1, MemorySegment item2) {
            var cmp = item1.Length.CompareTo(item2.Length);
            if(cmp != 0)
                return cmp;
            return item1.Address.CompareTo(item2.Address);
        }
        #endregion
        #region private static CompareMemorySegmentsSizeOnly()
        private static int CompareMemorySegmentsSizeOnly(MemorySegment item1, MemorySegment item2) {
            return item1.Length.CompareTo(item2.Length);
        }
        #endregion

        
        private readonly struct MemorySegment : IEquatable<MemorySegment> {
            public readonly long Address;
            public readonly long Length;

            #region constructors
            public MemorySegment(long address, long length) {
                this.Address = address;
                this.Length  = length;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return $"[{this.Address} - {(this.Address + this.Length)}] ({this.Length} bytes)";
            }
            #endregion

            #region Equals()
            public bool Equals(MemorySegment other) {
                return this.Address == other.Address && this.Length == other.Length;
            }
            public override bool Equals(object obj) {
                if(obj is MemorySegment memseg)
                    return this.Equals(memseg);
                return false;
            }

            public static bool operator ==(MemorySegment x, MemorySegment y) {
                return x.Equals(y);
            }
            public static bool operator !=(MemorySegment x, MemorySegment y) {
                return !(x == y);
            }
            #endregion
            #region GetHashCode()
            public override int GetHashCode() {
                return (this.Address, this.Length).GetHashCode();
            }
            #endregion
        }

        // todo: benchmark with oldcode
        /*private sealed class MemorySegment {
            public long Address;
            public long Length;

            #region constructors
            public MemorySegment(long address, long length) {
                this.Address = address;
                this.Length  = length;
            }
            #endregion
            #region ToString()
            public override string ToString() {
                return $"[{this.Address} - {(this.Address + this.Length)}] ({this.Length} bytes)";
            }
            #endregion
        }*/
    }
}
