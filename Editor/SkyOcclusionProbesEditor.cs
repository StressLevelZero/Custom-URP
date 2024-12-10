using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using NUnit.Framework;

[CustomEditor(typeof(SkyOcclusionProbes))]
public class SkyOcclusionProbesEditor : Editor
{
    private bool selectionMode = false;
    private List<int> selectedProbeIndices = new List<int>();

    private bool isDraggingSelection = false;
    private Vector2 dragStartPos;
    private Vector2 dragCurrentPos;
    private float dragThreshold = 4f; // Pixels
    private bool isMovingSelection = false;
    Tool LastTool = Tool.None;

    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("Used to occlude sky background lighting from volumetrics");

        LastTool = Tools.current;
        DrawDefaultInspector();

        // Add a toggle button for selection mode
        GUIStyle toggleButtonStyle = new GUIStyle(GUI.skin.button);
        selectionMode = GUILayout.Toggle(selectionMode, "Edit Mode", toggleButtonStyle);

        if (selectionMode)
        {
            // Add a button to clear the current selection
            if (GUILayout.Button("Clear Selection"))
            {
                selectedProbeIndices.Clear();
                isMovingSelection = false;
                // Repaint the scene view to update visuals
                SceneView.RepaintAll();
            }

            // Add a button to delete the current selection
            if (GUILayout.Button("Delete Selection"))
            {
                DeleteSelectedProbes();
            }

            // Add a button to duplicate the current selection
            if (GUILayout.Button("Duplicate Selection"))
            {
                DuplicateSelectedProbes();
            }

            // Add a button to select colliding probes
            if (GUILayout.Button("Select Colliding Probes"))
            {
                SelectCollidingProbes();
            }
        }
        else
        {
            Tools.current = LastTool;
        }
    }

    private void OnSceneGUI()
    {
        SkyOcclusionProbes probeScript = (SkyOcclusionProbes)target;

        if (probeScript.probePositions == null)
            return;

        Event e = Event.current;

        // Handle selection events
        if (selectionMode)
        {
            LastTool = Tools.current;
            Tools.current = Tool.None;

            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.D && (e.control || e.command))
            {
                // Override default behavior
                e.Use(); // Stops default action

                // Call your custom duplication function
                DuplicateSelectedProbes();
            }

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                dragStartPos = e.mousePosition;
                isDraggingSelection = false;
            }
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                if (Event.current.shift || Event.current.control) isMovingSelection = false;
                if (Event.current.shift && Event.current.control) isMovingSelection = true;
                if (selectedProbeIndices.Count < 1) isMovingSelection = false;

                float distance = Vector2.Distance(e.mousePosition, dragStartPos);
                if (!isDraggingSelection && distance > dragThreshold && !isMovingSelection)
                {
                    isDraggingSelection = true;
                }

                if (isDraggingSelection)
                {
                    dragCurrentPos = e.mousePosition;
                    // Repaint the scene view to update the selection rectangle
                    SceneView.RepaintAll();
                }
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                if (isDraggingSelection)
                {
                    isDraggingSelection = false;
                    dragCurrentPos = e.mousePosition;
                    SelectProbesInRectangle(dragStartPos, dragCurrentPos);
                    // Repaint the scene view to update the selected probes
                    SceneView.RepaintAll();
                }
            }
            else if (e.type == EventType.Repaint && isDraggingSelection)
            {
                // Draw the selection rectangle
                Handles.BeginGUI();
                Rect rect = GetScreenRect(dragStartPos, dragCurrentPos);
                EditorGUI.DrawRect(rect, new Color(0.3f, 0.5f, 1f, 0.25f));
                Handles.EndGUI();
            }

            // Draw the probes and handle selection clicks
            for (int i = 0; i < probeScript.probePositions.Length; i++)
            {
                Vector3 probePos = probeScript.probePositions[i] + probeScript.gameObject.transform.position;

                Color unselected = selectionMode ? new Color(0.6f, 0.6f, 0.5f, 0.25f) : Color.white;

                // Highlight selected probes
                Handles.color = selectedProbeIndices.Contains(i) ? Color.yellow : unselected;

                // Create an invisible button over each probe for selection
                float handleSize = HandleUtility.GetHandleSize(probePos) * 0.1f;

                if (Handles.Button(probePos, Quaternion.identity, handleSize, handleSize, Handles.SphereHandleCap))
                {
                    if (selectionMode)
                    {
                        // Toggle selection of this probe
                        if (Event.current.shift)
                        {
                            // Add or remove from selection
                            if (selectedProbeIndices.Contains(i))
                            {
                                selectedProbeIndices.Remove(i);
                            }
                            else
                            {
                                selectedProbeIndices.Add(i);
                            }
                        }
                        else
                        {
                            // Select only this probe
                            selectedProbeIndices.Clear();
                            selectedProbeIndices.Add(i);
                        }

                        // Repaint the scene view to update the visuals
                        SceneView.RepaintAll();
                    }
                }

                // Draw the sphere gizmo
                Handles.SphereHandleCap(0, probePos, Quaternion.identity, 0.1f, EventType.Repaint);
            }

            // Draw the position handle for moving selected probes
            if (selectionMode && selectedProbeIndices.Count > 0)
            {
                // Calculate center position of selected probes
                Vector3 center = Vector3.zero;
                foreach (int index in selectedProbeIndices)
                {
                    center += probeScript.probePositions[index];
                }

                center /= selectedProbeIndices.Count;
                center += probeScript.gameObject.transform.position;
                // Begin checking for changes
                EditorGUI.BeginChangeCheck();
                isMovingSelection = true;

                // Draw the standard position handle at the center
                Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(probeScript, "Move Selected Probes");

                    // Calculate the offset
                    Vector3 offset = newCenter - center;

                    // Move all selected probes
                    for (int i = 0; i < selectedProbeIndices.Count; i++)
                    {
                        int index = selectedProbeIndices[i];
                        probeScript.probePositions[index] += offset;
                    }

                    // Mark the object as dirty to save changes
                    EditorUtility.SetDirty(probeScript);
                    isMovingSelection = false;
                }
            }
        }
    }

    private Rect GetScreenRect(Vector2 screenPosition1, Vector2 screenPosition2)
    {
        // Get the rectangle between two screen positions
        Vector2 topLeft = Vector2.Min(screenPosition1, screenPosition2);
        Vector2 bottomRight = Vector2.Max(screenPosition1, screenPosition2);

        return new Rect(topLeft, bottomRight - topLeft);
    }

    private void SelectProbesInRectangle(Vector2 startScreenPos, Vector2 endScreenPos)
    {
        SkyOcclusionProbes probeScript = (SkyOcclusionProbes)target;

        Rect selectionRect = GetScreenRect(startScreenPos, endScreenPos);

        // Clear previous selection if Shift is not held
        if (!Event.current.shift)
        {
            selectedProbeIndices.Clear();
        }

        for (int i = 0; i < probeScript.probePositions.Length; i++)
        {
            Vector3 probePos = probeScript.probePositions[i] + probeScript.gameObject.transform.position;

            Vector2 screenPos = HandleUtility.WorldToGUIPoint(probePos);

            if (selectionRect.Contains(screenPos))
            {
                if (!selectedProbeIndices.Contains(i))
                    selectedProbeIndices.Add(i);
            }
        }
    }

    private void DeleteSelectedProbes()
    {
        SkyOcclusionProbes probeScript = (SkyOcclusionProbes)target;

        if (selectedProbeIndices.Count > 0)
        {
            Undo.RecordObject(probeScript, "Delete Selected Probes");

            List<Vector3> probePositionsList = new List<Vector3>(probeScript.probePositions);

            // Sort selected indices in descending order to avoid indexing issues
            selectedProbeIndices.Sort((a, b) => b.CompareTo(a));

            // Remove selected probes
            foreach (int index in selectedProbeIndices)
            {
                probePositionsList.RemoveAt(index);
            }

            // Assign back to array
            probeScript.probePositions = probePositionsList.ToArray();

            // Clear selection
            selectedProbeIndices.Clear();

            // Mark object as dirty
            EditorUtility.SetDirty(probeScript);

            // Repaint the scene view to update visuals
            SceneView.RepaintAll();
        }
    }

    private void DuplicateSelectedProbes()
    {
        SkyOcclusionProbes probeScript = (SkyOcclusionProbes)target;

        if (selectedProbeIndices.Count > 0)
        {
            Undo.RecordObject(probeScript, "Duplicate Selected Probes");

            List<Vector3> probePositionsList = new List<Vector3>(probeScript.probePositions);

            // Store duplicated indices
            List<int> newSelectedIndices = new List<int>();

            // Optional: Calculate a small offset for the duplicated probes
            Vector3 offset = new Vector3(0.015f, 0, 0); // Adjust as needed

            foreach (int index in selectedProbeIndices)
            {
                Vector3 position = probeScript.probePositions[index];
                Vector3 newPosition = position + offset; // Apply offset to duplicates
                probePositionsList.Add(newPosition);
                newSelectedIndices.Add(probePositionsList.Count - 1);
            }

            // Assign back to array
            probeScript.probePositions = probePositionsList.ToArray();

            // Switch selection to duplicates
            selectedProbeIndices = newSelectedIndices;

            // Mark object as dirty
            EditorUtility.SetDirty(probeScript);

            // Repaint the scene view to update visuals
            SceneView.RepaintAll();
        }
    }

    private void SelectCollidingProbes()
    {
        SkyOcclusionProbes probeScript = (SkyOcclusionProbes)target;

        // Clear the current selection
        selectedProbeIndices.Clear();

        // Radius to use for checking collision
        float radius = 0.1f; // Adjust as needed

        // Layer mask for collision detection (optional)
        int layerMask = ~0; // Collide with all layers

        for (int i = 0; i < probeScript.probePositions.Length; i++)
        {
            Vector3 probePos = probeScript.probePositions[i] + probeScript.gameObject.transform.position;

            // Check for overlapping colliders at the probe position, including terrain
            Collider[] colliders = Physics.OverlapSphere(probePos, radius, layerMask);

            if (colliders.Length > 0)
            {
                // There is a collision, select this probe
                selectedProbeIndices.Add(i);
            }
            else
            {
                // Additionally check for terrain collision using a raycast downwards
                var rayall = Physics.RaycastAll(probePos + Vector3.up * 100f, Vector3.down, 100f, layerMask);
                bool terrainhit = false;
                foreach (var ray in rayall)
                {
                    if (ray.collider is TerrainCollider) terrainhit = true;
                }
                // If the hit collider is a terrain collider, select this probe
                if (terrainhit)
                {
                    selectedProbeIndices.Add(i);
                }
            }
        }

        // Repaint the scene view to update visuals
        SceneView.RepaintAll();

        // Optionally, display a message if no probes were found
        if (selectedProbeIndices.Count == 0)
        {
            Debug.Log("No colliding probes found.");
        }
    }
    
        

    [MenuItem("GameObject/Light/Sky Occlusion Probes", false, 10)]
    private static void CreateSkyOcclusionProbes()
    {
        // Create a new GameObject
        GameObject newGameObject = new GameObject("Sky Occlusion Probe Group");
    
        // Attach the SkyOcclusionProbes component
        newGameObject.AddComponent<SkyOcclusionProbes>();

        // Register the creation in the undo system to make it undoable in the editor
        Undo.RegisterCreatedObjectUndo(newGameObject, "Create Sky Occlusion Probes");

        // Select the newly created object in the editor
        Selection.activeGameObject = newGameObject;
    }
    

}
