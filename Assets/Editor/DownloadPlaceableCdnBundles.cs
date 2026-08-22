#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>CDN mirror download retired — placeables load from Assets/ in the editor.</summary>
public static class DownloadPlaceableCdnBundles
{
    [MenuItem("Tools/Operating Room/Download Placeable CDN Bundles", false, 100)]
    private static void DownloadFromMenu()
    {
        EditorUtility.DisplayDialog(
            "CDN retired",
            "Placeables load from Assets/Prefabs/Selectables in the editor.\n\nTestData/CdnMirror and remote CDN downloads are removed.",
            "OK");
    }
}
#endif
