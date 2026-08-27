#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Restores GEStorageUnit visuals from the source blend model (pre-migration behavior).
/// Menu: Tools/Fix GEStorageUnit From Blend Model
/// Batch: -executeMethod GEStorageUnitFix.RunBatch
/// </summary>
public static class GEStorageUnitFix
{
    private const string PrefabPath = "Assets/Prefabs/Selectables/GEStorageUnit.prefab";
    private const string BlendModelPath = "Assets/Models/Omni CT Scan/Power Equipment/SERVICE STORAGE CABINET.blend";
    private const string ReportPath = "TestData/GEStorageUnit_fix_report.txt";

    [MenuItem("Tools/Fix GEStorageUnit From Blend Model")]
    public static void RunMenu() => RunInternal();

    public static void RunBatch()
    {
        RunInternal();
        EditorApplication.Exit(0);
    }

    private static void RunInternal()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("GEStorageUnit fix report");
        report.AppendLine(System.DateTime.Now.ToString("O"));

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            report.AppendLine("FAIL: could not load prefab");
            WriteReport(report);
            Debug.LogError(report.ToString());
            return;
        }

        try
        {
            Transform root = prefabRoot.transform;

            Transform oldVisual = root.Find("SERVICE STORAGE CABINET");
            if (oldVisual != null)
            {
                report.AppendLine("Removing baked SERVICE STORAGE CABINET hierarchy");
                Object.DestroyImmediate(oldVisual.gameObject);
            }
            else
            {
                foreach (Transform child in root)
                {
                    if (child.name.Contains("SERVICE STORAGE"))
                    {
                        report.AppendLine("Removing visual child: " + child.name);
                        Object.DestroyImmediate(child.gameObject);
                        break;
                    }
                }
            }

            GameObject blendAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BlendModelPath);
            if (blendAsset == null)
            {
                report.AppendLine("FAIL: blend model missing at " + BlendModelPath);
                WriteReport(report);
                Debug.LogError(report.ToString());
                return;
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(blendAsset, root);
            visual.name = "SERVICE STORAGE CABINET";
            visual.transform.SetSiblingIndex(0);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            visual.transform.localScale = Vector3.one;
            SetLayerRecursively(visual, root.gameObject.layer);

            // SubSelectable mouse forwarding (UnityEventSender) on visual root.
            var eventSender = visual.GetComponent<UnityEventSender>();
            if (eventSender == null)
                eventSender = visual.AddComponent<UnityEventSender>();
            eventSender.Target = root.gameObject;

            Bounds b = CalcRendererBounds(visual);
            report.AppendLine("Blend visual bounds (world): " + b);
            report.AppendLine("Blend visual bounds Y min/max: " + b.min.y + " / " + b.max.y);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            report.AppendLine("SUCCESS: prefab saved using blend model with -90X rotation");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        WriteReport(report);
        Debug.Log(report.ToString());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static Bounds CalcRendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static void WriteReport(System.Text.StringBuilder sb)
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }
}
#endif
