#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders GEStorageUnit (and Storage2 reference) to PNGs for visual face verification.
/// Batch: -executeMethod GEStorageUnitCaptureRenders.Run
/// </summary>
public static class GEStorageUnitCaptureRenders
{
    private const string OutDir = "TestData/GEStorageUnit_renders";
    private const string PrefabPath = "Assets/Prefabs/Selectables/GEStorageUnit.prefab";
    private const string Storage2Path = "Assets/Prefabs/Selectables/Storage2.prefab";
    private const string BlendPath = "Assets/Models/Omni CT Scan/Power Equipment/SERVICE STORAGE CABINET.blend";
    private const int Width = 1024;
    private const int Height = 1024;

    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("GEStorageUnitCaptureRenders " + System.DateTime.Now.ToString("O"));

        string absOut = Path.Combine(Directory.GetCurrentDirectory(), OutDir);
        Directory.CreateDirectory(absOut);
        foreach (var f in Directory.GetFiles(absOut, "unity_*.png"))
            File.Delete(f);

        // Scene setup
        var lightGo = new GameObject("CaptureLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var fillGo = new GameObject("FillLight");
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.45f;
        fill.transform.rotation = Quaternion.Euler(20f, 140f, 0f);

        var camGo = new GameObject("CaptureCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.35f, 0.38f, 0.42f, 1f);
        cam.orthographic = false;
        cam.fieldOfView = 35f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 50f;
        cam.allowHDR = false;
        cam.allowMSAA = true;

        // Floor plane at Y=0 for stuck-in-floor check
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                                    ?? Shader.Find("Standard"));
        floorMat.color = new Color(0.55f, 0.55f, 0.58f, 1f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

        try
        {
            CapturePrefab(PrefabPath, "GE", cam, absOut, report, Vector3.zero);
            CapturePrefab(Storage2Path, "Storage2", cam, absOut, report, new Vector3(3f, 0f, 0f));

            // Also capture blend model alone with -90X (known-good source)
            var blend = AssetDatabase.LoadAssetAtPath<GameObject>(BlendPath);
            if (blend != null)
            {
                var blendInst = (GameObject)PrefabUtility.InstantiatePrefab(blend);
                blendInst.name = "BLEND_SERVICE_STORAGE";
                blendInst.transform.position = new Vector3(6f, 0f, 0f);
                blendInst.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                report.AppendLine("--- Blend alone ---");
                DumpRenderers(blendInst, report);
                FrameAndShoot(cam, blendInst, absOut, "blend_alone", report);
                Object.DestroyImmediate(blendInst);
            }
            else
            {
                report.AppendLine("WARN: blend not loadable at " + BlendPath);
            }

            // Variant shots of GE with candidate transforms for diagnosis
            CaptureGEVariants(cam, absOut, report);
        }
        finally
        {
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(fillGo);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(floor);
            if (floorMat != null) Object.DestroyImmediate(floorMat);
        }

        string reportPath = Path.Combine(absOut, "report.txt");
        File.WriteAllText(reportPath, report.ToString());
        Debug.Log(report.ToString());
        EditorApplication.Exit(0);
    }

    private static void CaptureGEVariants(Camera cam, string absOut, StringBuilder report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return;

        var variants = new (string name, System.Action<Transform> apply)[]
        {
            ("ge_var_current", t => { }),
            ("ge_var_child_180Z", t =>
            {
                var mesh = FindMeshChild(t);
                if (mesh != null) mesh.localRotation = Quaternion.Euler(0f, 0f, 180f);
            }),
            ("ge_var_child_negScaleX", t =>
            {
                var mesh = FindMeshChild(t);
                if (mesh != null) mesh.localScale = new Vector3(-1f, 1f, 1f);
            }),
            ("ge_var_parent_pos90X", t =>
            {
                var visual = t.Find("SERVICE STORAGE CABINET");
                if (visual != null) visual.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }),
            ("ge_var_cullOff", t =>
            {
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.materials; // instance copies — do not mutate assets
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] != null && mats[i].HasProperty("_Cull"))
                            mats[i].SetFloat("_Cull", (float)CullMode.Off);
                    }
                    r.materials = mats;
                }
            }),
        };

        report.AppendLine("--- GE transform variants ---");
        foreach (var (name, apply) in variants)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = new Vector3(0f, 0f, 0f);
            apply(inst.transform);
            FrameAndShoot(cam, inst, absOut, name, report);
            Object.DestroyImmediate(inst);
        }
    }

    private static Transform FindMeshChild(Transform root)
    {
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            return mf.transform;
        return null;
    }

    private static void CapturePrefab(string path, string tag, Camera cam, string absOut,
        StringBuilder report, Vector3 pos)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        report.AppendLine($"=== {tag} prefab={path} loaded={(prefab != null)} ===");
        if (prefab == null) return;

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        // Match floor placement: SetPosition uses LookRotation(hit.normal) for floor hits.
        inst.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.up));
        if (tag == "GE")
        {
            var visual = inst.transform.Find("SERVICE STORAGE CABINET");
            if (visual != null)
                visual.localPosition = new Vector3(0f, 0f, 0.4572f);
        }
        DumpRenderers(inst, report);
        FrameAndShoot(cam, inst, absOut, tag.ToLowerInvariant() + "_iso", report);

        // Extra angles for GE
        if (tag == "GE")
        {
            ShootFrom(cam, inst, absOut, "ge_front", new Vector3(0, 0.55f, 2.2f), report);
            ShootFrom(cam, inst, absOut, "ge_back", new Vector3(0, 0.55f, -2.2f), report);
            ShootFrom(cam, inst, absOut, "ge_right", new Vector3(2.2f, 0.55f, 0), report);
            ShootFrom(cam, inst, absOut, "ge_left", new Vector3(-2.2f, 0.55f, 0), report);
            ShootFrom(cam, inst, absOut, "ge_top", new Vector3(0, 2.5f, 0.01f), report);
        }

        Object.DestroyImmediate(inst);
    }

    private static void DumpRenderers(GameObject go, StringBuilder report)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        Bounds? enc = null;
        foreach (var r in rends)
        {
            if (!enc.HasValue) enc = r.bounds;
            else
            {
                var b = enc.Value;
                b.Encapsulate(r.bounds);
                enc = b;
            }
            report.AppendLine(
                $"  renderer={GetPath(r.transform)} enabled={r.enabled} mats={r.sharedMaterials.Length} " +
                $"boundsY=[{r.bounds.min.y:F3},{r.bounds.max.y:F3}] rot={r.transform.localRotation.eulerAngles}");
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                report.AppendLine($"    mesh={mf.sharedMesh.name} verts={mf.sharedMesh.vertexCount} " +
                                  $"subMeshes={mf.sharedMesh.subMeshCount}");
        }
        if (enc.HasValue)
            report.AppendLine($"  encapsY=[{enc.Value.min.y:F3},{enc.Value.max.y:F3}] size={enc.Value.size}");
    }

    private static void FrameAndShoot(Camera cam, GameObject target, string absOut, string name,
        StringBuilder report)
    {
        Bounds b = Encapsulate(target);
        Vector3 center = b.center;
        float radius = Mathf.Max(b.extents.magnitude, 0.3f);
        Vector3 camPos = center + new Vector3(radius * 1.4f, radius * 0.9f, radius * 1.4f);
        ShootFrom(cam, target, absOut, name, camPos, report, center);
    }

    private static void ShootFrom(Camera cam, GameObject target, string absOut, string name,
        Vector3 camPos, StringBuilder report, Vector3? lookAt = null)
    {
        Bounds b = Encapsulate(target);
        Vector3 focus = lookAt ?? b.center;
        cam.transform.position = camPos;
        cam.transform.LookAt(focus);

        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        string path = Path.Combine(absOut, name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        report.AppendLine($"  wrote {path} cam={camPos} look={focus} boundsY=[{b.min.y:F3},{b.max.y:F3}]");

        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
    }

    private static Bounds Encapsulate(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0)
            return new Bounds(go.transform.position, Vector3.one * 0.5f);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);
        return b;
    }

    private static string GetPath(Transform t)
    {
        string p = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }
        return p;
    }
}
#endif
