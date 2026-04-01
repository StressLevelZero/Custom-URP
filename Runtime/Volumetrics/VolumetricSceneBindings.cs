using UnityEngine;

namespace SLZ
{
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)]
public sealed class VolumetricSceneBindings : MonoBehaviour
{
    [SerializeField] private BakedVolumetricsData bakedVolumetricsData;

    public BakedVolumetricsData BakedVolumetricsData => bakedVolumetricsData;

#if UNITY_EDITOR
    public void EditorSetBakedVolumetricsData(BakedVolumetricsData data)
    {
        bakedVolumetricsData = data;
    }
#endif
}
}