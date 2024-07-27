using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using static UnityEditorInternal.ReorderableList;

namespace SLZ.SLZEditorTools
{
    internal static class ReplaceBuiltinSky
    {
        static GUID ourSkyGUID = new GUID("b4f1ecee849c7f547a702a0ee76b4e49");
        static Material defaultSky;

        [InitializeOnLoadMethod]
        public static void Init()
        {
            defaultSky = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(ourSkyGUID));
            if (defaultSky != null)
            {
                EditorSceneManager.sceneOpened -= SceneOpenedCallback;
                EditorSceneManager.sceneOpened += SceneOpenedCallback;
            }
            else
            {
                Debug.LogError($"SLZ Builtin Sky Replacer: Failed to find default sky material from GUID ({ourSkyGUID.ToString()}). Either the material or its meta file may have been deleted. Check for GUID conflicts.");
            }
        }

        static void SceneOpenedCallback(Scene scene, OpenSceneMode mode)
        {
            if (mode == OpenSceneMode.Single)
            {
                Material skyMat = RenderSettings.skybox;
                if (skyMat == null) return;

                string guid;
                long localID;
                bool success = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(skyMat, out guid, out localID);
                if (success && guid == "0000000000000000f000000000000000")
                {
                    RenderSettings.skybox = defaultSky;
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }
        }
    }
}
