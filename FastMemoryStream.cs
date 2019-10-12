//#define ENABLE_SANITY_CHECKS  // disable for speed
using System.Runtime.CompilerServices;


namespace System.IO
{
    /// <summary>
    ///     A fast MemoryStream that uses a list of fixed byte[] internally.
    ///     Meant for efficient resize and seeks.
    /// </summary>
    public sealed class FastMemoryStream : Stream {
        private const int BUFFER_SIZE = 131072; // > 85k to avoid GC 1, must be power of 2

        private long m_length;
        private long m_position;  // this may be beyond length, see write()

        private byte[] m_current; // m_sections[m_position/BUFFER_SIZE]
        private byte[][] m_sections;

        #region constructors
        static FastMemoryStream() {
            CheckBufferSize(BUFFER_SIZE);
        }
        public FastMemoryStream() {
            m_position    = 0;
            m_length      = 0;
            m_sections    = new byte[4][];
            m_sections[0] = new byte[BUFFER_SIZE];
            m_current     = m_sections[0];
            this.Capacity = BUFFER_SIZE;
        }
        public FastMemoryStream(byte[] buffer, int offset, int count) : this() {
            this.Write(buffer, offset, count);

            m_position = 0;
            m_current  = m_sections[0];
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
#if ENABLE_SANITY_CHECKS
            if(buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if(offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if(count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if(offset + count > buffer.Length)
                throw new ArgumentException();
#endif

            long pos = m_position;
            // clip count to remaining
            count    = unchecked((int)Math.Min(count, Math.Max(m_length - pos, 0)));
            
            if(count == 0)
                return 0;

            int remaining  = count;
            int index      = unchecked((int)(pos % BUFFER_SIZE));
            int section    = unchecked((int)(pos / BUFFER_SIZE));
            var current    = m_current;
            while(true) {
                int read   = Math.Min(remaining, BUFFER_SIZE - index);

                Buffer.BlockCopy(current, index, buffer, offset, read);
                offset    += read;
                remaining -= read;

                if(remaining == 0) {
                    if(index + read == BUFFER_SIZE) {
                        current   = ++section < m_sections.Length ? m_sections[section] : null;
                        m_current = current;
                    }
                    break;
                }

                index     = 0;
                current   = ++section < m_sections.Length ? m_sections[section] : null;
                m_current = current;
            }

            m_position += count;
            return count;
        }
        #endregion
        #region ReadByte()
        public override int ReadByte() {
            if(m_position >= m_length)
                return -1;

            long pos  = m_position;
            int index = unchecked((int)(pos % BUFFER_SIZE));
            var res   = m_current[index];

            if(index + 1 == BUFFER_SIZE) {
                int section = unchecked((int)((pos + 1) / BUFFER_SIZE));
                m_current   = section < m_sections.Length ? m_sections[section] : null;
            }

            m_position = pos + 1;
            return res;
        }
        #endregion
        #region Write()
        public override void Write(byte[] buffer, int offset, int count) {
#if ENABLE_SANITY_CHECKS
            if(buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if(offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if(count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if(offset + count > buffer.Length)
                throw new ArgumentException();
#endif

            var pos = m_position;
            // if we were past the buffers, then clear between m_length to m_position
            if(pos > m_length || m_current == null)
                this.InternalSetLength(pos + count, count);

            int section           = unchecked((int)(pos / BUFFER_SIZE));
            int current_index     = unchecked((int)(pos % BUFFER_SIZE));
            int current_remaining = BUFFER_SIZE - current_index;

            while(count > 0) {
                int write   = Math.Min(count, current_remaining);

                Buffer.BlockCopy(buffer, offset, m_current, current_index, write);
                offset     += write;
                count      -= write;
                pos        += write;
                if(m_length < pos)
                    m_length = pos;

                if(current_remaining - write == 0) {
                    if(pos >= this.Capacity) {
                        if(count > 0) {
                            if(++section == m_sections.Length)
                                Array.Resize(ref m_sections, section * 2);
                            var current         = new byte[BUFFER_SIZE];
                            m_current           = current;
                            m_sections[section] = current;
                            this.Capacity      += BUFFER_SIZE;
                        } else {
                            m_current = null;
                            break;
                        }
                    } else
                        m_current = m_sections[++section];
                }

                current_index     = 0;
                current_remaining = BUFFER_SIZE;
            }

            m_position = pos;
        }
        #endregion
        #region WriteByte()
        public override void WriteByte(byte value) {
            // if we were past the buffers, then clear between m_length to m_position
            var pos = m_position;
            if(pos > m_length || m_current == null)
                this.InternalSetLength(pos + 1, 1);
            
            int current_index = unchecked((int)(pos % BUFFER_SIZE));
            m_current[current_index] = value;

            pos++;
            m_position = pos;

            if(m_length < pos)
                m_length = pos;

            if(current_index + 1 == BUFFER_SIZE) {
                m_current = pos >= this.Capacity ? 
                    null :
                    m_sections[unchecked((int)(pos / BUFFER_SIZE))];
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

            int section = unchecked((int)(offset / BUFFER_SIZE));
            m_current   = section < m_sections.Length ? m_sections[section] : null;
            m_position  = offset;
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
            if(value == m_length)
                return;

            int sections = unchecked((int)(this.Capacity / BUFFER_SIZE));
            int needed   = unchecked((int)(value / BUFFER_SIZE)) + 1;

            if(value > m_length) {
                // clear data between m_length and value
                long bytes_to_clear = value - m_length - dontClearLastBytes;
                if(bytes_to_clear > 0) {
                    int current_section = unchecked((int)(m_length / BUFFER_SIZE));
                    var current_index   = unchecked((int)(m_length % BUFFER_SIZE));
                    int qty             = unchecked((int)Math.Min(bytes_to_clear, BUFFER_SIZE - current_index));
                    Array.Clear(m_sections[current_section], current_index, qty);
                    bytes_to_clear     -= qty;
                    while(bytes_to_clear > 0 && current_section < sections - 1) {
                        Array.Clear(m_sections[++current_section], 0, unchecked((int)Math.Min(BUFFER_SIZE, bytes_to_clear)));
                        bytes_to_clear -= BUFFER_SIZE;
                    }
                }

                // adding capacity
                if(sections < needed) {
                    while(sections < needed) {
                        if(sections == m_sections.Length)
                            Array.Resize(ref m_sections, sections * 2);
                        m_sections[sections] = new byte[BUFFER_SIZE];
                        this.Capacity += BUFFER_SIZE;
                        sections++;
                    }
                    int section_index = unchecked((int)(m_position / BUFFER_SIZE));
                    m_current         = section_index < m_sections.Length ? m_sections[section_index] : null;
                }
            } else if(sections > needed) { 
                // decreasing capacity
                while(sections - 1 > needed) {
                    m_sections[sections] = null;
                    if(sections == m_sections.Length / 2 && sections > 8)
                        Array.Resize(ref m_sections, sections / 2);
                    this.Capacity -= BUFFER_SIZE;
                    sections--;
                }
                int section_index = unchecked((int)(m_position / BUFFER_SIZE));
                m_current         = section_index < m_sections.Length ? m_sections[section_index] : null;
            }

            m_length = value;
        }
        #endregion

        #region Flush()
        public override void Flush() {
            // intentionally empty
        }
        #endregion
        #region ToArray()
        public byte[] ToArray() {
            if(this.Length >= int.MaxValue)
                throw new NotSupportedException("The Length is too large to support Read() operations on that position.");

            var remaining = this.Length;
            var res       = new byte[remaining];
            int index     = 0;
            int count     = m_sections.Length;
            for(int i = 0; i < count; i++) {
                var current = m_sections[i];
                var request = unchecked((int)Math.Min(BUFFER_SIZE, remaining));
                if(request <= 0)
                    break;
                Buffer.BlockCopy(current, 0, res, index, request);
                index     += request;
                remaining -= request;
            }
            return res;
        }
        #endregion
        #region ToString()
        public override string ToString() {
            return string.Format("{0} Pos={1}, Len={2}", this.GetType().Name, this.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), this.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        #endregion

        #region private static CheckBufferSize()
        private static void CheckBufferSize(int buffer_size) {
            int bit_count   = 0;
            for(int i = 0; i < 32; i++) {
                if((buffer_size & 1) == 1)
                    bit_count++;
                buffer_size >>= 1;
            }
            if(bit_count != 1)
                throw new ArgumentOutOfRangeException(nameof(BUFFER_SIZE), "must be a multiple of 2.");
        }
        #endregion
    }
}
