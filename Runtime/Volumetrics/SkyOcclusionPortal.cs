using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class SkyOcclusionPortal : MonoBehaviour
{
    [HideInInspector, NonSerialized] public Renderer rendererComponent;
    [HideInInspector, NonSerialized] public Material[] originalMaterials;
    
    public enum PortalMode{ Portal, Window }

    [Tooltip("Portal: Bypasses sky occlusion and makes sky visible; Window: Makes current geo invisible to the sky occlusion bake  ")]
    
    public PortalMode mode;

    private bool skipRender = false;

    // private void Reset()
    // {
    //     rendererComponent = GetComponent<Renderer>();
    // }

    public void PrepareForRendering()
    {
        rendererComponent = GetComponent<Renderer>();

        if (rendererComponent != enabled || gameObject != enabled || this != enabled)
        {
            skipRender = true;
            return;
        }
        else skipRender = false;


        // Store the original materials
        originalMaterials = rendererComponent.sharedMaterials;

        if (mode == PortalMode.Portal)
        {
            // Create an Unlit/Color material with white color
            Material unlitWhiteMaterial = new Material(Shader.Find("Unlit/Color"));
            unlitWhiteMaterial.color = Color.white;

            // Create an array of the same length as the original materials
            Material[] newMaterials = new Material[originalMaterials.Length];
            for (int i = 0; i < newMaterials.Length; i++)
            {
                newMaterials[i] = unlitWhiteMaterial;
            }

            // Assign the new materials
            rendererComponent.sharedMaterials = newMaterials;
        }
        else if (mode == PortalMode.Window)
        {
            // Disable the renderer to make the object invisible
            rendererComponent.enabled = false;
        }
    }

    public void RestoreAfterRendering()
    {
        if (skipRender) return;
        
        // Restore the portals after rendering

        if (mode == PortalMode.Portal)
        {
            // Restore the original materials
            rendererComponent.sharedMaterials = originalMaterials;
        }
        else if (mode == PortalMode.Window)
        {
            // Enable the renderer
            rendererComponent.enabled = true;
        }
    }

}
