#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class GetGuidAndFileId
{
    [MenuItem("Assets/Print GUID and File ID")]
    private static void PrintGuidAndFileId()
    {
        foreach (var item in Selection.objects)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item, out string guid, out long localId))
            {
                Debug.Log($"GUID: {guid} | localFileId: {localId}");
            }
        }
    }

    [MenuItem("Assets/Print GUID and File ID", validate = true)]
    private static bool PrintGuidAndFileId_Validate()
    {
        if (Selection.objects.Length == 0)
            return false;

        return true;
    }
}
#endif