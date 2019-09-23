using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;


namespace TimeSeriesDB.IO
{
    /// <summary>
    ///     MemoryStream that uses a list of byte[] internally.
    ///     Provides faster expanding due to avoided array recopy.
    ///     This class is meant for flexible and efficient consecutive reads/writes.
    ///     If you need to do lots of seeking, consider using FastMemoryStream instead. 
    /// </summary>
    public sealed class DynamicMemoryStream : Stream {
        private const int              BUFFER_SIZE               = 256;           // initial capacity, as well as doubled every new section
        private const int              MAX_BUFFER_SIZE           = 128 * 1048576; // 128 MB (for easier consecutive memory allocation)
        private const IncreaseBehavior DEFAULT_INCREASE_BEHAVIOR = IncreaseBehavior.WriteZeroes;

        private long m_length;
        private long m_position;        // this may be beyond length, see write()

        private byte[] m_current;       // m_sections[m_sectionIndex].Buffer
        private int m_currentIndex;     // m_position within m_current
        private int m_currentRemaining; // m_current.Length - m_currentIndex

        private int m_sectionIndex;     // which section m_current is in
        private readonly List<Section> m_sections;

        #region constructors
        public DynamicMemoryStream(int capacity = BUFFER_SIZE) {
            if(capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            m_position     = 0;
            m_length       = 0;
            m_sectionIndex = 0;
            m_sections     = new List<Section>();

            // sanity checks done here
            this.IncreaseCapacityBy(capacity);
            
            m_currentIndex     = 0;
            m_current          = m_sections[0].Buffer;
            m_currentRemaining = m_sections[0].Length;
        }
        public DynamicMemoryStream(byte[] buffer, int offset, int count) {
            m_position         = 0;
            m_length           = count;
            m_current          = buffer;
            m_currentIndex     = offset;
            m_currentRemaining = count;
            m_sectionIndex     = 0;
            m_sections         = new List<Section>();

            // sanity checks done here
            this.AddCapacity(buffer, offset, count);
        }
        #endregion

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => m_length;

        public override long Position {
            get => m_position;
            set => this.Seek(value, SeekOrigin.Begin);
        }

        public long Capacity { get; private set; }

        #region Read()
        public override int Read(byte[] buffer, int offset, int count) {
            if(buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if(offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if(count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if(offset + count > buffer.Length)
                throw new ArgumentException();

            var startOffset = offset;
            // clip count to remaining
            count = unchecked((int)Math.Min(count, Math.Max(m_length - m_position, 0)));

            while(count > 0) {
                int read = Math.Min(count, m_currentRemaining);

                if(read > 8) {
                    Buffer.BlockCopy(m_current, m_currentIndex, buffer, offset, read);
                    m_currentIndex += read;
                    offset += read;
                } else {
                    int byteCount = read;
                    while(--byteCount >= 0)
                        buffer[offset++] = m_current[m_currentIndex++];
                }

                count -= read;
                m_position += read;
                m_currentRemaining -= read;

                if(m_currentRemaining == 0) { // end of section
                    if(m_sectionIndex < m_sections.Count - 1) {
                        var section = m_sections[++m_sectionIndex];

                        m_current          = section.Buffer;
                        m_currentIndex     = section.Index;
                        m_currentRemaining = section.Length;
                    } else
                        break;
                }
            }

            return offset - startOffset;
        }
        #endregion
        #region ReadByte()
        public override int ReadByte() {
            if(m_position >= m_length)
                return -1;

            if(m_currentRemaining == 0) { // end of section
                if(m_sectionIndex < m_sections.Count - 1) {
                    var section        = m_sections[++m_sectionIndex];

                    m_current          = section.Buffer;
                    m_currentIndex     = section.Index;
                    m_currentRemaining = section.Length;
                } else
                    return -1;
            }

            int res = m_current[m_currentIndex++];

            m_position++;
            m_currentRemaining--;

            if(m_currentRemaining == 0 && m_sectionIndex < m_sections.Count - 1) { // end of section
                var section = m_sections[++m_sectionIndex];

                m_current          = section.Buffer;
                m_currentIndex     = section.Index;
                m_currentRemaining = section.Length;
            }

            return res;
        }
        #endregion
        #region Write()
        public override void Write(byte[] buffer, int offset, int count) {
            if(buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if(offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if(count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if(offset + count > buffer.Length)
                throw new ArgumentException();
            
            // if we were past the buffers, then clear between m_length to m_position
            if(m_position > m_length)
                this.InternalSetLength(m_position + count, count);

            while(count > 0) {
                int write = Math.Min(count, m_currentRemaining);

                if(write > 8) {
                    Buffer.BlockCopy(buffer, offset, m_current, m_currentIndex, write);
                    m_currentIndex += write;
                    offset         += write;
                } else {
                    int byteCount = write;
                    while(--byteCount >= 0)
                        m_current[m_currentIndex++] = buffer[offset++];
                }

                count              -= write;
                m_position         += write;
                m_currentRemaining -= write;
                if(m_length < m_position)
                    m_length = m_position;

                if(m_currentRemaining == 0) {
                    if(m_sectionIndex >= m_sections.Count - 1) {
                        if(count == 0)
                            break;

                        this.IncreaseCapacityBy(count);
                    }

                    var current = m_sections[++m_sectionIndex];

                    m_current          = current.Buffer;
                    m_currentIndex     = current.Index;
                    m_currentRemaining = current.Length;
                }
            }
        }
        #endregion
        #region WriteByte()
        public override void WriteByte(byte value) {
            // if we were past the buffers, then clear between m_length to m_position
            if(m_position > m_length || m_currentRemaining == 0)
                this.InternalSetLength(m_position + 1, 1);
            
            m_current[m_currentIndex++] = value;

            m_position++;
            m_currentRemaining--;

            if(m_length < m_position)
                m_length = m_position;

            if(m_currentRemaining == 0 && m_sectionIndex < m_sections.Count - 1) {
                var current = m_sections[++m_sectionIndex];

                m_current          = current.Buffer;
                m_currentIndex     = current.Index;
                m_currentRemaining = current.Length;
            }
        }
        #endregion

        #region Seek()
        public override long Seek(long offset, SeekOrigin origin) {
            // note: As per MemoryStream, setting the position past the Length is OK, 
            // as allocation is only done upon Write()

            switch(origin) {
                case SeekOrigin.Begin:   break;
                case SeekOrigin.Current: offset = m_position + offset; break;
                case SeekOrigin.End:     offset = m_length + offset;   break;
                default:                 throw new NotImplementedException();
            }

            if(offset == m_position)
                return offset;
            if(offset < 0)
                throw new IOException();

            return this.InternalSeek(offset);
        }
        public long InternalSeek(long offset) {
            // note: As per MemoryStream, setting the position past the Length is OK, 
            // as allocation is only done upon Write()

            //if(offset == m_position) // intentionally not enabled
            //    return offset;

            int startSection = 0;

            // fast init
            if(offset > m_position) {
                // if we are already past the buffers, then no need to search for the current section
                if(m_currentRemaining == 0) {
                    m_position = offset;
                    return offset;
                }
                startSection = m_sectionIndex;
            }

            int count = m_sections.Count;
            Section current = null;

            // todo: binarysearch

            for(int i = startSection; i < count; i++) {
                current = m_sections[i];
                long start = current.CumulativePosition;

                if(start + current.Length >= offset) {
                    int pos = unchecked((int)(offset - start));
                    // if we were clipping to m_length
                    //int pos = Math.Min(unchecked((int)(offset - start)), unchecked((int)Math.Min(m_length - start, 0)));

                    m_sectionIndex     = i;
                    m_position         = offset;
                    m_current          = current.Buffer;
                    m_currentIndex     = current.Index + pos;
                    m_currentRemaining = current.Length - pos;
                    return offset;
                }
            }

            m_sectionIndex     = count - 1;
            m_position         = offset;
            m_current          = current.Buffer;
            m_currentIndex     = current.Index + current.Length;
            m_currentRemaining = 0;
            return offset;
        }
        #endregion
        #region SetLength()
        public override void SetLength(long value) {
            if(value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            
            this.InternalSetLength(value, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalSetLength(long value, int dontClearLastBytes) {
            if(m_length == value)
                return;

            int startSection = 0;
            Section current = null;

            // fast init
            if(value > m_position) {
                // if we are already past the buffers, then we need to find the proper new section for our position
                if(m_currentRemaining == 0) {
                    this.IncreaseCapacityBy(value - m_length);
                    m_length = value;

                    // find the proper section for the position that was already past the buffers
                    current            = m_sections[++m_sectionIndex];
                    m_current          = current.Buffer;
                    m_currentIndex     = current.Index;
                    m_currentRemaining = current.Length;
                    this.InternalSeek(m_position);
                    return;
                }
                startSection = m_sectionIndex;
            }

            int count = m_sections.Count;

            for(int i = startSection; i < count; i++) {
                current = m_sections[i];

                if(current.CumulativePosition + current.Length >= value) {
                    // found the last section needed, so remove sections after it

                    int remove_count = m_sections.Count - (i + 1);
                    if(remove_count > 0)
                        this.DecreaseCapacityTo(current.CumulativePosition + current.Length);

                    // must check == too in case the position fell at the start of the last section, which were removing, 
                    // we want it to now point to the new last section end
                    if(m_position >= value)
                        this.InternalSeek(m_position);

                    m_length = value;
                    return;
                }
            }

            // if the section was not found, that means we need to create new ones

            // but first, if we have an unused part of the last buffer, then we need to clear it
            int remainingBytesOnLastSectionPastThePreviousLength = unchecked((int)(current.CumulativePosition + current.Length - m_length));
            long capacity_to_add = value - m_length - remainingBytesOnLastSectionPastThePreviousLength;
            
            if(DEFAULT_INCREASE_BEHAVIOR == IncreaseBehavior.WriteZeroes && remainingBytesOnLastSectionPastThePreviousLength > 0) {
                // avoid needless clear if were going to perform a write right afterwards
                int bytes_to_clear = capacity_to_add - dontClearLastBytes >= 0 ?
                    remainingBytesOnLastSectionPastThePreviousLength :
                    unchecked((int)(remainingBytesOnLastSectionPastThePreviousLength + (capacity_to_add - dontClearLastBytes)));

                if(bytes_to_clear > 0)
                    Array.Clear(current.Buffer, current.Index + current.Length - remainingBytesOnLastSectionPastThePreviousLength, bytes_to_clear);
            }

            this.IncreaseCapacityBy(capacity_to_add);

            if(m_position > m_length)
                this.InternalSeek(m_position);

            m_length = value;
        }
        #endregion

        #region AddCapacity()
        /// <summary>
        ///     Appends capacity at the end of the list of sections.
        ///     This will not affect the Length.
        /// </summary>
        public void AddCapacity(byte[] buffer, int offset, int count) {
            if(buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if(buffer.LongLength >= int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(buffer));
            if(offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if(count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if(offset + count > buffer.Length)
                throw new ArgumentException();

            long cumulativePosition = 0;

            var sectionCount = m_sections.Count;
            if(sectionCount > 0) {
                var last = m_sections[sectionCount - 1];
                cumulativePosition = last.CumulativePosition + last.Length;
            }

            var section = new Section(buffer, offset, count, cumulativePosition);
            m_sections.Add(section);

            // if were at the end of the stream, then update the 'active' section
            if(m_position >= m_length) {
                //this.InternalSeek(m_position);

                var position_within_section = unchecked((int)Math.Min(m_position - section.CumulativePosition, section.Length));

                m_sectionIndex     = sectionCount; // sectionCount before add()
                m_current          = section.Buffer;
                m_currentIndex     = section.Index + position_within_section;
                m_currentRemaining = section.Length - position_within_section;
            }
        }
        #endregion
        #region private IncreaseCapacityBy()
        /// <summary>
        ///     Generates new sections until bytes are filled.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void IncreaseCapacityBy(long additionalBytes) {
            while(additionalBytes > 0) {
                var buffer = this.GenerateNextBuffer(additionalBytes);

                if(m_sections.Count > 0) {
                    var last = m_sections[m_sections.Count - 1];
                    m_sections.Add(new Section(buffer, 0, buffer.Length, last.CumulativePosition + last.Length));
                } else
                    m_sections.Add(new Section(buffer, 0, buffer.Length, 0));

                additionalBytes -= buffer.Length;
                this.Capacity   += buffer.Length;
            }
        }
        #endregion
        #region private DecreaseCapacityTo()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DecreaseCapacityTo(long capacity) {
            System.Diagnostics.Debug.Assert(this.Capacity >= capacity && capacity > 0);

            while(this.Capacity > capacity) {
                var lastIndex = m_sections.Count - 1;
                var last = m_sections[lastIndex];

                if(last.CumulativePosition + last.Length > capacity) {
                    this.Capacity -= last.Length;
                    m_sections.RemoveAt(lastIndex);
                } else
                    break;
            }
        }
        #endregion

        #region private GenerateNextBuffer()
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte[] GenerateNextBuffer(long size) {
            // this uses 2 different strategies
            // - the generic [capacity * 2]
            // - in case a lot of small buffers were manually provided and not automatically generated (through AddCapacity)
            //   then theres a minimum size. The goal is avoiding fragmentation/too many sections

            const int PAGE_SIZE = 4096;

            //long strategy1 = this.Capacity << 1; 
            long strategy1 = (long)1 << (Log2(this.Capacity) + 1); // rounddown(capacity, exponent of 2) * 2
            long strategy2 = (long)BUFFER_SIZE << m_sections.Count;

            long max       = Math.Max(Math.Max(strategy1, strategy2), size);
            int finalSize  = RoundUp(max, max >= PAGE_SIZE ? PAGE_SIZE : BUFFER_SIZE); // round to BUFFER_SIZE until pagesize

            return new byte[finalSize];

            // roundsup to round, and caps at MAX_BUFFER_SIZE
            int RoundUp(long value, long round) {
                return unchecked((int)(value < MAX_BUFFER_SIZE - round ?
                    ((value / round) + ((value % round) == 0 ? 0 : 1)) * round :
                    MAX_BUFFER_SIZE));
            }
            int Log2(long value) {
                // could be faster with intrinsics
                int res = 0;
                while((value >>= 1) > 0)
                    res++;
                return res;
            }
        }
        #endregion

        #region GetInternalSections()
        /// <summary>
        ///     Returns the internal primitives used to store the data.
        ///     This is meant for speeding up code in some specific scenarios.
        ///     The caller is expected to handle reading only the required amount of data, as this returns the entire capacity of the stream (thus beyond Length).
        /// </summary>
        public IEnumerable<Section> GetInternalSections() {
            return m_sections;
        }
        #endregion

        #region Flush()
        public override void Flush() {
            // intentionally empty
        }
        #endregion
        #region ToString()
        public override string ToString() {
            return string.Format("{0} Pos={1}, Len={2}", this.GetType().Name, this.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), this.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        #endregion
        #region ToArray()
        public byte[] ToArray() {
            if(this.Length >= int.MaxValue)
                throw new NotSupportedException("The Length is too large to support Read() operations on that position.");

            var remaining = this.Length;
            var res = new byte[remaining];
            int index = 0;
            int count = m_sections.Count;
            for(int i = 0; i < count; i++) {
                var current = m_sections[i];
                var request = unchecked((int)Math.Min(current.Length, remaining));
                if(request <= 0)
                    break;
                Buffer.BlockCopy(current.Buffer, current.Index, res, index, request);
                index += request;
                remaining -= request;
            }
            return res;
        }
        #endregion

        public sealed class Section {
            public readonly byte[] Buffer;
            public readonly int Index;
            public readonly int Length;
            public readonly long CumulativePosition; // the position within the stream
            
            #region constructors
            internal Section(byte[] buffer, int index, int length, long cumulativePosition) {
                this.Buffer = buffer;
                this.Index  = index;
                this.Length = length;
                this.CumulativePosition = cumulativePosition;
            }
            #endregion
        }
        public enum IncreaseBehavior {
            WriteZeroes,
            KeepData,
        }
    }
}
