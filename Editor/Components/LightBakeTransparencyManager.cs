using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Mathematics;

namespace SLZ.SLZEditorTools
{
    public static class LightBakeTransparencyManager
    {
        static List<GameObject> TransparentBakeObjects = new List<GameObject>();
        static List<Material> TransparentBakeMats = new List<Material>();
        static List<MeshRenderer> disabledRenderers = new List<MeshRenderer>();

        [InitializeOnLoadMethod]
        static void RegisterBakeEvents()
        {
            Lightmapping.bakeStarted += CreateTransparentLMObjs;
            Lightmapping.bakeCompleted += DestroyTransparentBakeObjects;
        }

        static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");
        static readonly int ID_TransparencyLM = Shader.PropertyToID("_TransparencyLM");

        static void CreateTransparentLMObjs()
        {
            Shader transparentLMShader = Shader.Find("SLZ/Transmissive Lightmap");

            LightBakeTintedTransparency[] tints = GameObject.FindObjectsOfType<LightBakeTintedTransparency>(false);
            if (TransparentBakeObjects.Capacity < tints.Length)
            {
                TransparentBakeObjects.Capacity = tints.Length;
            }

            if (disabledRenderers.Capacity < tints.Length)
            {
                disabledRenderers.Capacity = tints.Length;
            }

            if (TransparentBakeMats.Capacity < 2 * tints.Length)
            {
                TransparentBakeMats.Capacity = 2 * tints.Length;
            }

            HideFlags dontsaveWithoutMemLeak = HideFlags.DontSaveInBuild;
            //System.Type[] componentTypes = new System.Type[] {typeof(MeshFilter), typeof(MeshRenderer)};
            foreach (var tint in tints)
            {
                
                GameObject original = tint.gameObject;
                MeshRenderer originalMr = original.GetComponent<MeshRenderer>();

                if (!original.activeInHierarchy || !tint.isActiveAndEnabled || !originalMr.enabled)
                {
                    continue;
                }

                GameObject clone = new GameObject(tint.gameObject.name + " EDITOR_ONLY_TP_BAKE");

                TransparentBakeObjects.Add(clone);

                //clone.hideFlags = dontsaveWithoutMemLeak;
                GameObjectUtility.SetStaticEditorFlags(clone, GameObjectUtility.GetStaticEditorFlags(original));

                Transform originalT = original.transform;
                Transform cloneT = clone.transform; 
                cloneT.parent = originalT.parent;
                cloneT.localPosition = originalT.localPosition;
                cloneT.localRotation = originalT.localRotation;
                cloneT.localScale = originalT.localScale;


                MeshFilter originalMf = original.GetComponent<MeshFilter>();
                UnityEditorInternal.ComponentUtility.CopyComponent(originalMf);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(clone);
                MeshFilter cloneMf = clone.GetComponent<MeshFilter>();
                cloneMf.hideFlags = dontsaveWithoutMemLeak;

                UnityEditorInternal.ComponentUtility.CopyComponent(originalMr);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(clone);
                MeshRenderer cloneMr = clone.GetComponent<MeshRenderer>();
                cloneMr.hideFlags = dontsaveWithoutMemLeak;

                Material[] cloneMaterials = cloneMr.sharedMaterials;

                foreach (var slot in tint.transparentMaterials)
                {
                    if (slot.transmissionColor == null)
                        continue;
                    if (slot.materialIndex < 0 || slot.materialIndex >= cloneMaterials.Length)
                        continue;

                    Material mat = new Material(transparentLMShader);
                    TransparentBakeMats.Add(mat);

                    mat.hideFlags = dontsaveWithoutMemLeak;
                    mat.SetTexture(ID_TransparencyLM, slot.transmissionColor);

                    // set scale to 1,1 if it's 0,0
                    mat.SetTextureScale(ID_MainTex, slot.scale.x == 0 && slot.scale.y == 0 ? new float2(1,1) : slot.scale);
                    mat.SetTextureOffset(ID_MainTex, slot.offset);
                    cloneMaterials[slot.materialIndex] = mat;
                }

                cloneMr.sharedMaterials = cloneMaterials;

                originalMr.enabled = false;
                disabledRenderers.Add(originalMr);
            }
        }

        static void DestroyTransparentBakeObjects()
        {
            int numDisabled = disabledRenderers.Count;
            for (int dIdx = 0; dIdx < numDisabled; dIdx++)
            {
                if (disabledRenderers[dIdx])
                {
                    disabledRenderers[dIdx].enabled = true;
                }
            }
            disabledRenderers.Clear();

            int numObjs = TransparentBakeObjects.Count;
            for (int oIdx = 0; oIdx < numObjs; oIdx++)
            {
                if (TransparentBakeObjects[oIdx])
                {
                    Object.DestroyImmediate(TransparentBakeObjects[oIdx]);
                }
            }
            TransparentBakeObjects.Clear();

            int numMats = TransparentBakeMats.Count;
            for (int mIdx = 0; mIdx < numMats; mIdx++)
            {
                if (TransparentBakeMats[mIdx])
                {
                    Object.DestroyImmediate(TransparentBakeMats[mIdx]);
                }
            }
            TransparentBakeMats.Clear();
        }
    }
}
