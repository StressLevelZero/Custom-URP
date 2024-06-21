using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace SLZ.URPEditorBridge
{
    public static class UIEditorBridge
    {
        public static ActiveEditorTracker GetActiveEditorTracker(Editor editor)
        {
            return editor.propertyViewer.tracker;
        }
    }
}
