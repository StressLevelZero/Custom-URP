using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Unity.Mathematics;

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
    
    public Vector3 Scale = new Vector3(10,7,10);

    [Tooltip("Target shader to use")] public ComputeShader computeShader; 
   [HideInInspector,SerializeField] public Vector3 NormalizedScale;
    [HideInInspector,SerializeField] public Vector3 Corner;

    [Tooltip("How much of the volume is falloff from edge toward center. 0 = hard edge, 1 = all falloff.")]
    [Range(0.01f,1)] public float falloffDistance = 1f;
    
    // Optional one-time migration from your old field.
    [FormerlySerializedAs("ViewDistance")]
    #pragma warning disable 0414
    [SerializeField, HideInInspector] private float _legacyViewDistance = 1f;
    #pragma warning restore 0414
    [SerializeField, HideInInspector] private bool _densityMigrated;
    // [Tooltip("Density of the volume. How far you can see through it. The lower the number, the denser it is")]
    // [Range(0.01f,100)] public float ViewDistance = 1f;
    
    [Tooltip("Extinction / density. Higher = denser.")]
    [Min(0f)] public float density = 1f;
        
    // public float LocalExtinction()
    // {
    //     return VolumeRenderingUtils.ExtinctionFromMeanFreePath(ViewDistance);
    // }
    public float LocalExtinction() => density;


    private void OnEnable()
    {
#if UNITY_EDITOR
        computeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.unity.render-pipelines.universal/Shaders/Volumetrics/VolumetricDensityClipmap.compute");
#endif
        RefreshCachedData();
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
    
    public void RefreshCachedData()
    {
        Vector3 tempScale = Vector3.Scale(transform.localScale, Scale);
        if (NormalizedScale != tempScale)
            NormalizedScale = tempScale;

        Corner = transform.position - (NormalizedScale * 0.5f);
        
        Matrix4x4 boxmatx = transform.localToWorldMatrix;
        boxmatx = Matrix4x4.TRS(transform.position, Quaternion.identity, transform.lossyScale);

    }
    // private void OnDrawGizmosSelected()
    // {
    //     switch (shapeType)
    //     {
    //         case ShapeType.Sphere:
    //             Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position, Quaternion.identity, Vector3.Scale( Scale * 0.5f , gameObject.transform.lossyScale) );
    //
    //             float outterScalef = 1.0f;
    //             float innerScalef = 1 - falloffDistance;
    //         
    //             //Outer    
    //             Gizmos.color = new Color(0.4f, 0.4f, 0.4f, .1f);
    //             Gizmos.DrawWireSphere(Vector3.zero, outterScalef);
    //         
    //             //Mid
    //             Gizmos.color = new Color(0.45f, 0.45f, 0.45f, .25f);
    //             Gizmos.DrawWireSphere(Vector3.zero, Mathf.Lerp(innerScalef,outterScalef, 0.5f));
    //         
    //             //Inner
    //             Gizmos.color = new Color(0.45f, 0.45f, 0.45f, .35f);;
    //             Gizmos.DrawWireSphere(Vector3.zero, innerScalef );
    //             break;
    //         
    //         case ShapeType.Box:
    //
    //             // Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position,Quaternion.identity, Vector3.Scale( Scale , gameObject.transform.lossyScale) );
    //             //
    //             // Vector3 outterScale = Vector3.one;
    //             // Vector3 innerScale = Vector3.one * (1 - falloffDistance);
    //             //
    //             // //Outer    
    //             // Gizmos.color = new Color(0.4f, 0.4f, 0.4f, .1f);
    //             // Gizmos.DrawWireCube(Vector3.zero, outterScale);
    //             //
    //             // //Mid
    //             // Gizmos.color = new Color(0.45f, 0.45f, 0.45f, .5f);
    //             // Gizmos.DrawWireCube(Vector3.zero, Vector3.Lerp(innerScale,outterScale, 0.5f));
    //             //
    //             // //Inner
    //             // Gizmos.color = Color.gray;
    //             // Gizmos.DrawWireCube(Vector3.zero, innerScale );
    //             break;
    //     }
    //
    //
    // }
    
#if UNITY_EDITOR
    
    private Vector3 _lastScale;
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private Vector3 _lastLossyScale;
    private void OnValidate()
    {
        //Check to see if the same and change if different
        Vector3 tempscale = Vector3.Scale(gameObject.transform.localScale, Scale);
        if (NormalizedScale != tempscale)  NormalizedScale = tempscale; //redundant check to prevent dirtying 
        RefreshCachedData();
        VolumetricRegisters.MarkClipmapDirty();
     //   Corner = transform.position - (NormalizedScale * 0.5f);
    }
    
    private void Update()
    {
        if (Application.IsPlaying(gameObject))
            return;

        bool changed =
            transform.hasChanged ||
            _lastScale != Scale ||
            _lastPosition != transform.position ||
            _lastRotation != transform.rotation ||
            _lastLossyScale != transform.lossyScale;

        if (!changed)
            return;

        RecalculateCachedData();
        CacheTransformSnapshot();
        transform.rotation = Quaternion.identity; //Force no rotation! 
        transform.localScale = new Vector3(math.abs(transform.localScale.x), math.abs(transform.localScale.y), math.abs(transform.localScale.z)); //No negative scale
        transform.hasChanged = false;
        VolumetricRegisters.MarkClipmapDirty();
    }
    private void CacheTransformSnapshot()
    {
        _lastScale = Scale;
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
        _lastLossyScale = transform.lossyScale;
    }
    private void RecalculateCachedData()
    {
        // If parent scaling should affect the volume, lossyScale is usually what you want.
        NormalizedScale = Vector3.Scale(transform.lossyScale, Scale);
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
