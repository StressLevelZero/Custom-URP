#if !UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS
#error MeshProjectedUvVariant needs UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS set in Project Settings->Player->Scripting Define Symbols!
#endif
using System.Collections;
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

[ScriptedImporter(version: 3, ext: "projUV", AllowCaching = true)]
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

    static GUID defaultResourceGUID = new GUID("0x0000000000000000e000000000000000");

    public LazyLoadReference<Mesh> parentMesh;
    
    public float4x4 projectionSpace = Unity.Mathematics.float4x4.identity;

    ProjectionMethod projectionMethod = ProjectionMethod.Flat;

    public override void OnImportAsset(AssetImportContext ctx)
    {
        if (!parentMesh.isSet)
        {
            return;
        }
        

        if (GetGUIDAndLocalIdentifierInFile(parentMesh.instanceID, out GUID guid, out long localID))
        {
            if (guid != defaultResourceGUID)
            {
                ctx.DependsOnArtifact(guid);
            }
        }

        Mesh newMesh = new Mesh();
        //EditorUtility.CopySerialized(parentMesh.asset, newMesh);

        ProjectMono(parentMesh.asset, newMesh);

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

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, CompileSynchronously = true)]
    struct TriMarkFaceOrientationJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        NativeList<int> vtxFaceFlags;
        [WriteOnly]
        NativeArray<byte> triFaceDir;

        [ReadOnly]
        NativeArray<ushort> indexBuffer;
        [ReadOnly]
        NativeArray<float> vertexBuffer;

        int vertexStride;

        public float3x3 projectionSpace;

        public void Execute(int i)
        {
            int triIndex = 3 * i;
            int index0 = indexBuffer[triIndex];
            int index1 = indexBuffer[triIndex + 1];
            int index2 = indexBuffer[triIndex + 2];

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
            Unity.Burst.Intrinsics.Common.InterlockedAnd(ref vtxFaceFlags.ElementAt(index0), bitmask);
            Unity.Burst.Intrinsics.Common.InterlockedAnd(ref vtxFaceFlags.ElementAt(index1), bitmask);
            Unity.Burst.Intrinsics.Common.InterlockedAnd(ref vtxFaceFlags.ElementAt(index2), bitmask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float3 GetVtx(int index)
        {
            int offset = index * vertexStride;
            return new float3(
                vertexBuffer[offset],
                vertexBuffer[offset + 1],
                vertexBuffer[offset + 2]
            );
        }
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
    }

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, CompileSynchronously = true)]
    struct CalculateVertexBufferSizeJob : IJobParallelFor
    {
        [ReadOnly]
        NativeArray<int> vtxFaceFlags;
        [WriteOnly]
        NativeArray<TriplanarCount> outSize;

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
}
