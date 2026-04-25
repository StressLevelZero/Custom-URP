using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using SLZ;

namespace SLZ.SLZEditorTools
{
    public class SortedPostBakeEvent : StaticSortedEvent<float>
    {
        [InitializeOnLoadMethod]
        static void RegisterEvents()
        {
            Lightmapping.bakeCompleted += SortedPostBakeEvent.Invoke;
            EditorSceneManager.sceneClosed += SceneClosedEvent;
        }

        static void SceneClosedEvent(Scene scene)
        {
            SortedPostBakeEvent.GarbageCollect();
        }
    }
}