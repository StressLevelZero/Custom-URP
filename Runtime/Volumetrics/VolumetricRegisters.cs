using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
//using Unity.Mathematics;

public class VolumetricRegisters
{
    public static List<ParticipatingMediaEntity> participatingMediaEntities = new List<ParticipatingMediaEntity>();
    public static List<BakedVolumetricArea> volumetricAreas = new List<BakedVolumetricArea>();

    public static bool _meshObjectsNeedRebuilding = true;
//    public static List<RayTracingObject> _rayTracingObjects = new List<RayTracingObject>();
   // public static void RegisterObject(RayTracingObject obj)
   // {
   //     _rayTracingObjects.Add(obj);
   //     _meshObjectsNeedRebuilding = true;
   ////     Debug.Log("Added " + obj.name + " to raycasting database");

   // }
  //  public static void UnregisterObject(RayTracingObject obj)
  //  {
  //      _rayTracingObjects.Remove(obj);
  //      _meshObjectsNeedRebuilding = true;
  ////      Debug.Log("Removed " + obj.name + " to raycasting database");

  //  }

    public static void RegisterVolumetricArea(BakedVolumetricArea volumetricArea)
    {
        volumetricAreas.Add(volumetricArea);
    //    Debug.Log("Added " + volumetricArea.name + " to register");
    }
    public static void UnregisterVolumetricArea(BakedVolumetricArea volumetricArea)
    {
        volumetricAreas.Remove(volumetricArea);
    //    Debug.Log("Removed " + volumetricArea.name + " from register");

    }

}
