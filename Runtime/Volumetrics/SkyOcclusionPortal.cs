using System;
using System.Collections.Generic;
using UnityEngine;

public class SkyOcclusionPortal : MonoBehaviour
{
    public enum PortalMode { Portal, Window }

    [Tooltip("Portal: Bypasses sky occlusion and makes sky visible; Window: Makes current geo invisible to the sky occlusion bake")]
    public PortalMode mode;

    // Lists to store renderers and their original materials
    private List<Renderer> rendererComponents = new List<Renderer>();
    private List<Material[]> originalMaterials = new List<Material[]>();

    // Store the original active state of the GameObject
    private bool originalActiveState;

    public void PrepareForRendering()
    {
        if (mode == PortalMode.Portal)
        {
            // **Portal Mode Logic**

            // Find all Renderer components in this object and its children
            Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactive: false);

            if (renderers.Length == 0)
            {
                return;
            }

            // Clear previous data
            rendererComponents.Clear();
            originalMaterials.Clear();

            foreach (Renderer rendererComponent in renderers)
            {
                if (rendererComponent == null || !rendererComponent.enabled)
                    continue;

                // Store the renderer and its original materials
                rendererComponents.Add(rendererComponent);
                originalMaterials.Add(rendererComponent.sharedMaterials);

                // Create an Unlit/Color material with white color
                Material unlitWhiteMaterial = new Material(Shader.Find("Unlit/Color"));
                unlitWhiteMaterial.color = Color.white;

                // Create an array of the same length as the original materials
                Material[] newMaterials = new Material[rendererComponent.sharedMaterials.Length];
                for (int i = 0; i < newMaterials.Length; i++)
                {
                    newMaterials[i] = unlitWhiteMaterial;
                }

                // Assign the new materials
                rendererComponent.sharedMaterials = newMaterials;
            }
        }
        else if (mode == PortalMode.Window)
        {
            // **Window Mode Logic**

            // Store the original active state of the GameObject
            originalActiveState = gameObject.activeSelf;

            // Disable the GameObject (and all its children)
            gameObject.SetActive(false);
        }
    }

    public void RestoreAfterRendering()
    {
        if (mode == PortalMode.Portal)
        {
            // **Portal Mode Logic**

            // Restore the original materials
            for (int i = 0; i < rendererComponents.Count; i++)
            {
                Renderer rendererComponent = rendererComponents[i];
                if (rendererComponent == null)
                    continue;

                rendererComponent.sharedMaterials = originalMaterials[i];
            }

            // Clear the lists
            rendererComponents.Clear();
            originalMaterials.Clear();
        }
        else if (mode == PortalMode.Window)
        {
            // **Window Mode Logic**

            // Restore the original active state of the GameObject
            gameObject.SetActive(originalActiveState);
        }
    }
}