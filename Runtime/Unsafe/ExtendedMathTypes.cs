using System;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using System.Runtime.CompilerServices;


namespace Unity.Mathematics
{
    [BurstCompile]
    [Serializable]
    [StructLayout(LayoutKind.Explicit, Size=4)]
    public struct fixed8_t4 : System.IEquatable<fixed8_t4>, IFormattable
    {

        [NonSerialized][FieldOffset(0)]public byte x;
        [NonSerialized][FieldOffset(1)]public byte y;
        [NonSerialized][FieldOffset(2)]public byte z;
        [NonSerialized][FieldOffset(3)]public byte w;

        public fixed8_t4(byte x, byte y, byte z, byte w)
        {
            
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w; 
        }

        public unsafe uint raw
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get 
            {
                fixed (fixed8_t4* raw = &this)
                {
                    return *((uint*) raw); 
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set 
            {
                 fixed (fixed8_t4* raw = &this) { *((uint*) raw) = value; }
            }
        }

        public unsafe byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (unchecked((uint)index >= 4))
                    throw new System.ArgumentException("index must be between[0...3]");
#endif
                fixed (fixed8_t4* array = &this) { return ((byte*)array)[index]; }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (unchecked((uint)index >= 4))
                    throw new System.ArgumentException("index must be between[0...3]");
#endif
                fixed (byte* array = &x) { array[index] = value; }
            }
        }

        public unsafe fixed8_t4(uint value)
        {
            this.x = (byte)0;
            this.y = (byte)0;
            this.z = (byte)0;
            this.w = (byte)0; 
            fixed (fixed8_t4* raw = &this) { *((uint*) raw) = value; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public fixed8_t4(uint4 value)
        {
            if (!X86.Ssse3.IsSsse3Supported)
            {
                this.x = (byte)value.x;
                this.y = (byte)value.y;
                this.z = (byte)value.z;
                this.w = (byte)value.w;
            }
            else
            {
                this = default;
                v128 input = new v128(value.x,value.y,value.z,value.w);
                v128 shuffle = new v128(0x0C080400U, 0x80808080U, 0x80808080U, 0x80808080U);
                v128 output = X86.Ssse3.shuffle_epi8(input, shuffle);
                this.raw = output.UInt0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(fixed8_t4 other)
        {
            return this.x == other.x && this.y == other.y && this.z == other.z && this.w == other.w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object other)
        {
            return other is fixed8_t4 f && f.Equals(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            return string.Format("fixed8_t4({0}, {1}, {2}, {3})", x, y, z, w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return string.Format("fixed8_t4({0}, {1}, {2}, {3})", 
                x.ToString(format, formatProvider), 
                y.ToString(format, formatProvider), 
                z.ToString(format, formatProvider), 
                w.ToString(format, formatProvider));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator uint4(fixed8_t4 f) 
        {
            if (!X86.Ssse3.IsSsse3Supported)
            {
                return new uint4(f.x, f.y, f.z, f.w);
            }
            else
            {
                v128 input = new v128(f.raw);
                v128 shuffle = new v128(0x80808000U, 0x80808001U, 0x80808002U, 0x80808003U);
                v128 output = X86.Ssse3.shuffle_epi8(input, shuffle);
                return new uint4(output.UInt0,output.UInt1,output.UInt2,output.UInt3);
            }
        }
       

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator fixed8_t4(uint4 f) 
        {
            if (!X86.Ssse3.IsSsse3Supported)
            {
                return new fixed8_t4(
                    (byte) f.x,
                    (byte) f.y,
                    (byte) f.z,
                    (byte) f.w
                    );
            }
            else
            {
                v128 input = new v128(f.x,f.y,f.z,f.w);
                v128 shuffle = new v128(0x0C080400U, 0x80808080U, 0x80808080U, 0x80808080U);
                v128 output = X86.Ssse3.shuffle_epi8(input, shuffle);
                return new fixed8_t4(output.UInt0);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator float4(fixed8_t4 f) => new float4((uint4)f) * (0.003921568393707275390625f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator fixed8_t4(float4 f) => new fixed8_t4((uint4)(f * 255.0000152587890625f));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() { return unchecked((int)this.raw); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 operator ==(fixed8_t4 a, fixed8_t4 b)
        {
            return new bool4(
                a.x == b.x, 
                a.y == b.y,
                a.z == b.z,
                a.w == b.w
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 operator !=(fixed8_t4 a, fixed8_t4 b)
        {
            return new bool4(
                a.x != b.x, 
                a.y != b.y,
                a.z != b.z,
                a.w != b.w
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 operator >(fixed8_t4 a, fixed8_t4 b)
        {
            return new bool4(
                a.x > b.x, 
                a.y > b.y,
                a.z > b.z,
                a.w > b.w
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 operator <(fixed8_t4 a, fixed8_t4 b)
        {
            return new bool4(
                a.x < b.x, 
                a.y < b.y,
                a.z < b.z,
                a.w < b.w
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 operator >=(fixed8_t4 a, fixed8_t4 b)
        {
            return new bool4(
                a.x >= b.x, 
                a.y >= b.y,
                a.z >= b.z,
                a.w >= b.w
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 operator <=(fixed8_t4 a, fixed8_t4 b)
        {
            return new bool4(
                a.x <= b.x, 
                a.y <= b.y,
                a.z <= b.z,
                a.w <= b.w
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fixed8_t4 operator +(fixed8_t4 a, fixed8_t4 b)
        {
            return new fixed8_t4(
                (byte)((uint)a.x + (uint)b.x), 
                (byte)((uint)a.y + (uint)b.y),
                (byte)((uint)a.z + (uint)b.z),
                (byte)((uint)a.w + (uint)b.w)
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fixed8_t4 operator -(fixed8_t4 a, fixed8_t4 b)
        {
            return new fixed8_t4(
                (byte)((uint)a.x - (uint)b.x), 
                (byte)((uint)a.y - (uint)b.y),
                (byte)((uint)a.z - (uint)b.z),
                (byte)((uint)a.w - (uint)b.w)
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fixed8_t4 operator *(fixed8_t4 a, fixed8_t4 b)
        {
            return new fixed8_t4(
                (byte)(((uint)a.x * (uint)b.x) >> 8), 
                (byte)(((uint)a.y * (uint)b.y) >> 8),
                (byte)(((uint)a.z * (uint)b.z) >> 8),
                (byte)(((uint)a.w * (uint)b.w) >> 8)
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static fixed8_t4 operator /(fixed8_t4 a, fixed8_t4 b)
        {
            return new fixed8_t4(
                (byte)(((uint)a.x << 8) / (uint)b.x), 
                (byte)(((uint)a.y << 8) / (uint)b.y),
                (byte)(((uint)a.z << 8) / (uint)b.z),
                (byte)(((uint)a.w << 8) / (uint)b.w)
                );
        }
    }
}