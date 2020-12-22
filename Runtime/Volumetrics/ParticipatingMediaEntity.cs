using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VolumetricMedia : MonoBehaviour
{
    //Simple helper scrpit for baker
    public enum ShapeType { Sphere, Box };
    public ShapeType shapeType;
    [Tooltip("3d texture. RGB is color")]
    public Texture3D Texture;

    Vector3 Size;

    [Range(0,1)] public float Absorption = 0.1f;



    private void OnDrawGizmos()
    {

        Gizmos.color = Color.gray;
        Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position, gameObject.transform.rotation, gameObject.transform.lossyScale);
        Gizmos.DrawWireSphere(Vector3.zero, 0.5f);

    }

}
