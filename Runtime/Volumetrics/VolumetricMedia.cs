using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class VolumetricMedia : MonoBehaviour
{
    //Simple helper scrpit for baker
    public enum ShapeType { Sphere, Box };
    public ShapeType shapeType;
    [Tooltip("3d texture. RGB is color")]
    public Texture3D Texture;

    Vector3 Size;

    [Range(0.01f,100)] public float ViewDistance = 1f;


    private void OnEnable()
    {
        VolumetricRegisters.RegisterParticipatingMedia(this);
        VolumeRenderingUtils.ExtinctionFromMeanFreePath(ViewDistance);
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
        Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position, gameObject.transform.rotation, gameObject.transform.lossyScale);
        Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
    }

}
