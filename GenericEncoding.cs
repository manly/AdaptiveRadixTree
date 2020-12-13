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
                
            return null;
    
            void EncodeString(string key, Buffer res) {
                var count  = Encoding.UTF8.GetMaxByteCount(key.Length); //Encoding.UTF8.GetByteCount(key);
                res.EnsureCapacity(count);
                res.Length = Encoding.UTF8.GetBytes(key, 0, key.Length, res.Content, 0);
                // could use Encoding.UTF8.GetEncoder().Convert() to avoid GetByteCount()
            }
            void EncodeChar(char key, Buffer res) {
                var item   = new char[1] { (char)key };
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
                res.Content[0] = key ? (byte)1 : (byte)0;
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
                
            return null;
    
            void EncodeString(object key, Buffer res) {
                var item   = (string)key;
                var count  = Encoding.UTF8.GetMaxByteCount(item.Length);//Encoding.UTF8.GetByteCount(item);
                res.EnsureCapacity(count);
                res.Length = Encoding.UTF8.GetBytes(item, 0, item.Length, res.Content, 0);
                // could use Encoding.UTF8.GetEncoder().Convert() to avoid GetByteCount()
            }
            void EncodeChar(object key, Buffer res) {
                var item   = new char[1] { (char)key };
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
                res.Content[0] = item ? (byte)1 : (byte)0;
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
            object DecodeByteArray(byte[] buffer, int start, int len) {
                var res = new byte[len];
                BlockCopy(buffer, start, res, 0, len);
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
    }

}