#define USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE // if you dont want any external dependencies, comment this. this is only used to avoid needless casts

using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static System.Runtime.CompilerServices.MethodImplOptions;


namespace System.Collections.Specialized 
{
    /// <summary>
    /// Fast and efficient encoding of base types.
    /// </summary>
    public static class GenericEncoding {
        #region public static GetDefaultEncoder<T>()
#if USE_SYSTEM_RUNTIME_COMPILERSERVICES_UNSAFE
        public static Action<Buffer, T> GetDefaultEncoder<T>() {
            if(typeof(T) == typeof(string))   return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, string>(EncodeString));
            if(typeof(T) == typeof(int))      return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, int>(EncodeInt32));
            if(typeof(T) == typeof(long))     return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, long>(EncodeInt64));
            if(typeof(T) == typeof(double))   return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, double>(EncodeDouble));
            if(typeof(T) == typeof(float))    return Unsafe.As<Action<Buffer, T>>(new Action<Buffer, float>(BitConverter.IsLittleEndian ? EncodeFloatLE : (Action<Buffer, float>)EncodeFloat));
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
    
            void EncodeString(Buffer res, string key) {
                var count  = Encoding.UTF8.GetMaxByteCount(key.Length); //Encoding.UTF8.GetByteCount(key);
                res.EnsureCapacity(count);
                res.Length = Encoding.UTF8.GetBytes(key, 0, key.Length, res.Content, 0);
                // could use Encoding.UTF8.GetEncoder().Convert() to avoid GetByteCount()
            }
            void EncodeChar(Buffer res, char key) {
                if(key <= 0x7F) {
                    res.Content[0] = (byte)key;
                    res.Length = 1;
                } else {
                    var item   = new char[1] { key };
                    res.Length = Encoding.UTF8.GetBytes(item, 0, 1, res.Content, 0);
                }
            }
            void EncodeInt8(Buffer res, sbyte key) {
                res.Length     = 1;
                res.Content[0] = unchecked((byte)key);
            }
            void EncodeInt16(Buffer res, short key) {
                res.Length     = 2;
                res.Content[0] = unchecked((byte)((key >> 0) & 0xFF));
                res.Content[1] = unchecked((byte)((key >> 8) & 0xFF));
            }
            void EncodeInt32(Buffer res, int key) {
                res.Length = 4;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)((key >> 0) & 0xFF));
                buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
            }
            void EncodeInt64(Buffer res, long key) {
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
            void EncodeUInt8(Buffer res, byte key) {
                res.Length     = 1;
                res.Content[0] = key;
            }
            void EncodeUInt16(Buffer res, ushort key) {
                res.Length     = 2;
                res.Content[0] = unchecked((byte)((key >> 0) & 0xFF));
                res.Content[1] = unchecked((byte)((key >> 8) & 0xFF));
            }
            void EncodeUInt32(Buffer res, uint key) {
                res.Length = 4;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)((key >> 0) & 0xFF));
                buffer[1]  = unchecked((byte)((key >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((key >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((key >> 24) & 0xFF));
            }
            void EncodeUInt64(Buffer res, ulong key) {
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
            void EncodeBool(Buffer res, bool key) {
                res.Length = 1;
                res.Content[0] = key ? (byte)1 : (byte)0;
            }
            void EncodeFloatLE(Buffer res, float key) {
                res.Length     = 4;
                var buffer     = res.Content;
                var value_uint = new UnionFloat() { Value = key }.Binary;
                buffer[0] = unchecked((byte)((value_uint >> 0) & 0xFF));
                buffer[1] = unchecked((byte)((value_uint >> 8) & 0xFF));
                buffer[2] = unchecked((byte)((value_uint >> 16) & 0xFF));
                buffer[3] = unchecked((byte)((value_uint >> 24) & 0xFF));
            }
            void EncodeFloat(Buffer res, float key) {
                res.Length = 4;
                var buffer = res.Content;
                var value  = BitConverter.GetBytes(key);
                buffer[0]  = value[0];
                buffer[1]  = value[1];
                buffer[2]  = value[2];
                buffer[3]  = value[3];
            }
            void EncodeDouble(Buffer res, double key) {
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
            void EncodeDecimal(Buffer res, decimal key) {
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
            void EncodeDateTime(Buffer res, DateTime key) {
                var item   = unchecked((ulong)key.Ticks);
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
            void EncodeTimeSpan(Buffer res, TimeSpan key) {
                var item   = unchecked((ulong)key.Ticks);
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
            void EncodeGUID(Buffer res, Guid key) {
                res.Length  = 16;
                //res.Content = key.ToByteArray();
                // manually copy because the data is too short
                var source = key.ToByteArray();
                var dest   = res.Content;
                for(int i = 0; i < 16; i++)
                    dest[i] = source[i];
            }
            void EncodeByteArray(Buffer res, byte[] key) {
                res.Length = key.Length;
                // manually copy because the data is too short
                var dest = res.Content;
                var max  = key.Length;
                for(int i = 0; i < max; i++)
                    dest[i] = key[i];
            }
        }
#else
        public static Action<Buffer, object> GetDefaultEncoder<T>() {
            if(typeof(T) == typeof(string))   return EncodeString;
            if(typeof(T) == typeof(int))      return EncodeInt32;
            if(typeof(T) == typeof(long))     return EncodeInt64;
            if(typeof(T) == typeof(double))   return EncodeDouble;
            if(typeof(T) == typeof(float))    return BitConverter.IsLittleEndian ? EncodeFloatLE : (Action<Buffer, object>)EncodeFloat;
            if(typeof(T) == typeof(DateTime)) return EncodeDateTime;
            if(typeof(T) == typeof(TimeSpan)) return EncodeTimeSpan;
            if(typeof(T) == typeof(byte[]))   return EncodeByteArray;
            if(typeof(T) == typeof(uint))     return EncodeUInt32;
            if(typeof(T) == typeof(ulong))    return EncodeUInt64;
            if(typeof(T) == typeof(char))     return EncodeChar;
            if(typeof(T) == typeof(sbyte))    return EncodeInt8;
            if(typeof(T) == typeof(short))    return EncodeInt16;
            if(typeof(T) == typeof(byte))     return EncodeUInt8;
            if(typeof(T) == typeof(ushort))   return EncodeUInt16;
            if(typeof(T) == typeof(bool))     return EncodeBool;
            if(typeof(T) == typeof(decimal))  return EncodeDecimal;
            if(typeof(T) == typeof(Guid))     return EncodeGUID;
                
            return null;
    
            void EncodeString(Buffer res, object key) {
                var item   = (string)key;
                var count  = Encoding.UTF8.GetMaxByteCount(item.Length);//Encoding.UTF8.GetByteCount(item);
                res.EnsureCapacity(count);
                res.Length = Encoding.UTF8.GetBytes(item, 0, item.Length, res.Content, 0);
                // could use Encoding.UTF8.GetEncoder().Convert() to avoid GetByteCount()
            }
            void EncodeChar(Buffer res, object key) {
                var item = (char)key;
                if(item <= 0x7F) {
                    res.Content[0] = (byte)item;
                    res.Length = 1;
                } else {
                    var temp   = new char[1] { (char)item };
                    res.Length = Encoding.UTF8.GetBytes(temp, 0, 1, res.Content, 0);
                }
            }
            void EncodeInt8(Buffer res, object key) {
                var item       = (sbyte)key;
                res.Length     = 1;
                res.Content[0] = unchecked((byte)item);
            }
            void EncodeInt16(Buffer res, object key) {
                var item       = (short)key;
                res.Length     = 2;
                res.Content[0] = unchecked((byte)((item >> 0) & 0xFF));
                res.Content[1] = unchecked((byte)((item >> 8) & 0xFF));
            }
            void EncodeInt32(Buffer res, object key) {
                var item   = (int)key;
                res.Length = 4;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)((item >> 0) & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
            }
            void EncodeInt64(Buffer res, object key) {
                var item   = (long)key;
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
            void EncodeUInt8(Buffer res, object key) {
                var item       = (byte)key;
                res.Length     = 1;
                res.Content[0] = item;
            }
            void EncodeUInt16(Buffer res, object key) {
                var item       = (ushort)key;
                res.Length     = 2;
                res.Content[0] = unchecked((byte)((item >> 0) & 0xFF));
                res.Content[1] = unchecked((byte)((item >> 8) & 0xFF));
            }
            void EncodeUInt32(Buffer res, object key) {
                var item   = (uint)key;
                res.Length = 4;
                var buffer = res.Content;
                buffer[0]  = unchecked((byte)((item >> 0) & 0xFF));
                buffer[1]  = unchecked((byte)((item >> 8) & 0xFF));
                buffer[2]  = unchecked((byte)((item >> 16) & 0xFF));
                buffer[3]  = unchecked((byte)((item >> 24) & 0xFF));
            }
            void EncodeUInt64(Buffer res, object key) {
                var item   = (ulong)key;
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
            void EncodeBool(Buffer res, object key) {
                var item   = (bool)key;
                res.Length = 1;
                res.Content[0] = item ? (byte)1 : (byte)0;
            }
            void EncodeFloatLE(Buffer res, object key) {
                var item       = (float)key;
                res.Length     = 4;
                var buffer     = res.Content;
                var value_uint = new UnionFloat() { Value = item }.Binary;
                buffer[0]      = unchecked((byte)((value_uint >> 0) & 0xFF));
                buffer[1]      = unchecked((byte)((value_uint >> 8) & 0xFF));
                buffer[2]      = unchecked((byte)((value_uint >> 16) & 0xFF));
                buffer[3]      = unchecked((byte)((value_uint >> 24) & 0xFF));
            }
            void EncodeFloat(Buffer res, object key) {
                var item   = (float)key;
                res.Length = 4;
                var buffer = res.Content;
                var value  = BitConverter.GetBytes(item);
                buffer[0]  = value[0];
                buffer[1]  = value[1];
                buffer[2]  = value[2];
                buffer[3]  = value[3];
            }
            void EncodeDouble(Buffer res, object key) {
                var item   = unchecked((ulong)BitConverter.DoubleToInt64Bits((double)key));
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
            void EncodeDecimal(Buffer res, object key) {
                var item   = (decimal)key;
                res.Length = 16;
                var buffer = res.Content;
                var bits   = decimal.GetBits(item);
    
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
            void EncodeDateTime(Buffer res, object key) {
                var item   = unchecked((ulong)((DateTime)key).Ticks);
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
            void EncodeTimeSpan(Buffer res, object key) {
                var item   = unchecked((ulong)((TimeSpan)key).Ticks);
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
            void EncodeGUID(Buffer res, object key) {
                res.Length  = 16;
                //res.Content = ((Guid)key).ToByteArray();
                // manually copy because the data is too short
                var source = ((Guid)key).ToByteArray();
                var dest   = res.Content;
                for(int i = 0; i < 16; i++)
                    dest[i] = source[i];
            }
            void EncodeByteArray(Buffer res, object key) {
                var item   = (byte[])key;
                res.Length = item.Length;
                // manually copy because the data is too short
                var dest = res.Content;
                var max  = item.Length;
                for(int i = 0; i < max; i++)
                    dest[i] = item[i];
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
            if(typeof(T) == typeof(Guid))     return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, Guid>(DecodeGUID));
            if(typeof(T) == typeof(byte[]))   return Unsafe.As<Func<byte[], int, int, T>>(new Func<byte[], int, int, byte[]>(DecodeByteArray));
    
            return null;
    
            string DecodeString(byte[] buffer, int start, int len) {
                return Encoding.UTF8.GetString(buffer, start, len);
            }
            char DecodeChar(byte[] buffer, int start, int len) {
                if(len == 1)
                    return (char)buffer[start];
                else {
                    var temp = new char[1];
                    Encoding.UTF8.GetChars(buffer, start, len, temp, 0);
                    return temp[0];
                }
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
                return b != 0;
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
            Guid DecodeGUID(byte[] buffer, int start, int len) {
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
            byte[] DecodeByteArray(byte[] buffer, int start, int len) {
                var res = new byte[len];
                // manually copy because the data is too short
                for(int i = 0; i < len; i++)
                    res[i] = buffer[start++];
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
            if(typeof(T) == typeof(Guid))     return DecodeGUID;
            if(typeof(T) == typeof(byte[]))   return DecodeByteArray;
                
            return null;
    
            object DecodeString(byte[] buffer, int start, int len) {
                return Encoding.UTF8.GetString(buffer, start, len);
            }
            object DecodeChar(byte[] buffer, int start, int len) {
                if(len == 1)
                    return (char)buffer[start];
                else {
                    var temp = new char[1];
                    Encoding.UTF8.GetChars(buffer, start, len, temp, 0);
                    return temp[0];
                }
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
                return b != 0;
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
            object DecodeGUID(byte[] buffer, int start, int len) {
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
            object DecodeByteArray(byte[] buffer, int start, int len) {
                var res = new byte[len];
                // manually copy because the data is too short
                for(int i = 0; i < len; i++)
                    res[i] = buffer[start++];
                return res;
            }
        }
#endif
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