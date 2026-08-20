using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    [StructLayout(LayoutKind.Explicit)]
    public struct FoveationDataOld
    {
        public const uint shadingRateType = ('v' << 24) | ('u' << 16) | ('l' << 8) | ('k');
        public const uint fragDensityType = ('v' << 24) | ('k' << 16) | ('F' << 8) | ('D');

        [FieldOffset(0)] public ulong  size; // in 2022 this is 56 // U6: always 88, seems like the size of this struct

        // 4 char array, but C# doesn't support fixed length arrays. 
        // Observed values are 'kluv' when on pc, and 'DFkv' when on quest 3. 
        // if these are reversed, we get vulk and vkFD (fd = fragment density).
        // Probably they packed the chars into a uint and got the endianness mixed up
        [FieldOffset(8)] public uint   typeName;
        [FieldOffset(8)] public byte   typeName0;
        [FieldOffset(9)] public byte   typeName1;
        [FieldOffset(10)]public byte   typeName2;
        [FieldOffset(11)]public byte   typeName3;

       
        // Unity 6: Always 03 00 02 00, a single int or two shorts? Version number maybe?
        [FieldOffset(12)]public uint   version;
        [FieldOffset(12)]public ushort three;
        [FieldOffset(14)]public ushort two;


        // Definitely the shading rate/fragment density map width/height
        [FieldOffset(16)]public uint   width;
        [FieldOffset(20)]public uint   height;

        // Foveated rendering level, always equal to what's set from XRDisplaySubsystem.foveatedRenderingLevel
        [FieldOffset(24)]public float  level;

        // Eye screen coordinate centers in pixels.
        [FieldOffset(28)]public Vector2 eyeLeftCenter;
        [FieldOffset(36)]public Vector2 eyeRightCenter;

        /* seemingly no stretch factor in 2022
        // Eight floats in a row. Float4x2 so I don't have to define offsets for each. Each seems to stretch one side of the foveation circle in the order of left, right, down, up
        [FieldOffset(44)]public float4x2 stretchFactor;
        */
        
        /* U6 Notes: 
        // If the first field really is size, this suggests there should be 12 more bytes at the end
        // These bytes are always all 0 on PC, and something like 00 00 00 00 | 00 14 c0 83 | 76 00 00 b4 on quest.
        // If there were a uint64 at the end, it would need to be aligned on 8 bytes, so 4 bytes would be skipped to start on 80 hence the 4 blank bytes
        // 0xb400007683c01400 looks like it could be a vulkan handle

        [FieldOffset(80)]public ulong handle;
        */
        [FieldOffset(48)]public ulong handle;

        public override string ToString()
        {
            return "{"+
            $"\n  size:   {this.size}" +
            $"\n  name:   {SafeB2S(typeName3)}{SafeB2S(typeName2)}{SafeB2S(typeName1)}{SafeB2S(typeName0)}" +
            $"\n  version:  0x{version:x4}" +
            $"\n  width:  {width}" +
            $"\n  height: {height}" +
            $"\n  level:  {level}" +
            $"\n  eyeLeftCenter:  {eyeLeftCenter.x}, {eyeLeftCenter.y}" +
            $"\n  eyeRightCenter: {eyeRightCenter.x}, {eyeRightCenter.y}" +
            //$"\n  stretchFactor: {stretchFactor}" +
            $"\n  handle: 0x{handle:x8}" +
            "\n}";
        }

        string SafeB2S(byte b)
        {
            return b == '\0' ? "\\0" : ((char)b).ToString();
        }

        public static FoveationDataOld FromPointer(IntPtr pointer)
        {

            return Marshal.PtrToStructure<FoveationDataOld>(pointer);
        }

        public static void FixOpenXREyeOffsets(IntPtr foveatedRenderingInfo)
        {
            unsafe
            {
                float* leftEyeCenter  = (float*)(foveatedRenderingInfo + 28);
                float* rightEyeCenter = (float*)(foveatedRenderingInfo + 36);
                float leftEyeCenterVal = *leftEyeCenter;
                *leftEyeCenter = *rightEyeCenter;
                *rightEyeCenter = leftEyeCenterVal;
            }
        }
    }
}
