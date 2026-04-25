using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;

namespace SLZ.SLZEditorTools
{
    [RequireComponent(typeof(MeshRenderer))]
    public class LightBakeTintedTransparency : MonoBehaviour
    {
        [Serializable]
        public struct TransparentMaterialSlot
        {
            public int materialIndex;
            public float2 scale, offset;
            public Texture2D transmissionColor;
        }
#if UNITY_EDITOR
        public TransparentMaterialSlot[] transparentMaterials;
        [HideInInspector] public Material[] originalMaterials;
#endif
    }
}
