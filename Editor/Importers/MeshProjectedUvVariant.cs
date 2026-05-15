#if !UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS
#error MeshProjectedUvVariant needs UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS set in Project Settings->Player->Scripting Define Symbols!
#endif
using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEditor.AssetImporters;
using UnityEngine;
using Unity.Collections;
using UnityEditor;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using System;
using UnityEditorInternal;
using UnityEngine.Rendering;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;
using SLZ.SLZEditorTools;

[ScriptedImporter(version: 21, exts: new string[] {"projUV"}, overrideExts: new string[] {"asset"},  AllowCaching = true)]
public class MeshProjectedUvVariant : ScriptedImporter
{

    public enum ProjectionMethod
    {
        Flat = 0,
        Triplanar = 1
    }

    delegate bool d_GetGUIDAndLocalIdentifierInFile(int instanceID, out GUID outGuid, out long outLocalId);
    static d_GetGUIDAndLocalIdentifierInFile s_GetGUIDAndLocalIdentifierInFile;
    static d_GetGUIDAndLocalIdentifierInFile GetGUIDAndLocalIdentifierInFile
    {
        get 
        {
            if (s_GetGUIDAndLocalIdentifierInFile == null)
            {
                MethodInfo mi = typeof(AssetDatabase).GetMethod("GetGUIDAndLocalIdentifierInFile", BindingFlags.Static | BindingFlags.NonPublic);
                s_GetGUIDAndLocalIdentifierInFile = (d_GetGUIDAndLocalIdentifierInFile)mi.CreateDelegate(typeof(d_GetGUIDAndLocalIdentifierInFile));
            }
            return s_GetGUIDAndLocalIdentifierInFile;
        }
    }

    static GUID defaultResourceGUID = new GUID("0000000000000000e000000000000000");

    public LazyLoadReference<Mesh> parentMesh;
    
    public float4x4 projectionSpace = Unity.Mathematics.float4x4.identity;

    public ProjectionMethod projectionMethod = ProjectionMethod.Flat;

    //int counter = 0;

    public override void OnImportAsset(AssetImportContext ctx)
    {
        //Debug.Log($"Import Count {counter}");
        //counter++;
        UnityEngine.Object[] stuff = null;
        try
        {
            stuff = InternalEditorUtility.LoadSerializedFileAndForget(ctx.assetPath);
        }
        catch
        {

        }
        MeshVariantReference mvr = stuff != null && stuff.Length > 0 ? stuff[0] as MeshVariantReference : ScriptableObject.CreateInstance<MeshVariantReference>();
        ctx.AddObjectToAsset("meshReference", mvr);
        mvr.name = "SourceObjectReference";
        Mesh newMesh = new Mesh();
        newMesh.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
        if (!parentMesh.isSet)
        {
            ctx.AddObjectToAsset("mesh", newMesh);
            ctx.SetMainObject(newMesh);
            return;
        }

        string path = AssetDatabase.GetAssetPath(parentMesh.instanceID);
        if (path != null)
        {
            //AssetDatabase.TryGetGUIDAndLocalFileIdentifier(parentMesh.instanceID, out string guid, ou)
            GetGUIDAndLocalIdentifierInFile(parentMesh.instanceID, out GUID guid, out long localId);
            //Debug.Log($"Dependency GUID: {guid}");
            if (guid != defaultResourceGUID)
            {
                ctx.DependsOnArtifact(path);
                //ctx.DependsOnSourceAsset(path);
            }
        }
        Mesh oldMesh = parentMesh.asset;
     
        if (oldMesh.vertexBufferCount > 1)
        {
            ctx.LogImportError($"Cannot use mesh {oldMesh.name}, this importer does not support meshes with multiple vertex buffers");
        }
        else
        {
            if (projectionMethod == ProjectionMethod.Flat)
            {
                ProjectMono(oldMesh, newMesh);
            }
            else
            {
                ProjectTri(oldMesh, newMesh, projectionSpace);
            }
        }
        ctx.AddObjectToAsset("mesh", newMesh);
        ctx.SetMainObject(newMesh);
    }


    void ProjectMono(Mesh oldMesh, Mesh newMesh)
    {

        Mesh.MeshDataArray oldDataArray = MeshUtility.AcquireReadOnlyMeshData(oldMesh);
        Mesh.MeshDataArray newDataArray = Mesh.AllocateWritableMeshData(1);
        Mesh.MeshData oldMeshData = oldDataArray[0];
        Mesh.MeshData newMeshData = newDataArray[0];

        int vertexCount = oldMeshData.vertexCount;
        int vertexStride = oldMeshData.GetVertexBufferStride(0);
        int uvOffset = oldMeshData.GetVertexAttributeOffset(VertexAttribute.TexCoord0);
        int normalOffset = oldMeshData.GetVertexAttributeOffset(VertexAttribute.Normal);
        int tangentOffset = oldMeshData.GetVertexAttributeOffset(VertexAttribute.Tangent);

        MeshUpdateFlags nothing = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds;
        
        newMeshData.SetVertexBufferParams(vertexCount, oldMesh.GetVertexAttributes());
        NativeArray<ushort> oldIndexBuffer = oldMeshData.GetIndexData<ushort>();
        newMeshData.SetIndexBufferParams(oldIndexBuffer.Length / (oldMeshData.indexFormat == IndexFormat.UInt32 ? 2 : 1), oldMeshData.indexFormat);
        NativeArray<ushort> newIndexBuffer = newMeshData.GetIndexData<ushort>();
        newIndexBuffer.CopyFrom(oldIndexBuffer);

        int numSubmeshes = oldMeshData.subMeshCount;
        newMeshData.subMeshCount = numSubmeshes;
        for (int smIdx = 0; smIdx < numSubmeshes; smIdx++)
        {
            newMeshData.SetSubMesh(smIdx, oldMeshData.GetSubMesh(smIdx), nothing);
        }

        NativeArray<uint> oldVtxBuffer = oldMeshData.GetVertexData<uint>(0);
        NativeArray<uint> newVtxBuffer = newMeshData.GetVertexData<uint>(0);
        newVtxBuffer.CopyFrom(oldVtxBuffer);

        ProjectMonoJob projJob = new()
        {
            meshDataLength = (uint)newVtxBuffer.Length,
            meshData = newVtxBuffer,
            vertexStride    = vertexStride / 4,
            uvOffset        = uvOffset / 4,
            normalOffset    = normalOffset / 4,
            tangentOffset   = tangentOffset / 4,
            projectionSpace = this.projectionSpace,
            invProjectionSpace = math.inverse(this.projectionSpace)
        };
        int numVerts = projJob.meshData.Length;
        JobHandle jh = projJob.Schedule(numVerts, 256);
        jh.Complete();
        Mesh.ApplyAndDisposeWritableMeshData(newDataArray, newMesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds);
        oldDataArray.Dispose();
        newMesh.bounds = oldMesh.bounds;
        newMesh.RecalculateUVDistributionMetrics();
        newMesh.UploadMeshData(true);
    }

        #if HLSL

        float3 Load3Floats(RWByteAddressBuffer buffer, int address)
        {
            return asfloat(buffer.Load3((uint)address));
        }

        void Store2Floats(RWByteAddressBuffer buffer, int address, float2 value)
        {
            buffer.Store2((uint) address, asuint(value));
        }

        void Store4Floats(RWByteAddressBuffer buffer, int address, float4 value)
        {
            buffer.Store4((uint) address, asuint(value));
        }

        #else

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3 Load3Floats(ref NativeArray<uint> buffer, int address)
        {
            int intAddress = (int)address;
            return asfloat(uint3((buffer[intAddress    ]), 
                                 (buffer[intAddress + 1]),
                                 (buffer[intAddress + 2])));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Store2Floats(ref NativeArray<uint> buffer, int address, float2 value)
        {
            uint2 uintVal = asuint(value);
            buffer[address    ] = uintVal.x;
            buffer[address + 1] = uintVal.y;
            //buffer[address + 2] = uintVal.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Store4Floats(ref NativeArray<uint> buffer, int address, float4 value)
        {
            uint4 uintVal = asuint(value);
            buffer[address    ] = uintVal.x;
            buffer[address + 1] = uintVal.y;
            buffer[address + 2] = uintVal.z;
            buffer[address + 3] = uintVal.w;
        }
        #endif

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, CompileSynchronously = true)]
    struct ProjectMonoJob : IJobParallelFor
    {
        #if HLSL
        RWByteAddressBuffer
        #else
        [NativeDisableParallelForRestriction]
        public NativeArray<uint> 
        #endif
            meshData;

        public uint meshDataLength;
        public int vertexStride;
        public int uvOffset;
        public int normalOffset;
        public int tangentOffset;

        public float4x4 projectionSpace;
        public float4x4 invProjectionSpace;



        #if HLSL
        []
        #endif
        public void Execute(int i)
        {
            int vtxOffset = i * vertexStride;

            if ((vtxOffset + vertexStride) > meshDataLength)
            {
                return;
            }

            float4 positionOS = float4(Load3Floats(ref meshData, vtxOffset), 1.0f);
            float2 newUV = mul(projectionSpace, positionOS).xy;

            int absNormalOffset = vtxOffset + normalOffset;
            float3 normal = Load3Floats(ref meshData, absNormalOffset);
            normal = normalize(normal);
            float3 tangent2 = float3(invProjectionSpace[0][0],
                                     invProjectionSpace[1][0],
                                     invProjectionSpace[2][0]);
            float3 bitangent2 = float3( invProjectionSpace[0][1],
                                        invProjectionSpace[1][1],
                                        invProjectionSpace[2][1]);
            tangent2 = normalize(tangent2);
            float NoT = dot(tangent2, normal);
            if (abs(NoT) >= 0.999999)
            {
                tangent2 = -bitangent2; 
            }
            float3 tangent = tangent2 - ((dot(tangent2, normal) / dot(normal, normal)) * normal);
            tangent = normalize(tangent);

           

            float3 binormal = cross(tangent2, bitangent2);

            float bitangentSign = dot(normal, binormal) > 0 ? 1 : -1;
            
            int absUvOffset = vtxOffset + uvOffset;
            Store2Floats(ref meshData, absUvOffset, newUV);

            int absTanOffset = vtxOffset + tangentOffset;

            Store4Floats(ref meshData, absTanOffset, float4(tangent, bitangentSign));
        }
    }

    //[MenuItem("TEST/DbgProjectTri")]
    //public static void DbgProjectTri()
    //{
    //    Mesh m = (Mesh) Selection.activeObject;
    //    ProjectTri(m, null, Unity.Mathematics.float4x4.identity);
    //}

    static void ProjectTri(Mesh oldMesh, Mesh newMesh, float4x4 projectionSpace)
    {
        
        Mesh.MeshDataArray oldDataArray = MeshUtility.AcquireReadOnlyMeshData(oldMesh);
        Mesh.MeshDataArray newDataArray = Mesh.AllocateWritableMeshData(1);
        Mesh.MeshData oldMeshData = oldDataArray[0];
        Mesh.MeshData newMeshData = newDataArray[0];

        int oldVertexCount = oldMeshData.vertexCount;
        int vertexStride = oldMeshData.GetVertexBufferStride(0);
        int uvOffset = oldMeshData.GetVertexAttributeOffset(VertexAttribute.TexCoord0);
        //int normalOffset = oldMeshData.GetVertexAttributeOffset(VertexAttribute.Normal);
        //int tangentOffset = oldMeshData.GetVertexAttributeOffset(VertexAttribute.Tangent);

        bool largeIdxFmt = oldMeshData.indexFormat != IndexFormat.UInt16;
        
        NativeArray<ushort> oldIndexBuffer = oldMeshData.GetIndexData<ushort>();
        int oldIndex2ByteCnt = oldIndexBuffer.Length;
        int oldIndexCount = largeIdxFmt ? oldIndex2ByteCnt / 2 : oldIndexBuffer.Length;
        NativeSlice<uint> oldIndexBuffer32 = oldIndexBuffer.Slice(0, (oldIndex2ByteCnt / 2) * 2).SliceConvert<uint>();


        int oldTriCount = oldIndexCount / 3;
        NativeArray<uint> oldVertexBuffer = oldMeshData.GetVertexData<uint>(0);

        MeshUpdateFlags nothing = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds;
        
        NativeList<int>             vtxFaceFlags   = default;
        NativeList<TriplanarCount>  triCountPerDir = default;
        NativeArray<TriplanarCount> vtxCountPerDir = default;
        NativeArray<byte>           triFaceDir     = default;
        NativeArray<ushort>         oldToNewVtxMap = default;
        try
        {

        // Has to be a native list because unity doesn't provide ref access to elements of nativearrays necessary for atomics
        vtxFaceFlags = new NativeList<int>(oldVertexCount, Allocator.TempJob);
        vtxFaceFlags.Resize(oldVertexCount, NativeArrayOptions.ClearMemory);
        // We need to return a single TriplanarCount from the job, and we need to do atomic operations on it's members. 
        // Only way to get something back from a job is in native memory, and nativearrays can't do atomics because we can't ref their elements
        triCountPerDir = new NativeList<TriplanarCount>(1, Allocator.TempJob);
        triCountPerDir.Add(new TriplanarCount());

        triFaceDir = new NativeArray<byte>(oldIndexCount / 3, Allocator.TempJob);

        TriMarkFaceOrientationJob markFacesJob = new ()
        {
            vtxFaceFlags      = vtxFaceFlags,
            triFaceDir        = triFaceDir,
            triFaceCount      = triCountPerDir,
            indexBuffer       = oldIndexBuffer,
            indexBuffer32     = oldIndexBuffer32,
            i32IdxBuffer      = largeIdxFmt,
            vertexBuffer      = oldVertexBuffer,
            vertex4ByteStride = vertexStride / 4,
            projectionSpace   = (float3x3) projectionSpace
        };

        JobHandle markFacesJH = markFacesJob.Schedule(oldTriCount, 2048);
        markFacesJH.Complete();
        int chunkSize = max(512, (oldVertexCount + 31) / 32);
        int chunkCount = (oldVertexCount + chunkSize - 1) / chunkSize;
        vtxCountPerDir = new NativeArray<TriplanarCount>(chunkCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        NativeArray<int> vtxFaceFlagsArray = vtxFaceFlags.AsArray();
        CalculateVertexBufferSizeJob vtxBufSizeJob = new CalculateVertexBufferSizeJob() 
        {
            vtxFaceFlags    = vtxFaceFlagsArray,
            outSize         = vtxCountPerDir,
            chunkSize       = chunkSize
        };
        JobHandle vtxBufSizeJh = vtxBufSizeJob.Schedule(chunkCount, 1);
        vtxBufSizeJh.Complete();
        TriplanarCount totalVtxCountPerDir = new TriplanarCount();
        int newVtxCount = 0;
        for (int cIdx = 0; cIdx < chunkCount; cIdx++)
        {
            totalVtxCountPerDir.xn += vtxCountPerDir[cIdx].xn;
            totalVtxCountPerDir.xp += vtxCountPerDir[cIdx].xp;
            totalVtxCountPerDir.yn += vtxCountPerDir[cIdx].yn;
            totalVtxCountPerDir.yp += vtxCountPerDir[cIdx].yp;
            totalVtxCountPerDir.zn += vtxCountPerDir[cIdx].zn;
            totalVtxCountPerDir.zp += vtxCountPerDir[cIdx].zp;

            newVtxCount += vtxCountPerDir[cIdx].xn;
            newVtxCount += vtxCountPerDir[cIdx].xp;
            newVtxCount += vtxCountPerDir[cIdx].yn;
            newVtxCount += vtxCountPerDir[cIdx].yp;
            newVtxCount += vtxCountPerDir[cIdx].zn;
            newVtxCount += vtxCountPerDir[cIdx].zp;
        }
        /*
        Debug.Log($"Original vertex count: {oldVertexCount}, new vertex count: {newVtxCount}\n" + 
        "Direction Counts:\n"+
        $"X-: tris: {triCountPerDir[0].xn, -10}, vtxs: {totalVtxCountPerDir.xn}\n" +
        $"X+: tris: {triCountPerDir[0].xp, -10}, vtxs: {totalVtxCountPerDir.xp}\n" +
        $"Y-: tris: {triCountPerDir[0].yn, -10}, vtxs: {totalVtxCountPerDir.yn}\n" +
        $"Y+: tris: {triCountPerDir[0].yp, -10}, vtxs: {totalVtxCountPerDir.yp}\n" +
        $"Z-: tris: {triCountPerDir[0].zn, -10}, vtxs: {totalVtxCountPerDir.zn}\n" +
        $"Z+: tris: {triCountPerDir[0].zp, -10}, vtxs: {totalVtxCountPerDir.zp}\n"
        );
        */
        bool newLargeIdxFmt = newVtxCount >= 0xFFFFF;

        newMeshData.SetVertexBufferParams(newVtxCount, oldMesh.GetVertexAttributes());
        newMeshData.SetIndexBufferParams(oldIndexCount, newLargeIdxFmt ? IndexFormat.UInt32 : IndexFormat.UInt16);
       
        NativeArray<ushort> newIndexBuffer = newMeshData.GetIndexData<ushort>();
        
        int newIndex2ByteCnt = newIndexBuffer.Length;
        NativeSlice<uint> newIndexBuffer32 = newIndexBuffer.Slice(0, (newIndex2ByteCnt / 2) * 2).SliceConvert<uint>();

        int numSubmeshes = oldMeshData.subMeshCount;
        newMeshData.subMeshCount = numSubmeshes;
        for (int smIdx = 0; smIdx < numSubmeshes; smIdx++)
        {
            newMeshData.SetSubMesh(smIdx, oldMeshData.GetSubMesh(smIdx), nothing);
        }


        NativeArray<uint> newVertexBuffer = newMeshData.GetVertexData<uint>();
        oldToNewVtxMap = new NativeArray<ushort>(newLargeIdxFmt ? 2 * oldVertexCount : oldVertexCount, Allocator.TempJob);
        NativeSlice<uint> oldToNewVtxMap32 = oldToNewVtxMap.Slice(0, (oldToNewVtxMap.Length / 2) * 2).SliceConvert<uint>();
        bool uv0isF16 = oldMeshData.GetVertexAttributeFormat(VertexAttribute.TexCoord0) == VertexAttributeFormat.Float16;
        ReorderVertexBuffer(
        ref oldVertexBuffer, 
        ref newVertexBuffer, 
        ref vtxFaceFlagsArray, 
        ref triFaceDir,
        ref oldToNewVtxMap,
        ref oldToNewVtxMap32,
        ref oldIndexBuffer,
        ref oldIndexBuffer32,
        ref newIndexBuffer,
        ref newIndexBuffer32,
        projectionSpace,
        vertexStride / 4,
        uvOffset / 4,
        uv0isF16,
        largeIdxFmt,
        newLargeIdxFmt
        );

        Mesh.ApplyAndDisposeWritableMeshData(newDataArray, newMesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontResetBoneBounds);
        newMesh.bounds = oldMesh.bounds;
        newMesh.RecalculateUVDistributionMetrics();
        newMesh.UploadMeshData(true);

        }
        finally
        {
            if (vtxFaceFlags  .IsCreated) vtxFaceFlags  .Dispose();
            if (triCountPerDir.IsCreated) triCountPerDir.Dispose();
            if (vtxCountPerDir.IsCreated) vtxCountPerDir.Dispose();
            if (triFaceDir    .IsCreated) triFaceDir    .Dispose();
            if (oldToNewVtxMap.IsCreated) oldToNewVtxMap.Dispose();
            oldDataArray.Dispose();
        }
        //
        

    }

    struct TriplanarCount
    {
        public int xp;
        public int xn;
        public int yp;
        public int yn;
        public int zp;
        public int zn;

        public int Sum()
        {
            return xp + xn + yp + yn + zp + zn;
        }

        public int this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return xp;
                    case 1: return xn;
                    case 2: return yp;
                    case 3: return yn;
                    case 4: return zp;
                    case 5: return zn;
                    default: new System.ArgumentException("index must be between [0...5]"); return 0;
                }
            }
            set
            {
                switch (i)
                {
                    case 0: xp = value; break;
                    case 1: xn = value; break;
                    case 2: yp = value; break;
                    case 3: yn = value; break;
                    case 4: zp = value; break;
                    case 5: zn = value; break;
                    default: new System.ArgumentException("index must be between [0...5]"); return;
                }
            }
        } 
    }

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, CompileSynchronously = true)]
    struct TriMarkFaceOrientationJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeList<int> vtxFaceFlags;
        [WriteOnly]
        public NativeArray<byte> triFaceDir;
        [NativeDisableParallelForRestriction]
        public NativeList<TriplanarCount> triFaceCount;

        [ReadOnly]
        public NativeArray<ushort> indexBuffer;
        [ReadOnly]
        public NativeSlice<uint> indexBuffer32;
        [ReadOnly]
        public bool i32IdxBuffer;

        [ReadOnly]
        public NativeArray<uint> vertexBuffer;

        public int vertex4ByteStride;

        public float3x3 projectionSpace;

        public void Execute(int i)
        {
            int triIndex = 3 * i;
            int index0 = 0;
            int index1 = 0;
            int index2 = 0;
            index0 = ReadIndex(triIndex);
            index1 = ReadIndex(triIndex + 1);
            index2 = ReadIndex(triIndex + 2);
            

            float3 vtx0 = GetVtx(index0);
            float3 vtx1 = GetVtx(index1);
            float3 vtx2 = GetVtx(index2);

            float3 edge0 = mul(projectionSpace, vtx2 - vtx1);
            float3 edge1 = mul(projectionSpace, vtx0 - vtx1);
            float3 normal = cross(edge0, edge1);
            float normLenSq = dot(normal, normal);
            
            if (normLenSq == 0)
            {
                normal = float3(0,1,0);
                normLenSq = 1;
            }

            normal = normal * rsqrt(normLenSq);

            float3 absNorm = abs(normal);
            int bitmask = 0;
            if (absNorm.x >= absNorm.y && absNorm.x >= absNorm.z)
            {
                bitmask = normal.x > 0 ? 1 : 1 << 1; 
            }
            else if (absNorm.y >= absNorm.x && absNorm.y >= absNorm.z)
            {
                bitmask = normal.y > 0 ? 1 << 2 : 1 << 3; 
            }
            else
            {
                bitmask = normal.z > 0 ? 1 << 4 : 1 << 5; 
            }
            triFaceDir[i] = (byte)bitmask;
            switch (bitmask)
            {
                case 1 << 0: Interlocked.Increment(ref triFaceCount.ElementAt(0).xp); break;
                case 1 << 1: Interlocked.Increment(ref triFaceCount.ElementAt(0).xn); break;
                case 1 << 2: Interlocked.Increment(ref triFaceCount.ElementAt(0).yp); break;
                case 1 << 3: Interlocked.Increment(ref triFaceCount.ElementAt(0).yn); break;
                case 1 << 4: Interlocked.Increment(ref triFaceCount.ElementAt(0).zp); break;
                case 1 << 5: Interlocked.Increment(ref triFaceCount.ElementAt(0).zn); break;
            }
            Unity.Burst.Intrinsics.Common.InterlockedOr(ref vtxFaceFlags.ElementAt(index0), bitmask);
            Unity.Burst.Intrinsics.Common.InterlockedOr(ref vtxFaceFlags.ElementAt(index1), bitmask);
            Unity.Burst.Intrinsics.Common.InterlockedOr(ref vtxFaceFlags.ElementAt(index2), bitmask);
        }

        int ReadIndex(int idx)
        {
            if (!i32IdxBuffer)
            {
                return indexBuffer[idx];
            }
            else
            {
                return (int)indexBuffer32[idx];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float3 GetVtx(int index)
        {
            int offset = index * vertex4ByteStride;
            return new float3(
                asfloat(vertexBuffer[offset]),
                asfloat(vertexBuffer[offset + 1]),
                asfloat(vertexBuffer[offset + 2])
            );
        }
    }

 

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, CompileSynchronously = true)]
    struct CalculateVertexBufferSizeJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<int> vtxFaceFlags;
        [WriteOnly]
        public NativeArray<TriplanarCount> outSize;

        public int chunkSize;

        public void Execute(int i)
        {
            int startIndex = chunkSize * i;
            int endIndex = min(startIndex + chunkSize, vtxFaceFlags.Length);
            TriplanarCount count = new TriplanarCount();
            for (int vIdx = startIndex; vIdx < endIndex; vIdx++)
            {
                count.xp += (vtxFaceFlags[vIdx] & 1         ) > 0 ? 1 : 0;
                count.xn += (vtxFaceFlags[vIdx] & (1 << 1)  ) > 0 ? 1 : 0;
                count.yp += (vtxFaceFlags[vIdx] & (1 << 2)  ) > 0 ? 1 : 0;
                count.yn += (vtxFaceFlags[vIdx] & (1 << 3)  ) > 0 ? 1 : 0;
                count.zp += (vtxFaceFlags[vIdx] & (1 << 4)  ) > 0 ? 1 : 0;
                count.zn += (vtxFaceFlags[vIdx] & (1 << 5)  ) > 0 ? 1 : 0;
            }
            outSize[i] = count;
        }
    }

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, CompileSynchronously = true)]
    static void ReorderVertexBuffer(
        ref NativeArray<uint>   oldVtxBuffer, 
        ref NativeArray<uint>   newVtxBuffer, 
        ref NativeArray<int>    vtxFaceBitmask, 
        ref NativeArray<byte>   triFaceDir,
        ref NativeArray<ushort> oldToNewVtxMap,
        ref NativeSlice<uint>   oldToNewVtxMap32,
        ref NativeArray<ushort> oldIdxBuffer,
        ref NativeSlice<uint>   oldIdxBuffer32,
        ref NativeArray<ushort> newIdxBuffer,
        ref NativeSlice<uint>   newIdxBuffer32,
        float4x4 projectionMatrix,
        int vtx4ByteStride,
        int uv04ByteOffset,
        bool uv0f16,
        bool oldI32Idx,
        bool newI32Idx
        )
    {
        
        int vertexCount = oldVtxBuffer.Length / vtx4ByteStride;
        int indexCount = oldI32Idx ? oldIdxBuffer32.Length : oldIdxBuffer.Length;
        int pointer = 0;
        for (int dir = 5; dir >=0; dir--)
        {
            
            int absDir = dir / 2;
            float2 uFlip = float2((dir & 1) == 1 ? -1 : 1, 1.0f);
            if (dir >= 4) uFlip = float2((dir & 1) == 0 ? -1 : 1, 1.0f);
            float2x4 uvMatrix = default;
            switch (absDir)
            {
                case 0: uvMatrix = new float2x4(uFlip * projectionMatrix.c0.zy, uFlip * projectionMatrix.c1.zy, uFlip * projectionMatrix.c2.zy, projectionMatrix.c3.zy); break; 
                case 1: uvMatrix = new float2x4(uFlip * projectionMatrix.c0.xz, uFlip * projectionMatrix.c1.xz, uFlip * projectionMatrix.c2.xz, projectionMatrix.c3.xz); break; 
                case 2: uvMatrix = new float2x4(uFlip * projectionMatrix.c0.xy, uFlip * projectionMatrix.c1.xy, uFlip * projectionMatrix.c2.xy, projectionMatrix.c3.xy); break; 
            }
            int dirBitmask = 1 << dir;
            for (int vIdx = 0; vIdx < vertexCount; vIdx++)
            {
                if ((vtxFaceBitmask[vIdx] & dirBitmask) == 0)
                {
                    oldToNewVtxMap[vIdx] = 0xFFFF;
                    continue;
                }

                int oldIdx = vIdx * vtx4ByteStride;
                int newIdx = pointer * vtx4ByteStride;
                NativeArray<uint>.Copy(oldVtxBuffer, oldIdx, newVtxBuffer, newIdx, vtx4ByteStride);
                float4 pos = float4(Load3Floats(ref newVtxBuffer, newIdx), 1.0f);
                float2 uv0 = mul(uvMatrix, pos);
                if (uv0f16)
                {
                    newVtxBuffer[newIdx + uv04ByteOffset] = f32tof16(uv0.x) | (f32tof16(uv0.y) << 16);
                }
                else
                {
                    Store2Floats(ref newVtxBuffer, newIdx + uv04ByteOffset, uv0);
                }

                if (!newI32Idx)
                {
                    oldToNewVtxMap[vIdx] = (ushort) pointer;
                }
                else
                {
                    oldToNewVtxMap32[vIdx] = (uint)pointer;
                }

                pointer += 1;
            }

            int dirMask = (1 << dir);
            if (!newI32Idx)
            {
                for (int iIdx = 0; iIdx < indexCount; iIdx += 3)
                {
                    int triDir = (int)triFaceDir[iIdx / 3];
                    if (triDir != dirMask) continue;

                    newIdxBuffer[iIdx]     = oldToNewVtxMap[oldI32Idx ? (int)oldIdxBuffer32[iIdx]     : oldIdxBuffer[iIdx]    ];
                    newIdxBuffer[iIdx + 1] = oldToNewVtxMap[oldI32Idx ? (int)oldIdxBuffer32[iIdx + 1] : oldIdxBuffer[iIdx + 1]];
                    newIdxBuffer[iIdx + 2] = oldToNewVtxMap[oldI32Idx ? (int)oldIdxBuffer32[iIdx + 2] : oldIdxBuffer[iIdx + 2]];
                }
            }
            else
            {
                for (int iIdx = 0; iIdx < indexCount; iIdx += 3)
                {
                    int triDir = (int)triFaceDir[iIdx / 3];
                    if (triDir != dirMask) continue;
                    newIdxBuffer32[iIdx]     = oldToNewVtxMap32[oldI32Idx ? (int)oldIdxBuffer32[iIdx]     : oldIdxBuffer[iIdx]    ];
                    newIdxBuffer32[iIdx + 1] = oldToNewVtxMap32[oldI32Idx ? (int)oldIdxBuffer32[iIdx + 1] : oldIdxBuffer[iIdx + 1]];
                    newIdxBuffer32[iIdx + 2] = oldToNewVtxMap32[oldI32Idx ? (int)oldIdxBuffer32[iIdx + 2] : oldIdxBuffer[iIdx + 2]];
                }
            }
        }
    }

}
