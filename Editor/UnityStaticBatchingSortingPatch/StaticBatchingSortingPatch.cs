using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;
using Unity.Burst;
//using Unity.Collections; //VRCSDK has conflict with collections, use copy-pasted file from collections instead
using SBSP.CollectionsPatch;
using Unity.Mathematics;
using static System.Reflection.BindingFlags;
using Object = UnityEngine.Object;

[BurstCompile]
static class HilbertCurve
{
    // calculate hilbert curve index from 2d/3d point coordinates
    // based on https://stackoverflow.com/a/10384110/5104533
    static ulong HilbertIndex(in uint3 point, int bitDepth, int dimensions)
    {
        uint3 hilbert = point;
        uint M = 1U << (bitDepth - 1);
        int bitCount = dimensions * bitDepth;

        // inverse undo
        for (uint Q = M; Q > 1; Q >>= 1)
        {
            uint P = Q - 1;

            for (int i = 0; i < dimensions; i++)
            {
                if ((hilbert[i] & Q) != 0)
                {
                    hilbert[0] ^= P; // invert
                }
                else
                {
                    uint t = (hilbert[0] ^ hilbert[i]) & P;
                    hilbert[0] ^= t;
                    hilbert[i] ^= t;
                }
            }
        }

        // gray encode
        {
            for (int i = 1; i < dimensions; i++)
                hilbert[i] ^= hilbert[i - 1];

            uint t = 0;

            for (uint Q = M; Q > 1; Q >>= 1)
                if ((hilbert[dimensions - 1] & Q) != 0)
                    t ^= Q - 1;

            for (int i = 0; i < dimensions; i++)
                hilbert[i] ^= t;
        }

        // untranspose
        {
            var bitField = default(BitField64);

            for (int i = 0; i < bitCount; ++i)
            {
                uint bit = (hilbert[i % dimensions] >> (i / dimensions)) & 0x1u;
                bitField.SetBits(i, bit > 0);
            }

            return bitField.Value;
        }
    }

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, CompileSynchronously = true)]
    public static ulong GetHilbertCurveIndexForWorldSpacePosition(in float3 position, float quantizationStepSize)
    {
        // offset the coordinates to make sure they're always positive
        // you shouldn't have any objects this far away from the camera anyway
        const int offset = 50000;

        // quantize the position to uint3
        uint3 point = (uint3) (position / quantizationStepSize + offset);

#if STATIC_BATCHING_PARTITION_3D
        return HilbertIndex(point, bitDepth: 20, dimensions: 3);
#else
        return HilbertIndex(new uint3(point.xz, 0), bitDepth: 32, dimensions: 2);
#endif
    }
}

[InitializeOnLoad]
static class StaticBatchingSortingPatch
{
    static StaticBatchingSortingPatch()
    {
        // the method we're replacing
        // (name/visibility changed over time)
        var oldMethod = typeof(StaticBatchingUtility).Assembly.GetType("UnityEngine.InternalStaticBatchingUtility").GetMethod(nameof(SortGameObjectsForStaticBatching), NonPublic | Static) ??
                        typeof(StaticBatchingUtility).Assembly.GetType("UnityEngine.InternalStaticBatchingUtility").GetMethod("SortGameObjectsForStaticbatching", Public | Static);

        // the new method that will be called instead
        var newMethod = typeof(StaticBatchingSortingPatch).GetMethod(nameof(SortGameObjectsForStaticBatching), NonPublic | Static);

        TryDetourFromTo(
            src: oldMethod,
            dst: newMethod
        );
    }

    // this is based on an interesting technique from the RimWorld ComunityCoreLibrary project, originally credited to RawCode:
    // https://github.com/RimWorldCCLTeam/CommunityCoreLibrary/blob/master/DLL_Project/Classes/Static/Detours.cs
    static unsafe void TryDetourFromTo(MethodInfo src, MethodInfo dst)
    {
        try
        {
            if (IntPtr.Size == sizeof(Int64))
            {
                // 64-bit systems use 64-bit absolute address and jumps
                // 12 byte destructive

                // Get function pointers
                long Source_Base = src.MethodHandle.GetFunctionPointer().ToInt64();
                long Destination_Base = dst.MethodHandle.GetFunctionPointer().ToInt64();

                // Native source address
                byte* Pointer_Raw_Source = (byte*) Source_Base;

                // Pointer to insert jump address into native code
                long* Pointer_Raw_Address = (long*) (Pointer_Raw_Source + 0x02);

                // Insert 64-bit absolute jump into native code (address in rax)
                // mov rax, immediate64
                // jmp [rax]
                *(Pointer_Raw_Source + 0x00) = 0x48;
                *(Pointer_Raw_Source + 0x01) = 0xB8;
                *Pointer_Raw_Address = Destination_Base; // ( Pointer_Raw_Source + 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 )
                *(Pointer_Raw_Source + 0x0A) = 0xFF;
                *(Pointer_Raw_Source + 0x0B) = 0xE0;
            }
            else
            {
                // 32-bit systems use 32-bit relative offset and jump
                // 5 byte destructive

                // Get function pointers
                int Source_Base = src.MethodHandle.GetFunctionPointer().ToInt32();
                int Destination_Base = dst.MethodHandle.GetFunctionPointer().ToInt32();

                // Native source address
                byte* Pointer_Raw_Source = (byte*) Source_Base;

                // Pointer to insert jump address into native code
                int* Pointer_Raw_Address = (int*) (Pointer_Raw_Source + 1);

                // Jump offset (less instruction size)
                int offset = (Destination_Base - Source_Base) - 5;

                // Insert 32-bit relative jump into native code
                *Pointer_Raw_Source = 0xE9;
                *Pointer_Raw_Address = offset;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unable to detour: {src?.Name ?? "UnknownSrc"} -> {dst?.Name ?? "UnknownDst"}\n{ex}");
            throw;
        }
    }

    // https://github.com/Unity-Technologies/UnityCsReference/blob/61f92bd79ae862c4465d35270f9d1d57befd1761/Runtime/Export/StaticBatching/CombineForStaticBatching.cs#L60-L76
    static GameObject[] SortGameObjectsForStaticBatching(GameObject[] gameObjects, [UsedImplicitly] object sorter)
    {
        bool isBuildingPlayer = sorter.GetType().Name == "EditorStaticBatcherGOSorter";
        Debug.Log("Executing Static Batching");
        var gameObjectsOrderedByMaterialId = isBuildingPlayer
            ? gameObjects.OrderBy(x => GetMaterialIdAtBuildTime(GetRenderer(x)))
            : gameObjects.OrderBy(x => GetMaterialId(GetRenderer(x)));

        return gameObjectsOrderedByMaterialId
            .ThenBy(x => GetLightmapIndex(GetRenderer(x)))
            .ThenBy(x => HilbertCurve.GetHilbertCurveIndexForWorldSpacePosition(x.transform.position, quantizationStepSize: 0.1f))
            .ToArray();
    }

    // the original GetFileIDHint method is internal, so we're creating a delegate to access it by reflection 
    // todo: can we use UnityEditor.Unsupported.GetLocalIdentifierInFileForPersistentObjectInternal instead?
    static readonly Func<Object, ulong> GetFileIDHint
        = (Func<Object, ulong>)
        typeof(Unsupported)
            .GetMethod("GetFileIDHint", NonPublic | Static)
            .CreateDelegate(typeof(Func<Object, ulong>));

    // https://github.com/Unity-Technologies/UnityCsReference/blob/61f92bd79ae862c4465d35270f9d1d57befd1761/Editor/Mono/PostprocessScene.cs#L35-L47
    static long GetStableHash(Object instance, string guid)
    {
        using (var md5Hash = System.Security.Cryptography.MD5.Create())
        {

            var guidBytes = Encoding.ASCII.GetBytes(guid);
            md5Hash.TransformBlock(guidBytes, 0, guidBytes.Length, null, 0);

            ulong lfid = GetFileIDHint(instance);
            var lfidBytes = BitConverter.GetBytes(lfid);
            md5Hash.TransformFinalBlock(lfidBytes, 0, lfidBytes.Length);

            return BitConverter.ToInt64(md5Hash.Hash, 0);
        }
    }

    // https://github.com/Unity-Technologies/UnityCsReference/blob/61f92bd79ae862c4465d35270f9d1d57befd1761/Editor/Mono/PostprocessScene.cs#L49-L57
    static long GetMaterialIdAtBuildTime(Renderer renderer)
    {
        if (!renderer)
            return 0;

        var rendererSharedMaterial = renderer.sharedMaterial;

        if (!rendererSharedMaterial)
            return 0;

        string path = AssetDatabase.GetAssetPath(rendererSharedMaterial);
        string guid = AssetDatabase.AssetPathToGUID(path);
        return GetStableHash(rendererSharedMaterial, guid);
    }

    // https://github.com/Unity-Technologies/UnityCsReference/blob/61f92bd79ae862c4465d35270f9d1d57befd1761/Runtime/Export/StaticBatching/CombineForStaticBatching.cs#L258-L264
    static long GetMaterialId(Renderer renderer)
    {
        if (!renderer)
            return 0;

        var rendererSharedMaterial = renderer.sharedMaterial;

        if (!rendererSharedMaterial)
            return 0;

        return rendererSharedMaterial.GetInstanceID();
    }

    // https://github.com/Unity-Technologies/UnityCsReference/blob/61f92bd79ae862c4465d35270f9d1d57befd1761/Runtime/Export/StaticBatching/CombineForStaticBatching.cs#L273-L282
    static Renderer GetRenderer(GameObject go)
    {
        if (!go)
            return null;

        var filter = go.GetComponent<MeshFilter>();

        if (!filter)
            return null;

        return filter.GetComponent<Renderer>();
    }

    // https://github.com/Unity-Technologies/UnityCsReference/blob/61f92bd79ae862c4465d35270f9d1d57befd1761/Runtime/Export/StaticBatching/CombineForStaticBatching.cs#L266-L271
    static int GetLightmapIndex(Renderer renderer)
        => renderer
            ? renderer.lightmapIndex
            : -1;
}
