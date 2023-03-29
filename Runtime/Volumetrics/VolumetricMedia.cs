using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class VolumetricMedia : MonoBehaviour
{
    //Simple helper script for baker
    public enum ShapeType { Sphere, Box };
    public ShapeType shapeType;
    [Tooltip("3d texture. RGB is color")]
    public Texture3D Texture;
    
    public Vector3 Scale = Vector3.one;
   
    
    [HideInInspector,SerializeField] public Vector3 NormalizedScale;
    [HideInInspector,SerializeField] public Vector3 Corner;

    public float LocalExtinction()
    {
        return VolumeRenderingUtils.ExtinctionFromMeanFreePath(ViewDistance);
    }
    

    [Range(0.01f,100)] public float ViewDistance = 1f;
    [Range(0,1)] public float falloffDistance = .2f;


    private void OnEnable()
    {
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

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.gray;
        Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position, gameObject.transform.rotation, Vector3.Scale( Scale , gameObject.transform.lossyScale) );
        Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
        Gizmos.color = new Color(0.4f, 0.4f, 0.4f, .1f);
        Gizmos.DrawWireSphere(Vector3.zero, 0.5f * (1-falloffDistance) );

    }

    private void OnValidate()
    {
        //Check to see if the same and change if different
        Vector3 tempscale = Vector3.Scale(gameObject.transform.localScale, Scale);
        if (NormalizedScale != tempscale)  NormalizedScale = tempscale; //redundant check to prevent dirtying 
        

    }

}
