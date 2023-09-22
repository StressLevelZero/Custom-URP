using SLZ.SLZEditorTools;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ForceReloadDrawer : MaterialPropertyDrawer
{
    public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
    {
        ShaderGUIUtils.ForceRebuild(editor);
    }
}
