using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParticipatingMediaEntity : MonoBehaviour
{
    //Simple helper scrpit for baker
    public enum ShapeType { Sphere };

    public ShapeType shapeType;

    Vector3 Size;

    [Range(0,1)] public float Absorption = 0.1f;



    private void OnDrawGizmos()
    {

        Gizmos.color = Color.gray;
        Gizmos.matrix = Matrix4x4.TRS(gameObject.transform.position, gameObject.transform.rotation, gameObject.transform.lossyScale);
        Gizmos.DrawWireSphere(Vector3.zero, 0.5f);

    }

}
