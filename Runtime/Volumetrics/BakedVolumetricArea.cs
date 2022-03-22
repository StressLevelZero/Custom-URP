using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class BakedVolumetricArea : MonoBehaviour
{
    [SerializeField, Tooltip("Texel density per meter. Controls the resolution of the baked texture.")] float TexelDensity = 5;
  //  [SerializeField, Tooltip("Texel density ratio scaler. Multiples Texel density per dimension")] Vector3 TexelRatio = new Vector3(1,1,1);
    [SerializeField] Vector3 BoxScale = new Vector3(10,5,10);
    [SerializeField] public Texture3D bakedTexture;
    [SerializeField] public Vector3Int NormalizedTexelDensity; //exposed to see target resolution
    [HideInInspector,SerializeField] public Vector3 NormalizedScale;
    [HideInInspector,SerializeField] public Vector3 Corner;
   


    private void OnEnable()
    {
        VolumetricRegisters.RegisterVolumetricArea(this);
    }
    private void OnDisable()
    {
        VolumetricRegisters.UnregisterVolumetricArea(this);
#if UNITY_EDITOR
        DisableDebugMesh();
#endif
    }
    private void OnDestroy()
    {
        VolumetricRegisters.UnregisterVolumetricArea(this);
#if UNITY_EDITOR
        DisableDebugMesh();
#endif
    }

    private void OnValidate()
    {

        //Check to see if the same and change if different

        Vector3 tempscale = Vector3.Scale(gameObject.transform.localScale, BoxScale);
        if (NormalizedScale != tempscale)
            NormalizedScale = tempscale;

        Vector3 UnclampedResolution = Vector3.Scale(NormalizedScale, new Vector3(TexelDensity, TexelDensity, TexelDensity)) ;

        float Maxres = Mathf.Max(UnclampedResolution.x, UnclampedResolution.y, UnclampedResolution.z);

        float rescaler = Mathf.Min(1, 4096 / Maxres);

        //   Debug.Log("Clamped resolution to " + Mathf.RoundToInt(UnclampedResolution.x * rescaler));

        Vector3Int tempTexelDensity = new Vector3Int
        {
            x = Mathf.RoundToInt(UnclampedResolution.x * rescaler),
            y = Mathf.RoundToInt(UnclampedResolution.y * rescaler),
            z = Mathf.RoundToInt(UnclampedResolution.z * rescaler)
        };     

        //if (tempTexelDensity.x > 8096 || tempTexelDensity.y > 8096 || tempTexelDensity.z > 8096)
        //{
        //}

     //   if (NormalizedTexelDensity != tempTexelDensity)
            NormalizedTexelDensity = tempTexelDensity;

        Corner = transform.position - (NormalizedScale * 0.5f);



    }

#if UNITY_EDITOR

    private void Awake()
    {
        if (UnityEditor.EditorApplication.isPlaying) DisableDebugMesh();
    }
    private void OnDrawGizmos()
    {
        //if (!UnityEditor.Selection.Contains(gameObject)) DisableDebugMesh();
        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position, Quaternion.identity, NormalizedScale);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        //Gizmos.DrawWireCube(new Vector3(  
        //    (0.0f /TexelDensity.x) - (TexelDensity.x * 0.5f), 
        //    (0.0f /TexelDensity.y) - (TexelDensity.y * 0.5f), 
        //    (0.0f /TexelDensity.z) - (TexelDensity.x * 0.5f)) , new Vector3(1 / TexelDensity.x, 1 / TexelDensity.y, 1 / TexelDensity.z) );
    }

    private void OnDrawGizmosSelected()
    {
        /*
        if (DEBUG)
        {
            if (UnityEditor.Selection.Contains(gameObject))
            {
                EnableDebugMesh();
            } 
            else 
            {
                DisableDebugMesh();
            }
        }
        else if (DebugCube != null)
        {
            DisableDebugMesh();
        }
        */
            OnValidate();
    //    Gizmos.DrawWireSphere(transform.position - (NormalizedScale* 0.5f), .5f);
        Gizmos.color = new Color(0.5f,0.5f,0.5f,0.25f);
        Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position, Quaternion.identity, NormalizedScale);
        if (!DEBUG) Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
   //     Gizmos.DrawWireSphere(Corner, 0.5f);
     
    }

    [HideInInspector] public bool DEBUG;
    [HideInInspector] public static bool VisStateGlobal;
    [SerializeField,HideInInspector] GameObject DebugCube;
    [SerializeField,HideInInspector] Material mat;
    [HideInInspector,SerializeField] public bool MarkDebugCubeForDelete;
    private bool VisStateLocal;

    public void EnableDebugMesh()
    {
        if (bakedTexture == null || DebugCube != null || UnityEditor.EditorApplication.isPlaying) return;

        MarkDebugCubeForDelete = false;
        DebugCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DebugCube.transform.parent = gameObject.transform;
        DebugCube.transform.localPosition = Vector3.zero;
        DebugCube.transform.localScale = BoxScale;
        DebugCube.transform.rotation = Quaternion.identity;
        DestroyImmediate(DebugCube.GetComponent<Collider>());

        Renderer rnd = DebugCube.GetComponent<Renderer>();
        mat = new Material(Shader.Find("hidden/VolumetricPreview"));

        mat.SetTexture("_Volume", bakedTexture);
        mat.renderQueue = 3000;

        rnd.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        rnd.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        rnd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        rnd.material = mat;
        DebugCube.hideFlags = HideFlags.HideAndDontSave;

    }
    public void DisableDebugMesh()
    {
        if (DebugCube == null) return;
        MarkDebugCubeForDelete = true;
        //DestroyImmediate(DebugCube);
        //DestroyImmediate(mat);
    }

    public void RefreshDebugMesh()
    {
        DisableDebugMesh();
        EnableDebugMesh();
    }
    [ExecuteInEditMode]    
    void LateUpdate()
    {
        if (VisStateLocal != VisStateGlobal)
        {
            VisStateLocal = VisStateGlobal;
            if (VisStateGlobal)
            {
                EnableDebugMesh();
            }
            else
            {
                DisableDebugMesh();
            }
        }

        if (MarkDebugCubeForDelete && DebugCube != null)
        {
            MarkDebugCubeForDelete = false;
            DestroyImmediate(DebugCube);
            DestroyImmediate(mat);
            
        }
        
    }

#endif
}