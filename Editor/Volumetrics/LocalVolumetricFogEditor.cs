#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(LocalVolumetricFog))]
public sealed class LocalVolumetricFogEditor : Editor
{
    private readonly BoxBoundsHandle _boxHandle = new BoxBoundsHandle();

    // Log-scaled density handle range: 0.001 .. 100
    // private const float kLogDensityMin = -3f;
    // private const float kLogDensityMax =  2f;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var fog = (LocalVolumetricFog)target;
        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(
            "Scene controls:\n" +
            "• Outer handle = volume size\n" +
            "• Cyan inner handle = falloff start\n" +
            "• Orange vertical handle = density\n",
            MessageType.None);

        EditorGUILayout.LabelField("Extinction", fog.density.ToString("0.###"));
    }

    private void OnSceneGUI()
    {
        var fog = (LocalVolumetricFog)target;
        if (fog == null) return;

        switch (fog.shapeType)
        {
            case LocalVolumetricFog.ShapeType.Box:
                DrawBoxHandles(fog);
                DrawFloatingScalarWidget(fog);
                break;

            case LocalVolumetricFog.ShapeType.Sphere:
                DrawScaledSphere(fog);
                break;
        }
    }

    private void DrawBoxHandles(LocalVolumetricFog fog)
    {
        Transform t = fog.transform;

        Matrix4x4 boxMatrix = Matrix4x4.TRS(t.position, Quaternion.identity, t.lossyScale);

        using (new Handles.DrawingScope(boxMatrix))
        {
            Vector3 outerSize = ClampMin(fog.Scale, 0.01f);

            // ---- Outer size ----
            _boxHandle.center = Vector3.zero;
            _boxHandle.size = outerSize;
            Handles.color = new Color(1f, 1f, 1f, 0.5f);

            EditorGUI.BeginChangeCheck();
            _boxHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(fog, "Resize Local Volumetric Fog");
                fog.Scale = ClampMin(_boxHandle.size, 0.01f);
                ApplyAndDirty(fog);
                outerSize = fog.Scale;
            }

            // ---- Inner falloff preview ----
            float innerRatio = Mathf.Clamp01(1f - fog.falloffDistance);
            Vector3 innerSize = outerSize * innerRatio;

            Handles.color = new Color(0.3f, 0.9f, 1f, .35f);
            Handles.DrawWireCube(Vector3.zero, innerSize);

            // Use the +X inner face as a uniform falloff handle.
            //float outerHalfX = Mathf.Max(outerSize.x * 0.5f, 0.0001f);
        }
    }

  

   
private void DrawFloatingScalarWidget(LocalVolumetricFog fog)
{
    Camera cam = SceneView.currentDrawingSceneView != null
        ? SceneView.currentDrawingSceneView.camera
        : Camera.current;

    if (cam == null)
        return;

    Vector3 center = fog.transform.position;
    float hs = HandleUtility.GetHandleSize(center);

    Vector3 camRight = cam.transform.right;
    Vector3 camUp    = cam.transform.up;

    Vector3 anchor = center + camRight * (hs * 1.35f) + camUp * (hs * 0.95f);

    float panelWidth  = hs * 0.9f;
    float panelHeight = hs * 0.9f;

    // Horizontal = falloff
    float falloffT = Mathf.Clamp01(1f - fog.falloffDistance);
    Vector3 fallStart = anchor;
    Vector3 fallEnd   = anchor + camRight * panelWidth;
    Vector3 fallPos   = Vector3.Lerp(fallStart, fallEnd, falloffT);

    Handles.color = new Color(0.3f, 0.9f, 1f, 1f);
    Handles.DrawLine(fallStart, fallEnd);

    EditorGUI.BeginChangeCheck();
    Vector3 newFallPos = Handles.Slider(
        fallPos,
        camRight,
        hs * 0.15f,
        Handles.SphereHandleCap,
        0f);
    if (EditorGUI.EndChangeCheck())
    {
        Undo.RecordObject(fog, "Adjust Fog Falloff");

        float newT = Mathf.Clamp01(Vector3.Dot(newFallPos - fallStart, camRight) / panelWidth);
        fog.falloffDistance = Mathf.Clamp01(1f - newT);

        ApplyAndDirty(fog);
    }

    // Vertical = density (log scale)
    const float logMin = -3f;
    const float logMax =  2f;

    float logDensity = Mathf.Log10(Mathf.Max(0.001f, fog.density));
    float densityT = Mathf.InverseLerp(logMin, logMax, logDensity);

    Vector3 densStart = anchor + camUp * (hs * 0.2f);
    Vector3 densEnd   = densStart + camUp * panelHeight;
    Vector3 densPos   = Vector3.Lerp(densStart, densEnd, densityT);

    Handles.color = new Color(1f, 0.65f, 0.15f, 1f);
    Handles.DrawLine(densStart, densEnd);

    EditorGUI.BeginChangeCheck();
    Vector3 newDensPos = Handles.Slider(
        densPos,
        camUp,
        hs * 0.15f,
        Handles.SphereHandleCap,
        0f);
    if (EditorGUI.EndChangeCheck())
    {
        Undo.RecordObject(fog, "Adjust Fog Density");

        float newT = Mathf.Clamp01(Vector3.Dot(newDensPos - densStart, camUp) / panelHeight);
        float newLog = Mathf.Lerp(logMin, logMax, newT);
        fog.density = Mathf.Pow(10f, newLog);

        ApplyAndDirty(fog);
    }
    
    Handles.color = Color.white;
    Handles.Label(
        camRight * .15f + anchor + camUp * (panelHeight * .5f + hs * 0.15f),
        $"<color=#ffca80>Density {fog.density:0.###}</color>\n<color=#80e8ff>Falloff {fog.falloffDistance:0.##}</color>",
        LabelStyle
    );
}
private static GUIStyle _labelStyle;
private static GUIStyle LabelStyle => _labelStyle ??= new GUIStyle
{
    richText = true,
    normal = { textColor = Color.white }
};
private void DrawScaledSphere(LocalVolumetricFog fog)
{
    Transform t = fog.transform;
    Vector3 center = t.position;

    // Normal XYZ scale gizmo stays.
    // using (new Handles.DrawingScope(Matrix4x4.TRS(center, Quaternion.identity, Vector3.one)))
    // {
    //     EditorGUI.BeginChangeCheck();
    //     Vector3 newScale = Handles.ScaleHandle(
    //         ClampMin(fog.Scale, 0.01f),
    //         Vector3.zero,
    //         Quaternion.identity,
    //         HandleUtility.GetHandleSize(center));
    //
    //     if (EditorGUI.EndChangeCheck())
    //     {
    //         Undo.RecordObject(fog, "Resize Local Volumetric Fog");
    //         fog.Scale = ClampMin(newScale, 0.01f);
    //         ApplyAndDirty(fog);
    //     }
    // }

    // Preview rings using your scaled-sphere model:
    // TRS(position, identity, Scale * 0.5 * lossyScale)
    Vector3 halfWorldScale = Vector3.Scale(fog.Scale * 0.5f, t.lossyScale);

    DrawScaledSphereRings(center, halfWorldScale, Color.white * .5f);

    float innerRatio = Mathf.Clamp01(1f - fog.falloffDistance);
    DrawScaledSphereRings(center, halfWorldScale * innerRatio, new Color(0.3f, 0.9f, 1f, .35f));

    // Per-axis shell sliders on the outer shell.
    DrawSphereAxisHandle(fog, 0, +1f);
    DrawSphereAxisHandle(fog, 0, -1f);
    DrawSphereAxisHandle(fog, 1, +1f);
    DrawSphereAxisHandle(fog, 1, -1f);
    DrawSphereAxisHandle(fog, 2, +1f);
    DrawSphereAxisHandle(fog, 2, -1f);

    DrawFloatingScalarWidget(fog);
}

private void DrawSphereAxisHandle(LocalVolumetricFog fog, int axisIndex, float sign)
{
    Transform t = fog.transform;
    Vector3 center = t.position;

    Vector3 axis = axisIndex switch
    {
        0 => Vector3.right,
        1 => Vector3.up,
        _ => Vector3.forward
    };

    float lossy = axisIndex switch
    {
        0 => Mathf.Abs(t.lossyScale.x),
        1 => Mathf.Abs(t.lossyScale.y),
        _ => Mathf.Abs(t.lossyScale.z)
    };

    lossy = Mathf.Max(lossy, 1e-5f);

    float halfWorldExtent = axisIndex switch
    {
        0 => fog.Scale.x * 0.5f * lossy,
        1 => fog.Scale.y * 0.5f * lossy,
        _ => fog.Scale.z * 0.5f * lossy
    };

    Vector3 handlePos = center + axis * (halfWorldExtent * sign);
    float handleSize = HandleUtility.GetHandleSize(handlePos) * 0.08f;

    Handles.color = axisIndex switch
    {
        0 => new Color(1f, 0.35f, 0.35f, .5f),
        1 => new Color(0.35f, 1f, 0.35f, .5f),
        _ => new Color(0.35f, 0.65f, 1f, .5f)
    };

    EditorGUI.BeginChangeCheck();
    Vector3 newPos = Handles.Slider(
        handlePos,
        axis * sign,
        handleSize,
        Handles.DotHandleCap,
        0f);

    if (EditorGUI.EndChangeCheck())
    {
        Undo.RecordObject(fog, "Scale Local Volumetric Fog Axis");

        float newHalfWorld = Mathf.Abs(Vector3.Dot(newPos - center, axis));
        float newScaleValue = Mathf.Max(0.01f, (newHalfWorld / lossy) * 2f);

        Vector3 s = fog.Scale;
        switch (axisIndex)
        {
            case 0: s.x = newScaleValue; break;
            case 1: s.y = newScaleValue; break;
            case 2: s.z = newScaleValue; break;
        }

        fog.Scale = ClampMin(s, 0.01f);
        ApplyAndDirty(fog);
    }
}

private void DrawScaledSphereRings(Vector3 center, Vector3 radii, Color color)
{
    Handles.color = color;

    Matrix4x4 old = Handles.matrix;

    Handles.matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(radii.x, radii.y, 1f));
    Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 1f);

    Handles.matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(radii.x, 1f, radii.z));
    Handles.DrawWireDisc(Vector3.zero, Vector3.up, 1f);

    Handles.matrix = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(1f, radii.y, radii.z));
    Handles.DrawWireDisc(Vector3.zero, Vector3.right, 1f);

    Handles.matrix = old;
}
    private static Vector3 ClampMin(Vector3 v, float min)
    {
        v.x = Mathf.Max(v.x, min);
        v.y = Mathf.Max(v.y, min);
        v.z = Mathf.Max(v.z, min);
        return v;
    }

    private static void ApplyAndDirty(LocalVolumetricFog fog)
    {
        fog.RefreshCachedData();
        EditorUtility.SetDirty(fog);
        VolumetricRegisters.MarkClipmapDirty();
        SceneView.RepaintAll();
    }
}
#endif