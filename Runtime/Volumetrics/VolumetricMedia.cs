using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class LocalVolumetricFog : MonoBehaviour
{
    //Simple helper script for baker
    public enum ShapeType { Sphere, Box };
    public enum ModeType { Froxel, Clipmap };

    public ShapeType shapeType;
    [HideInInspector] // Froxel Not ready yet, just using Clipmap
    [Tooltip("Clipmap is fast. Froxel is full resolution and realtime")]
    public ModeType modeType;
    [Tooltip("3d texture. RGB is color")]
    public Texture3D volumeTexture;
    
    public Vector3 Scale = Vector3.one;

    [Tooltip("Target shader to use")] public ComputeShader computeShader; 
   
    
    [HideInInspector,SerializeField] public Vector3 NormalizedScale;
    [HideInInspector,SerializeField] public Vector3 Corner;

    [Tooltip("How much falloff is there from the edge to the center")]  
    [Range(0.01f,1)] public float falloffDistance = 1f;
    
    [Tooltip("Density of the volume. How far you can see through it. The lower the number, the denser it is")]
    [Range(0.01f,100)] public float ViewDistance = 1f;

        
    public float LocalExtinction()
    {
        return VolumeRenderingUtils.ExtinctionFromMeanFreePath(ViewDistance);
    }
 


    private void OnEnable()
    {
#if UNITY_EDITOR
        computeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/VoulmetricDensityClipmap.compute");
#endif
        VolumetricRegisters.RegisterParticipatingMedia(this);
    }

    private void OnDisable()
    {
        VolumetricRegisters.UnregisterParticipatingMedia(this);
    }

    private void OnDestroy()
    {
        VolumetricRegisters.UnregisterParticipatingMedia(this);
    }

    private void OnDrawGizmosSelected()
    {
        switch (shapeType)
        {
            case ShapeType.Sphere:
                Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position, Quaternion.identity, Vector3.Scale( Scale * 0.5f , gameObject.transform.lossyScale) );

                float outterScalef = 1.0f;
                float innerScalef = 1 - falloffDistance;
            
                //Outer    
                Gizmos.color = new Color(0.4f, 0.4f, 0.4f, .1f);
                Gizmos.DrawWireSphere(Vector3.zero, outterScalef);
            
                //Mid
                Gizmos.color = new Color(0.45f, 0.45f, 0.45f, .5f);
                Gizmos.DrawWireSphere(Vector3.zero, Mathf.Lerp(innerScalef,outterScalef, 0.5f));
            
                //Inner
                Gizmos.color = Color.gray;
                Gizmos.DrawWireSphere(Vector3.zero, innerScalef );
                break;
            
            case ShapeType.Box:

                Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position,Quaternion.identity, Vector3.Scale( Scale , gameObject.transform.lossyScale) );

                Vector3 outterScale = Vector3.one;
                Vector3 innerScale = Vector3.one * (1 - falloffDistance);
                
                //Outer    
                Gizmos.color = new Color(0.4f, 0.4f, 0.4f, .1f);
                Gizmos.DrawWireCube(Vector3.zero, outterScale);
                
                //Mid
                Gizmos.color = new Color(0.45f, 0.45f, 0.45f, .5f);
                Gizmos.DrawWireCube(Vector3.zero, Vector3.Lerp(innerScale,outterScale, 0.5f));
                
                //Inner
                Gizmos.color = Color.gray;
                Gizmos.DrawWireCube(Vector3.zero, innerScale );
                break;
        }


    }
    
#if UNITY_EDITOR
    private void OnValidate()
    {
        //Check to see if the same and change if different
        Vector3 tempscale = Vector3.Scale(gameObject.transform.localScale, Scale);
        if (NormalizedScale != tempscale)  NormalizedScale = tempscale; //redundant check to prevent dirtying 
        
        Corner = transform.position - (NormalizedScale * 0.5f);
    }
    

    [MenuItem("GameObject/Light/Local Volumetric Fog", false, 10)]
    static void CreateBakedVolumetricArea(MenuCommand menuCommand)
    {
        // Create a custom game object
        GameObject go = new GameObject("Local Volumetric Fog");
        // Ensure it gets reparented if this was a context click (otherwise does nothing)
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        // Register the creation in the undo system
        go.AddComponent<LocalVolumetricFog>();
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }
#endif

}
