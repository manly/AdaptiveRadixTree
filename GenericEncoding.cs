#define USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE // if you dont want any external dependencies, comment this. this is only used to avoid needless casts

using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace System.Collections.Specialized 
{
    /// <summary>
    /// Fast and efficient encoding of base types.
    /// </summary>
    public static class GenericEncoding {
        #region static GetDefaultEncoder()
#if USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE
        public static Action<Buffer, T> GetDefaultEncoder<T>() {
            if(typeof(T) == typeof(string))   return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, string>(EncodeString));
            if(typeof(T) == typeof(int))      return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, int>(EncodeInt32));
            if(typeof(T) == typeof(long))     return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, long>(EncodeInt64));
            if(typeof(T) == typeof(double))   return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, double>(BitConverter.IsLittleEndian ? EncodeDoubleLE : (Action<Buffer, double>)EncodeDoubleBE));
            if(typeof(T) == typeof(float))    return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, float>(BitConverter.IsLittleEndian ? EncodeFloatLE : (Action<Buffer, float>)EncodeFloatBE));
            if(typeof(T) == typeof(DateTime)) return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, DateTime>(EncodeDateTime));
            if(typeof(T) == typeof(TimeSpan)) return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, TimeSpan>(EncodeTimeSpan));
            if(typeof(T) == typeof(byte[]))   return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, byte[]>(EncodeByteArray));
            if(typeof(T) == typeof(uint))     return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, uint>(EncodeUInt32));
            if(typeof(T) == typeof(ulong))    return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, ulong>(EncodeUInt64));
            if(typeof(T) == typeof(char))     return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, char>(EncodeChar));
            if(typeof(T) == typeof(sbyte))    return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, sbyte>(EncodeInt8));
            if(typeof(T) == typeof(short))    return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, short>(EncodeInt16));
            if(typeof(T) == typeof(byte))     return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, byte>(EncodeUInt8));
            if(typeof(T) == typeof(ushort))   return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, ushort>(EncodeUInt16));
            if(typeof(T) == typeof(bool))     return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, bool>(EncodeBool));
            if(typeof(T) == typeof(decimal))  return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, decimal>(EncodeDecimal));
            if(typeof(T) == typeof(Guid))     return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, Guid>(EncodeGUID));
                
            return null;
        }
#else
        public static Action<Buffer, object> GetDefaultEncoder<T>() {
            return GetDefaultEncoder(typeof(T));
        }
#endif
        private static Dictionary<Type, Action<Buffer, object>> m_genericEncoders;
        public static Action<Buffer, object> GetDefaultEncoder(Type type) {
            if(m_genericEncoders == null) {
                m_genericEncoders = new Dictionary<Type, Action<Buffer, object>>(){
                    { typeof(string),   EncodeString },
                    { typeof(int),      EncodeInt32 },
                    { typeof(long),     EncodeInt64 },
                    { typeof(double),   BitConverter.IsLittleEndian ? EncodeDoubleLE : (Action<Buffer, object>)EncodeDoubleBE },
                    { typeof(float),    BitConverter.IsLittleEndian ? EncodeFloatLE : (Action<Buffer, object>)EncodeFloatBE },
                    { typeof(DateTime), EncodeDateTime },
                    { typeof(TimeSpan), EncodeTimeSpan },
                    { typeof(byte[]),   EncodeByteArray },
                    { typeof(uint),     EncodeUInt32 },
                    { typeof(ulong),    EncodeUInt64 },
                    { typeof(char),     EncodeChar },
                    { typeof(sbyte),    EncodeInt8 },
                    { typeof(short),    EncodeInt16 },
                    { typeof(byte),     EncodeUInt8 },
                    { typeof(ushort),   EncodeUInt16 },
                    { typeof(bool),     EncodeBool },
                    { typeof(decimal),  EncodeDecimal },
                    { typeof(Guid),     EncodeGUID },
                };
            }
            m_genericEncoders.TryGetValue(type, out var res);
            return res;
        }
        [StructLayout(LayoutKind.Explicit)]
        private struct UnionFloat {
            [FieldOffset(0)] public float Value; // only works with BitConverter.IsLittleEndian
            [FieldOffset(0)] public uint Binary;
        }
        #endregion
        #region static GetDefaultDecoder()
#if USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE
        public static Func<byte[], int, int, T> GetDefaultDecoder<T>() {
            if(typeof(T) == typeof(string))   return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, string>(DecodeString));
            if(typeof(T) == typeof(int))      return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, int>(DecodeInt32));
            if(typeof(T) == typeof(long))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, long>(DecodeInt64));
            if(typeof(T) == typeof(double))   return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, double>(BitConverter.IsLittleEndian ? DecodeDoubleLE : (Func<byte[], int, int, double>)DecodeDoubleBE));
            if(typeof(T) == typeof(float))    return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, float>(BitConverter.IsLittleEndian ? DecodeFloatLE : (Func<byte[], int, int, float>)DecodeFloatBE));
            if(typeof(T) == typeof(DateTime)) return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, DateTime>(DecodeDateTime));
            if(typeof(T) == typeof(TimeSpan)) return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, TimeSpan>(DecodeTimeSpan));
            if(typeof(T) == typeof(byte[]))   return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, byte[]>(DecodeByteArray));
            if(typeof(T) == typeof(uint))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, uint>(DecodeUInt32));
            if(typeof(T) == typeof(ulong))    return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, ulong>(DecodeUInt64));
            if(typeof(T) == typeof(char))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, char>(DecodeChar));
            if(typeof(T) == typeof(sbyte))    return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, sbyte>(DecodeInt8));
            if(typeof(T) == typeof(short))    return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, short>(DecodeInt16));
            if(typeof(T) == typeof(byte))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, byte>(DecodeUInt8));
            if(typeof(T) == typeof(ushort))   return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, ushort>(DecodeUInt16));
            if(typeof(T) == typeof(bool))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, bool>(DecodeBool));
            if(typeof(T) == typeof(decimal))  return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, decimal>(DecodeDecimal));
            if(typeof(T) == typeof(Guid))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, Guid>(DecodeGUID));
            
            return null;
        }
#else
        public static Func<byte[], int, int, object> GetDefaultDecoder<T>() {
            return GetDefaultDecoder(typeof(T));
        }
#endif
        private static Dictionary<Type, Func<byte[], int, int, object>> m_genericDecoders;
        public static Func<byte[], int, int, object> GetDefaultDecoder(Type type) {
            if(m_genericDecoders == null) {
                m_genericDecoders = new Dictionary<Type, Func<byte[], int, int, object>>(){
                    { typeof(string),   DecodeStringGeneric },
                    { typeof(int),      DecodeInt32Generic },
                    { typeof(long),     DecodeInt64Generic },
                    { typeof(double),   BitConverter.IsLittleEndian ? DecodeDoubleLEGeneric : (Func<byte[], int, int, object>)DecodeDoubleBEGeneric },
                    { typeof(float),    BitConverter.IsLittleEndian ? DecodeFloatLEGeneric : (Func<byte[], int, int, object>)DecodeFloatBEGeneric },
                    { typeof(DateTime), DecodeDateTimeGeneric },
                    { typeof(TimeSpan), DecodeTimeSpanGeneric },
                    { typeof(byte[]),   DecodeByteArrayGeneric },
                    { typeof(uint),     DecodeUInt32Generic },
                    { typeof(ulong),    DecodeUInt64Generic },
                    { typeof(char),     DecodeCharGeneric },
                    { typeof(sbyte),    DecodeInt8Generic },
                    { typeof(short),    DecodeInt16Generic },
                    { typeof(byte),     DecodeUInt8Generic },
                    { typeof(ushort),   DecodeUInt16Generic },
                    { typeof(bool),     DecodeBoolGeneric },
                    { typeof(decimal),  DecodeDecimalGeneric },
                    { typeof(Guid),     DecodeGUIDGeneric },
                };
            }
            m_genericDecoders.TryGetValue(type, out var res);
            return res;
        }
        #endregion
        #region static GetDefaultEncodedLength()
        /// <summary>
        ///     Returns the encoded length for the given type.
        ///     All variable-sized values are returned as 0.
        /// </summary>
        public static int GetDefaultEncodedLength(Type type) {
            if(type == typeof(string))   return 0;
            if(type == typeof(int))      return 4;
            if(type == typeof(long))     return 8;
            if(type == typeof(double))   return 8;
            if(type == typeof(float))    return 4;
            if(type == typeof(DateTime)) return 8;
            if(type == typeof(TimeSpan)) return 8;
            if(type == typeof(byte[]))   return 0;
            if(type == typeof(uint))     return 4;
            if(type == typeof(ulong))    return 8;
            if(type == typeof(char))     return 0; // because of utf-8
            if(type == typeof(sbyte))    return 1;
            if(type == typeof(short))    return 2;
            if(type == typeof(byte))     return 1;
            if(type == typeof(ushort))   return 2;
            if(type == typeof(bool))     return 1;
            if(type == typeof(decimal))  return 16;
            if(type == typeof(Guid))     return 16;

            return 0;
        }
        #endregion

        #region private static EncodeString()
        private static void EncodeString(Buffer res, string key) {
            var count  = Encoding.UTF8.GetMaxByteCount(key.Length); //Encoding.UTF8.GetByteCount(key);
            res.EnsureCapacity(count);
            res.Length = Encoding.UTF8.GetBytes(key, 0, key.Length, res.Content, 0);
            // could use Encoding.UTF8.GetEncoder().Convert() to avoid GetByteCount()
        }
        private static void EncodeString(Buffer res, object item) {
            EncodeString(res, (string)item);
        }
        #endregion
        #region private static EncodeChar()
        private static void EncodeChar(Buffer res, char key) {
            if(key <= 0x7F) {
                res.Content[0] = (byte)key;
                res.Length = 1;
            } else {
                var item   = new char[1] { key };
                res.Length = Encoding.UTF8.GetBytes(item, 0, 1, res.Content, 0);
            }
        }
        private static void EncodeChar(Buffer res, object item) {
            EncodeChar(res, (char)item);
        }
        #endregion
        #region private static EncodeInt8()
        private static void EncodeInt8(Buffer res, sbyte key) {
            res.Length     = 1;
            res.Content[0] = unchecked((byte)key);
        }
        private static void EncodeInt8(Buffer res, object item) {
            EncodeInt8(res, (sbyte)item);
        }
        #endregion
        #region private static EncodeInt16()
        private static void EncodeInt16(Buffer res, short key) {
            res.Length     = 2;
            res.Content[0] = unchecked((byte)((key >> 0) & 0xFF));
            res.Content[1] = unchecked((byte)((key >> 8) & 0xFF));
        }
        private static void EncodeInt16(Buffer res, object item) {
            EncodeInt16(res, (short)item);
        }
        #endregion
        #region private static EncodeInt32()
        private static void EncodeInt32(Buffer res, int key) {
            res.Length = 4;
            var buffer = res.Content;
            buffer[0]  = unchecked((byte)((key >> 0) & 0xFF));
            buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
            buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
            buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
        }
        private static void EncodeInt32(Buffer res, object item) {
            EncodeInt32(res, (int)item);
        }
        #endregion
        #region private static EncodeInt64()
        private static void EncodeInt64(Buffer res, long key) {
            res.Length = 8;
            var buffer = res.Content;
            buffer[0]  = unchecked((byte)((key >> 0) & 0xFF));
            buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
            buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
            buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
            buffer[4]  = unchecked((byte)((key >> 32) & 0xFF));
            buffer[5]  = unchecked((byte)((key >> 40) & 0xFF));
            buffer[6]  = unchecked((byte)((key >> 48) & 0xFF));
            buffer[7]  = unchecked((byte)((key >> 56) & 0xFF));
        }
        private static void EncodeInt64(Buffer res, object item) {
            EncodeInt64(res, (long)item);
        }
        #endregion
        #region private static EncodeUInt8()
        private static void EncodeUInt8(Buffer res, byte key) {
            res.Length     = 1;
            res.Content[0] = key;
        }
        private static void EncodeUInt8(Buffer res, object item) {
            EncodeUInt8(res, (byte)item);
        }
        #endregion
        #region private static EncodeUInt16()
        private static void EncodeUInt16(Buffer res, ushort key) {
            res.Length     = 2;
            res.Content[0] = unchecked((byte)((key >> 0) & 0xFF));
            res.Content[1] = unchecked((byte)((key >> 8) & 0xFF));
        }
        private static void EncodeUInt16(Buffer res, object item) {
            EncodeUInt16(res, (ushort)item);
        }
        #endregion
        #region private static EncodeUInt32()
        private static void EncodeUInt32(Buffer res, uint key) {
            res.Length = 4;
            var buffer = res.Content;
            buffer[0]  = unchecked((byte)((key >> 0) & 0xFF));
            buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
            buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
            buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
        }
        private static void EncodeUInt32(Buffer res, object item) {
            EncodeUInt32(res, (uint)item);
        }
        #endregion
        #region private static EncodeUInt64()
        private static void EncodeUInt64(Buffer res, ulong key) {
            res.Length = 8;
            var buffer = res.Content;
            buffer[0]  = unchecked((byte)((key >> 0) & 0xFF));
            buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
            buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
            buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
            buffer[4]  = unchecked((byte)((key >> 32) & 0xFF));
            buffer[5]  = unchecked((byte)((key >> 40) & 0xFF));
            buffer[6]  = unchecked((byte)((key >> 48) & 0xFF));
            buffer[7]  = unchecked((byte)((key >> 56) & 0xFF));
        }
        private static void EncodeUInt64(Buffer res, object item) {
            EncodeUInt64(res, (ulong)item);
        }
        #endregion
        #region private static EncodeBool()
        private static void EncodeBool(Buffer res, bool key) {
            res.Length     = 1;
            res.Content[0] = key ? (byte)1 : (byte)0;
        }
        private static void EncodeBool(Buffer res, object item) {
            EncodeBool(res, (bool)item);
        }
        #endregion
        #region private static EncodeFloatLE()
        private static void EncodeFloatLE(Buffer res, float key) {
            res.Length     = 4;
            var buffer     = res.Content;
            var value_uint = new UnionFloat() { Value = key }.Binary;
            buffer[0] = unchecked((byte)((value_uint >> 0) & 0xFF));
            buffer[1] = unchecked((byte)((value_uint >> 8) & 0xFF));
            buffer[2] = unchecked((byte)((value_uint >> 16) & 0xFF));
            buffer[3] = unchecked((byte)((value_uint >> 24) & 0xFF));
        }
        private static void EncodeFloatLE(Buffer res, object item) {
            EncodeFloatLE(res, (float)item);
        }
        #endregion
        #region private static EncodeFloatBE()
        private static void EncodeFloatBE(Buffer res, float key) {
            res.Length     = 4;
            var buffer     = res.Content;
            var value_uint = new UnionFloat() { Value = key }.Binary;
            buffer[0] = unchecked((byte)((value_uint >> 24) & 0xFF));
            buffer[1] = unchecked((byte)((value_uint >> 16) & 0xFF));
            buffer[2] = unchecked((byte)((value_uint >> 8) & 0xFF));
            buffer[3] = unchecked((byte)((value_uint >> 0) & 0xFF));
        }
        private static void EncodeFloatBE(Buffer res, object item) {
            EncodeFloatBE(res, (float)item);
        }
        #endregion
        #region private static EncodeDoubleLE()
        private static void EncodeDoubleLE(Buffer res, double key) {
            var item   = unchecked((ulong)BitConverter.DoubleToInt64Bits(key));
            res.Length = 8;
            var buffer = res.Content;
            buffer[0]  = unchecked((byte)((item >> 0) & 0xFF));
            buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
            buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
            buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
            buffer[4]  = unchecked((byte)((item >> 32) & 0xFF));
            buffer[5]  = unchecked((byte)((item >> 40) & 0xFF));
            buffer[6]  = unchecked((byte)((item >> 48) & 0xFF));
            buffer[7]  = unchecked((byte)((item >> 56) & 0xFF));
        }
        private static void EncodeDoubleLE(Buffer res, object item) {
            EncodeDoubleLE(res, (double)item);
        }
        #endregion
        #region private static EncodeDoubleBE()
        private static void EncodeDoubleBE(Buffer res, double key) {
            var item   = unchecked((ulong)BitConverter.DoubleToInt64Bits(key));
            res.Length = 8;
            var buffer = res.Content;
            buffer[0]  = unchecked((byte)((item >> 56) & 0xFF));
            buffer[1]  = unchecked((byte)((item >> 48) & 0xFF));
            buffer[2]  = unchecked((byte)((item >> 40) & 0xFF));
            buffer[3]  = unchecked((byte)((item >> 32) & 0xFF));
            buffer[4]  = unchecked((byte)((item >> 24) & 0xFF));
            buffer[5]  = unchecked((byte)((item >> 16) & 0xFF));
            buffer[6]  = unchecked((byte)((item >> 8) & 0xFF));
            buffer[7]  = unchecked((byte)((item >> 0) & 0xFF));
        }
        private static void EncodeDoubleBE(Buffer res, object item) {
            EncodeDoubleBE(res, (double)item);
        }
        #endregion
        #region private static EncodeDecimal()
        private static void EncodeDecimal(Buffer res, decimal key) {
            res.Length = 16;
            var buffer = res.Content;
            var bits   = decimal.GetBits(key);
    
            // technically could be compressed since theres some unused ranges
            // int[3] bits [30-24] and [0-15] are always zero
    
            int bit    = bits[0];
            buffer[0]  = unchecked((byte)((bit >> 0) & 0xFF));
            buffer[1]  = unchecked((byte)((bit >> 8) & 0xFF));
            buffer[2]  = unchecked((byte)((bit >> 16) & 0xFF));
            buffer[3]  = unchecked((byte)((bit >> 24) & 0xFF));
            bit        = bits[1];
            buffer[4]  = unchecked((byte)((bit >> 0) & 0xFF));
            buffer[5]  = unchecked((byte)((bit >> 8) & 0xFF));
            buffer[6]  = unchecked((byte)((bit >> 16) & 0xFF));
            buffer[7]  = unchecked((byte)((bit >> 24) & 0xFF));
            bit        = bits[2];
            buffer[8]  = unchecked((byte)((bit >> 0) & 0xFF));
            buffer[9]  = unchecked((byte)((bit >> 8) & 0xFF));
            buffer[10] = unchecked((byte)((bit >> 16) & 0xFF));
            buffer[11] = unchecked((byte)((bit >> 24) & 0xFF));
            bit        = bits[3];
            buffer[12] = unchecked((byte)((bit >> 0) & 0xFF));
            buffer[13] = unchecked((byte)((bit >> 8) & 0xFF));
            buffer[14] = unchecked((byte)((bit >> 16) & 0xFF));
            buffer[15] = unchecked((byte)((bit >> 24) & 0xFF));
        }
        private static void EncodeDecimal(Buffer res, object item) {
            EncodeDecimal(res, (decimal)item);
        }
        #endregion
        #region private static EncodeDateTime()
        private static void EncodeDateTime(Buffer res, DateTime key) {
            EncodeInt64(res, key.Ticks);
        }
        private static void EncodeDateTime(Buffer res, object item) {
            EncodeDateTime(res, (DateTime)item);
        }
        #endregion
        #region private static EncodeTimeSpan()
        private static void EncodeTimeSpan(Buffer res, TimeSpan key) {
            EncodeInt64(res, key.Ticks);
        }
        private static void EncodeTimeSpan(Buffer res, object item) {
            EncodeTimeSpan(res, (TimeSpan)item);
        }
        #endregion
        #region private static EncodeGUID()
        private static void EncodeGUID(Buffer res, Guid key) {
            res.Length = 16;
            //res.Content = key.ToByteArray();
            // manually copy because the data is too short
            var source = key.ToByteArray();
            var dest   = res.Content;
            for(int i = 0; i < 16; i++)
                dest[i] = source[i];
        }
        private static void EncodeGUID(Buffer res, object item) {
            EncodeGUID(res, (Guid)item);
        }
        #endregion
        #region private static EncodeByteArray()
        private static void EncodeByteArray(Buffer res, byte[] key) {
            res.Length = key.Length;
            // manually copy because the data is too short
            var dest = res.Content;
            var max  = key.Length;
            for(int i = 0; i < max; i++)
                dest[i] = key[i];
        }
        private static void EncodeByteArray(Buffer res, object item) {
            EncodeByteArray(res, (byte[])item);
        }
        #endregion

        #region private static DecodeString()
        private static string DecodeString(byte[] buffer, int start, int len) {
            return Encoding.UTF8.GetString(buffer, start, len);
        }
        private static object DecodeStringGeneric(byte[] buffer, int start, int len) {
            return DecodeString(buffer, start, len);
        }
        #endregion
        #region private static DecodeChar()
        private static char DecodeChar(byte[] buffer, int start, int len) {
            if(len == 1)
                return (char)buffer[start];
            else {
                var temp = new char[1];
                Encoding.UTF8.GetChars(buffer, start, len, temp, 0);
                return temp[0];
            }
        }
        private static object DecodeCharGeneric(byte[] buffer, int start, int len) {
            return DecodeChar(buffer, start, len);
        }
        #endregion
        #region private static DecodeInt8()
        private static sbyte DecodeInt8(byte[] buffer, int start, int len) {
            return unchecked((sbyte)buffer[start]);
        }
        private static object DecodeInt8Generic(byte[] buffer, int start, int len) {
            return DecodeInt8(buffer, start, len);
        }
        #endregion
        #region private static DecodeInt16()
        private static short DecodeInt16(byte[] buffer, int start, int len) {
            return unchecked((short)(
                (buffer[start + 0] << 0) |
                (buffer[start + 1] << 8)));
        }
        private static object DecodeInt16Generic(byte[] buffer, int start, int len) {
            return DecodeInt16(buffer, start, len);
        }
        #endregion
        #region private static DecodeInt32()
        private static int DecodeInt32(byte[] buffer, int start, int len) {
            return unchecked(
                (buffer[start + 0] << 0) |
                (buffer[start + 1] << 8) |
                (buffer[start + 2] << 16) |
                (buffer[start + 3] << 24));
        }
        private static object DecodeInt32Generic(byte[] buffer, int start, int len) {
            return DecodeInt32(buffer, start, len);
        }
        #endregion
        #region private static DecodeInt64()
        private static long DecodeInt64(byte[] buffer, int start, int len) {
            return unchecked(
                ((long)buffer[start + 0] << 0) |
                ((long)buffer[start + 1] << 8) |
                ((long)buffer[start + 2] << 16) |
                ((long)buffer[start + 3] << 24) |
                ((long)buffer[start + 4] << 32) |
                ((long)buffer[start + 5] << 40) |
                ((long)buffer[start + 6] << 48) |
                ((long)buffer[start + 7] << 56));
        }
        private static object DecodeInt64Generic(byte[] buffer, int start, int len) {
            return DecodeInt64(buffer, start, len);
        }
        #endregion
        #region private static DecodeUInt8()
        private static byte DecodeUInt8(byte[] buffer, int start, int len) {
            return buffer[start];
        }
        private static object DecodeUInt8Generic(byte[] buffer, int start, int len) {
            return DecodeUInt8(buffer, start, len);
        }
        #endregion
        #region private static DecodeUInt16()
        private static ushort DecodeUInt16(byte[] buffer, int start, int len) {
            return unchecked((ushort)(
                (buffer[start + 0] << 0) |
                (buffer[start + 1] << 8)));
        }
        private static object DecodeUInt16Generic(byte[] buffer, int start, int len) {
            return DecodeUInt16(buffer, start, len);
        }
        #endregion
        #region private static DecodeUInt32()
        private static uint DecodeUInt32(byte[] buffer, int start, int len) {
            return unchecked(
                ((uint)buffer[start + 0] << 0) |
                ((uint)buffer[start + 1] << 8) |
                ((uint)buffer[start + 2] << 16) |
                ((uint)buffer[start + 3] << 24));
        }
        private static object DecodeUInt32Generic(byte[] buffer, int start, int len) {
            return DecodeUInt32(buffer, start, len);
        }
        #endregion
        #region private static DecodeUInt64()
        private static ulong DecodeUInt64(byte[] buffer, int start, int len) {
            return unchecked(
                ((ulong)buffer[start + 0] << 0) |
                ((ulong)buffer[start + 1] << 8) |
                ((ulong)buffer[start + 2] << 16) |
                ((ulong)buffer[start + 3] << 24) |
                ((ulong)buffer[start + 4] << 32) |
                ((ulong)buffer[start + 5] << 40) |
                ((ulong)buffer[start + 6] << 48) |
                ((ulong)buffer[start + 7] << 56));
        }
        private static object DecodeUInt64Generic(byte[] buffer, int start, int len) {
            return DecodeUInt64(buffer, start, len);
        }
        #endregion
        #region private static DecodeBool()
        private static bool DecodeBool(byte[] buffer, int start, int len) {
            var b = buffer[start];
            return b != 0;
        }
        private static object DecodeBoolGeneric(byte[] buffer, int start, int len) {
            return DecodeBool(buffer, start, len);
        }
        #endregion
        #region private static DecodeFloatLE()
        private static float DecodeFloatLE(byte[] buffer, int start, int len) {
            var value_uint = unchecked(
                ((uint)buffer[start + 0] << 0) |
                ((uint)buffer[start + 1] << 8) |
                ((uint)buffer[start + 2] << 16) |
                ((uint)buffer[start + 3] << 24));
    
            return new UnionFloat() { Binary = value_uint }.Value;
        }
        private static object DecodeFloatLEGeneric(byte[] buffer, int start, int len) {
            return DecodeFloatLE(buffer, start, len);
        }
        #endregion
        #region private static DecodeFloatBE()
        private static float DecodeFloatBE(byte[] buffer, int start, int len) {
            var value_uint = unchecked(
                ((uint)buffer[start + 0] << 24) |
                ((uint)buffer[start + 1] << 16) |
                ((uint)buffer[start + 2] << 8) |
                ((uint)buffer[start + 3] << 0));
    
            return new UnionFloat() { Binary = value_uint }.Value;
        }
        private static object DecodeFloatBEGeneric(byte[] buffer, int start, int len) {
            return DecodeFloatBE(buffer, start, len);
        }
        #endregion
        #region private static DecodeDoubleLE()
        private static double DecodeDoubleLE(byte[] buffer, int start, int len) {
            return BitConverter.Int64BitsToDouble(unchecked(
                ((long)buffer[start + 0] << 0) |
                ((long)buffer[start + 1] << 8) |
                ((long)buffer[start + 2] << 16) |
                ((long)buffer[start + 3] << 24) |
                ((long)buffer[start + 4] << 32) |
                ((long)buffer[start + 5] << 40) |
                ((long)buffer[start + 6] << 48) |
                ((long)buffer[start + 7] << 56) ));
        }
        private static object DecodeDoubleLEGeneric(byte[] buffer, int start, int len) {
            return DecodeDoubleLE(buffer, start, len);
        }
        #endregion
        #region private static DecodeDoubleBE()
        private static double DecodeDoubleBE(byte[] buffer, int start, int len) {
            return BitConverter.Int64BitsToDouble(unchecked(
                ((long)buffer[start + 0] << 56) |
                ((long)buffer[start + 1] << 48) |
                ((long)buffer[start + 2] << 40) |
                ((long)buffer[start + 3] << 32) |
                ((long)buffer[start + 4] << 24) |
                ((long)buffer[start + 5] << 16) |
                ((long)buffer[start + 6] << 8) |
                ((long)buffer[start + 7] << 0) ));
        }
        private static object DecodeDoubleBEGeneric(byte[] buffer, int start, int len) {
            return DecodeDoubleBE(buffer, start, len);
        }
        #endregion
        #region private static DecodeDecimal()
        private static decimal DecodeDecimal(byte[] buffer, int start, int len) {
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
        private static object DecodeDecimalGeneric(byte[] buffer, int start, int len) {
            return DecodeDecimal(buffer, start, len);
        }
        #endregion
        #region private static DecodeDateTime()
        private static DateTime DecodeDateTime(byte[] buffer, int start, int len) {
            var ticks = DecodeInt64(buffer, start, len);
            return new DateTime(ticks);
        }
        private static object DecodeDateTimeGeneric(byte[] buffer, int start, int len) {
            return DecodeDateTime(buffer, start, len);
        }
        #endregion
        #region private static DecodeTimeSpan()
        private static TimeSpan DecodeTimeSpan(byte[] buffer, int start, int len) {
            var ticks = DecodeInt64(buffer, start, len);
            return new TimeSpan(ticks);
        }
        private static object DecodeTimeSpanGeneric(byte[] buffer, int start, int len) {
            return DecodeTimeSpan(buffer, start, len);
        }
        #endregion
        #region private static DecodeGUID()
        private static Guid DecodeGUID(byte[] buffer, int start, int len) {
            if(buffer.Length == 16 && start == 0)
                return new Guid(buffer);

            return new Guid(
                ((uint)buffer[start + 0] << 0) | ((uint)buffer[start + 1] << 8) | ((uint)buffer[start + 2] << 16) | ((uint)buffer[start + 3] << 24),
                (ushort)((buffer[start + 4] << 0) | (buffer[start + 5] << 8)),
                (ushort)((buffer[start + 6] << 0) | (buffer[start + 7] << 8)),
                buffer[start + 8],
                buffer[start + 9],
                buffer[start + 10],
                buffer[start + 11],
                buffer[start + 12],
                buffer[start + 13],
                buffer[start + 14],
                buffer[start + 15]);
        }
        private static object DecodeGUIDGeneric(byte[] buffer, int start, int len) {
            return DecodeGUID(buffer, start, len);
        }
        #endregion
        #region private static DecodeByteArray()
        private static byte[] DecodeByteArray(byte[] buffer, int start, int len) {
            var res = new byte[len];
            // manually copy because the data is too short
            for(int i = 0; i < len; i++)
                res[i] = buffer[start++];
            return res;
        }
        private static object DecodeByteArrayGeneric(byte[] buffer, int start, int len) {
            return DecodeByteArray(buffer, start, len);
        }
        #endregion

        


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
        }
    }

}