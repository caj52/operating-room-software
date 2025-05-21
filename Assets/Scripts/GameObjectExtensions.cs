using System.Collections.Generic;
using UnityEngine;

public static class GameObjectExtensions
{
    public static List<GameObject> GetAllChildren(this GameObject parent)
    {
        List<GameObject> children = new List<GameObject>();
        GetChildrenRecursive(parent.transform, children);
        return children;
    }

    private static void GetChildrenRecursive(Transform parent, List<GameObject> result)
    {
        foreach (Transform child in parent)
        {
            result.Add(child.gameObject);
            GetChildrenRecursive(child, result); // Recursive call
        }
    }
}
