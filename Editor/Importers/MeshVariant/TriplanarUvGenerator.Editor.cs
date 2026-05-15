#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;


using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Jobs;
using Unity.Burst;
using Object = UnityEngine.Object;
using UnityEditor.IMGUI.Controls;
using UnityEditor.EditorTools;
using System.Reflection;


namespace SLZ.SLZEditorTools.MeshVariant
{

    [CustomEditor(typeof(TriplanarUvGenerator))]
    [CanEditMultipleObjects]
    public class TriplanarUvGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            Rect r = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight * 1.15f);
            if (GUI.Button(r, "Update Mesh UVs"))
            {
                foreach (Object tgt in targets)
                {
                    if (tgt is TriplanarUvGenerator tpm)
                    {
                        tpm.InitGeneratedMesh();
                    }
                }
            }
        }
    }
   
    public partial class TriplanarUvGenerator : MonoBehaviour
    {

        [MenuItem("CONTEXT/TriplanarUvGenerator/Update Projection")]
        public static void ContextMenuCapture(MenuCommand command)
        {
            TriplanarUvGenerator tgt = (TriplanarUvGenerator)command.context;
            tgt.InitGeneratedMesh();
            //EditorApplication.update += tgt.FuckUnity;
        }


        private MeshProjectedUvVariant projUVImporter;
        private string savePath;

        [Serializable]
        public struct ObjectInfo
        {
            public string scenePath;
            public string goPath;
            public string goGlobalIdStr;
            public ulong goPrefabId;
            public ulong goObjectId;
            public string meshGUID;
            public long  meshLocalId;

            public GlobalObjectId GameObjectGlobalID
            {
                get 
                { 
                    bool success = GlobalObjectId.TryParse(goGlobalIdStr, out GlobalObjectId id); 
                    if (!success)
                    {
                        Debug.LogError("Failed to parse string serialized global object ID");
                    }
                    return id; 
                }
            }
        }

        [Serializable]
        public struct JsonSceneObjectIdentifier
        {
            public ulong TargetObject;
            public ulong TargetPrefab;
        }

        [Serializable]
        public struct JsonGlobalObjectId
        {
            public JsonSceneObjectIdentifier m_SceneObjectIdentifier;
            public GUID m_AssetGUID;
            public int  m_IdentifierType;
        }

        GlobalObjectId m_GlobalGOId;
        GlobalObjectId GlobalGOId
        {
            get
            {
                if (m_GlobalGOId.assetGUID == default)
                {
                    m_GlobalGOId = GlobalObjectId.GetGlobalObjectIdSlow(this.gameObject);
                }
                return m_GlobalGOId;
            }
            set
            {
                m_GlobalGOId = value;
            }
        }

        // Only check the contents of the generated mesh file once to make sure it doesn't belong to another object 
        bool checkedIfMeshBelongsToThisObj = false;

        public void InitGeneratedMesh()
        {
            //if (this.generatedMesh != null) return;

            int instanceID = this.gameObject.GetInstanceID();
 

            if (!originalMesh.isSet)
            {
                originalMesh = this.GetComponent<MeshFilter>().sharedMesh;
            }

            bool needsNewMeshAsset = !generatedMesh;
            if (generatedMesh)
            {
                //string json = File.ReadAllText(AssetDatabase.GetAssetPath(generatedMesh));
                //ObjectInfo info = JsonUtility.FromJson<ObjectInfo>(json);
                //if (!string.IsNullOrEmpty(info.goGlobalIdStr))
                //{
                //    if (GlobalGOId.targetPrefabId != info.goPrefabId || GlobalGOId.targetObjectId != info.goObjectId)
                //    {
                //        needsNewMeshAsset = true;
                //    }
                //}
                Object[] stuff = null;
                string path = AssetDatabase.GetAssetPath(generatedMesh);
                try
                {
                    stuff = InternalEditorUtility.LoadSerializedFileAndForget(path);
                }
                catch
                {

                }
                MeshVariantReference mvr = stuff != null && stuff.Length > 0 ? stuff[0] as MeshVariantReference : null;
                GlobalObjectId mvrGO = (GlobalObjectId) mvr.gameObjectId;
                if (mvr == null || mvrGO.assetGUID != GlobalGOId.assetGUID ||
                    mvrGO.targetObjectId != GlobalGOId.targetObjectId ||
                    mvrGO.targetPrefabId != GlobalGOId.targetPrefabId
                )
                {
                    needsNewMeshAsset = true;
                    generatedMesh = null;
                    projUVImporter = null;
                }
                checkedIfMeshBelongsToThisObj = true;
            }
            if (needsNewMeshAsset)
            {
                string saveFolder = "";
                string sceneOrPrefabPath = "";

                MeshVariantReference mvr = ScriptableObject.CreateInstance<MeshVariantReference>();

                if (PrefabUtility.IsPartOfPrefabAsset(this.gameObject))
                {
                    string prefabPath = AssetDatabase.GetAssetOrScenePath(this.gameObject);
                    string absoluteSaveFolder = EditorUtility.SaveFolderPanel("Select folder to save generated mesh", Path.GetDirectoryName(prefabPath), "");
                    if (string.IsNullOrEmpty(absoluteSaveFolder) || !Directory.Exists(absoluteSaveFolder))
                    {
                        Debug.LogError($"Save path is invalid: {absoluteSaveFolder}");
                        return;
                    }
                    string relativeSaveFolder = Path.GetRelativePath(Path.GetDirectoryName(Application.dataPath), absoluteSaveFolder);
                    if (!AssetDatabase.IsValidFolder(relativeSaveFolder))
                    {
                        AssetDatabase.Refresh();
                    }
                    saveFolder = relativeSaveFolder;
                    mvr.sceneOrPrefabPath = prefabPath;
                    mvr.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                }
                else
                {
                    Scene scene = GameObject.GetScene(instanceID);
                    string scenePath = scene.path;
                    string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                    string sceneParentFolder = Path.GetDirectoryName(scenePath);
                    string sceneFolder = Path.Combine(sceneParentFolder, sceneName);
                    if (!AssetDatabase.IsValidFolder(sceneFolder)) AssetDatabase.CreateFolder(sceneParentFolder, sceneName);
                    string sceneAssetsPath = Path.Combine(sceneFolder, "SceneAssets");
                    if (!AssetDatabase.IsValidFolder(sceneAssetsPath)) AssetDatabase.CreateFolder(sceneFolder, "SceneAssets");
                    saveFolder = sceneAssetsPath;
                    //sceneOrPrefabPath = scenePath;
                    mvr.sceneOrPrefabPath = scenePath;
                    mvr.scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                }
                GlobalObjectId meshID = GlobalObjectId.GetGlobalObjectIdSlow(this.gameObject);
                GlobalGOId = meshID;

                uint idVal = hash(double2(
                    asdouble(unchecked((long)meshID.targetObjectId)),
                    asdouble(unchecked((long)meshID.targetPrefabId))
                ));
                string meshName = $"{idVal:X8}-{this.gameObject.name}";
                savePath = Path.Combine(saveFolder, $"{meshName}.projUV");

                /*
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(originalMesh.asset, out string originalMeshGuid, out long originalMeshLocalID);
                ObjectInfo info = new()
                {
                    scenePath     = sceneOrPrefabPath,
                    goPath        = AnimationUtility.CalculateTransformPath(this.transform,null),
                    goGlobalIdStr = meshID.ToString(),
                    goPrefabId    = meshID.targetPrefabId,
                    goObjectId    = meshID.targetObjectId,
                    meshGUID      = originalMeshGuid,
                    meshLocalId   = originalMeshLocalID
                };

                string json = JsonUtility.ToJson(info, true);
                File.WriteAllText(savePath, json);
                */
                

                //string savePath2 = Path.Combine(sceneAssetsPath, $"{meshName}.asset");
                //string savePath2Meta = savePath2 + ".meta";
                //TextAsset dummy = new TextAsset();
                //InternalEditorUtility.SaveToSerializedFileAndForget(new Object[] {dummy}, savePath, true);
                mvr.gameObjectId = meshID;
                mvr.originalMesh = this.originalMesh;
                mvr.hierarchyPath = AnimationUtility.CalculateTransformPath(this.transform,null);
                InternalEditorUtility.SaveToSerializedFileAndForget(new Object[] {mvr}, savePath, true);

                AssetDatabase.ImportAsset(savePath, 
                    ImportAssetOptions.ForceUpdate | 
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.DontDownloadFromCacheServer
                    );

                counter = 0;
                projUVImporter = null;
                checkedIfMeshBelongsToThisObj = true;
            }


            GetImporter();
            AssignMesh();
            UpdateProjection();
            projUVImporter.SaveAndReimport();
            
        }
        private int counter = 0;
        void FuckUnity()
        {
            counter++;
            if (counter == 1)
            {
                AssetDatabase.ImportAsset(savePath, 
                    ImportAssetOptions.ForceUpdate | 
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.DontDownloadFromCacheServer
                    );
            }      
            else if (counter == 20)
            {
               
            }
            else if (counter > 20)
            {
                EditorApplication.update -= FuckUnity;
            }
        }

        void AssignMesh()
        {
            MeshFilter mf = this.GetComponent<MeshFilter>();
            bool parentMeshIsNotSet = !projUVImporter.parentMesh.isSet;
            bool originalMeshChanged = originalMesh.asset != projUVImporter.parentMesh.asset;
            if (parentMeshIsNotSet || originalMeshChanged)
            {
                if (originalMeshChanged)
                {
                    Object[] stuff = InternalEditorUtility.LoadSerializedFileAndForget(savePath);
                    if (stuff != null && stuff.Length > 0 && stuff[0] is MeshVariantReference mvr)
                    {
                        mvr.originalMesh = originalMesh;
                        stuff[0] = mvr;
                        InternalEditorUtility.SaveToSerializedFileAndForget(stuff,savePath,true);
                    }
                }
                projUVImporter.parentMesh = originalMesh;
                EditorUtility.SetDirty(projUVImporter);
            }
            if (this.generatedMesh == null)
            {
                this.generatedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(savePath);
                mf.sharedMesh = this.generatedMesh;
                EditorUtility.SetDirty(mf);
                EditorUtility.SetDirty(this);
            }
        }

        void GetImporter()
        {
            //if (projUVImporter == null)
            {
                if (string.IsNullOrEmpty(savePath) && generatedMesh != null)
                {
                    savePath = AssetDatabase.GetAssetPath(generatedMesh);
                }
                if (string.IsNullOrEmpty(savePath))
                {
                    Debug.LogError("savePath is null");
                }
                projUVImporter = (MeshProjectedUvVariant) MeshProjectedUvVariant.GetAtPath(savePath);
                if (projUVImporter == null)
                {
                    Debug.LogError("projUVImporter is null at path " + savePath);
                }
            }
        }

        void UpdateProjection()
        {
            //Debug.Log($"Updating projection");
            float4x4 projectionMatrix = this.transform.localToWorldMatrix;
            if (projectionSpace != null)
            {
                projectionMatrix = mul(projectionSpace.worldToLocalMatrix, projectionMatrix);
            }
            projectionMatrix = mul(projectionMatrix, new float4x4(
                projectionScale.x, 0.0f,              0.0f,              0.0f,
                0.0f,              projectionScale.y, 0.0f,              0.0f,
                0.0f,              0.0f,              projectionScale.z, 0.0f,
                0.0f,              0.0f,              0.0f,              1.0f
            ));

            if (any(projUVImporter.projectionSpace.c0 != projectionMatrix.c0) || 
                any(projUVImporter.projectionSpace.c1 != projectionMatrix.c1) ||
                any(projUVImporter.projectionSpace.c2 != projectionMatrix.c2) ||
                any(projUVImporter.projectionSpace.c3 != projectionMatrix.c3) ||
                projUVImporter.projectionMethod != projectionMethod
            )
            {
                //Debug.Log($"Setting projectionSpace to {projectionMatrix}");
                projUVImporter.projectionSpace = projectionMatrix;
                projUVImporter.projectionMethod = projectionMethod;
                EditorUtility.SetDirty(projUVImporter);
            }
        }
    }
}
#endif // UNITY_EDITOR