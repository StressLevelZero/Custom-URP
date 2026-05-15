using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace SLZ.SLZEditorTools.MeshVariant
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public partial class TriplanarUvGenerator : MonoBehaviour
    {
        public LazyLoadReference<Mesh> originalMesh;
        public Mesh generatedMesh;
        public Transform projectionSpace;
        public float3 projectionScale = new float3(1,1,1);
        public ProjectionMethod projectionMethod = ProjectionMethod.Triplanar;
    }
}
