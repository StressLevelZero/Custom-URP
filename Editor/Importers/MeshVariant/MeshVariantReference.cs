using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;



#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
#endif

namespace SLZ.SLZEditorTools.MeshVariant
{
    public class MeshVariantReference : ScriptableObject
    {
        #if UNITY_EDITOR
        public SceneAsset scene;
        public GameObject prefab;
        public string sceneOrPrefabPath;
        public SerialGlobalObjectId gameObjectId;
        public string hierarchyPath;
        public LazyLoadReference<Mesh> originalMesh;

        [Serializable]
        public struct SerialSceneObjectId
        {
            public ulong TargetObject;
            public ulong TargetPrefab;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct SerialGUID
        {
            public UInt32 m_Value0;
	        public UInt32 m_Value1;
	        public UInt32 m_Value2;
	        public UInt32 m_Value3;
        }

        [Serializable]
        public struct SerialGlobalObjectId
        {
            public SerialSceneObjectId m_SceneObjectIdentifier;
            public SerialGUID m_AssetGUID;
            public int  m_IdentifierType;

            public static SerialGlobalObjectId FromGlobalId(GlobalObjectId id)
            {
                return UnsafeUtility.As<GlobalObjectId, SerialGlobalObjectId>(ref id);
            }

            public static implicit operator SerialGlobalObjectId(GlobalObjectId id)
            {
                return FromGlobalId(id);
            }

            public static GlobalObjectId ToGlobalId(SerialGlobalObjectId id)
            {
                return UnsafeUtility.As<SerialGlobalObjectId, GlobalObjectId>(ref id);
            }

            public static implicit operator GlobalObjectId(SerialGlobalObjectId id)
            {
                return ToGlobalId(id);
            }

            public override string ToString()
	        {
	        	return $"GlobalObjectId_V1-{m_IdentifierType}-{m_AssetGUID}-{m_SceneObjectIdentifier.TargetObject}-{m_SceneObjectIdentifier.TargetPrefab}";
	        }
        }

        #endif
    }
}