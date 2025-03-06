using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GetAllMaterials : MonoBehaviour
{
    public Material[] allMats;

    [ContextMenu("Get All Mats")]
    public void GetAllMats()
    {
        MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>(); // Get all MeshRenderers in children
        List<Material> materialsList = new List<Material>();

        foreach (MeshRenderer meshRenderer in meshRenderers)
        {
            if (meshRenderer != null && meshRenderer.sharedMaterials.Length > 0)
            {
                foreach (Material mat in meshRenderer.sharedMaterials)
                {
                    if (mat != null && !materialsList.Contains(mat)) // Avoid duplicates
                    {
                        materialsList.Add(mat);
                        Debug.Log($"Material found: {mat.name} on {meshRenderer.gameObject.name}");
                    }
                }
            }
        }

        allMats = materialsList.ToArray(); // Convert List to Array
    }

}
