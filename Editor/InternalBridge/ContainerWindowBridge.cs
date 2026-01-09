using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

public class ContainerWindowBridge
{
    public static Rect MainWindowRect => ContainerWindow.windows[0].position;

    public static Rect ParentBorderSize(EditorWindow window, Rect relative)
    {
        return window.m_Parent.borderSize.Add(relative);
    }

    public static void DisableSavingLayout(EditorWindow window)
    {
        window.m_Parent.window.m_DontSaveToLayout = true;
    }
}
