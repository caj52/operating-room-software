#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch audit for GEStorageUnit mesh orientation. Run:
/// Unity.exe -batchmode -quit -projectPath ... -executeMethod GEStorageUnitMeshAudit.Run -logFile ...
/// </summary>
public static class GEStorageUnitMeshAudit
{
    private const string OutputPath = "TestData/GEStorageUnit_mesh_audit.txt";
    private const string MirrorMeshPath =
        "Assets/_MirrorExtract/gameobject_fd562582e6e124e41980d48de0029757/cerradura.006.mesh.asset";
    private const string BlendModelPath =
        "Assets/Models/Omni CT Scan/Power Equipment/SERVICE STORAGE CABINET.blend";
    private const string PrefabPath = "Assets/Prefabs/Selectables/GEStorageUnit.prefab";
    private const string Storage2Path = "Assets/Prefabs/Selectables/Storage2.prefab";

    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("GEStorageUnit mesh audit");
        sb.AppendLine($"Time: {DateTime.Now:O}");
        sb.AppendLine();

        Mesh mirrorMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MirrorMeshPath);
        GameObject blendRoot = AssetDatabase.LoadMainAssetAtPath(BlendModelPath) as GameObject;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject storage2 = AssetDatabase.LoadAssetAtPath<GameObject>(Storage2Path);

        if (mirrorMesh == null)
        {
            sb.AppendLine($"FAIL: mirror mesh not found at {MirrorMeshPath}");
            Write(sb);
            EditorApplication.Exit(1);
            return;
        }

        sb.AppendLine("=== Mirror mesh (cerradura.006) local AABB ===");
        sb.AppendLine(BoundsLine(mirrorMesh.bounds));
        sb.AppendLine();

        sb.AppendLine("=== Mirror mesh winding / normals (mesh local space) ===");
        sb.AppendLine(AnalyzeMeshNormals(mirrorMesh, Matrix4x4.identity, "+Y camera (top-down)"));
        sb.AppendLine(AnalyzeMeshNormals(mirrorMesh, Matrix4x4.identity, "+Z camera (front)"));
        sb.AppendLine();

        if (blendRoot != null)
        {
            sb.AppendLine("=== Blend model hierarchy ===");
            DumpHierarchy(blendRoot.transform, sb, 0);
            sb.AppendLine();

            foreach (var mf in blendRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;
                sb.AppendLine($"--- Blend mesh: {GetPath(mf.transform)} ---");
                sb.AppendLine($"Local rotation: {mf.transform.localRotation.eulerAngles}");
                sb.AppendLine($"Local position: {mf.transform.localPosition}");
                sb.AppendLine(BoundsLine(mf.sharedMesh.bounds));
                Matrix4x4 worldFromMesh = mf.transform.localToWorldMatrix;
                sb.AppendLine(AnalyzeMeshNormals(mf.sharedMesh, worldFromMesh, "world +Y view"));
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine($"WARN: blend model not found at {BlendModelPath}");
            sb.AppendLine();
        }

        if (prefab != null)
        {
            sb.AppendLine("=== GEStorageUnit prefab (instantiated) ===");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                DumpHierarchy(instance.transform, sb, 0);
                sb.AppendLine();

                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    sb.AppendLine($"Renderer: {GetPath(r.transform)}");
                    sb.AppendLine($"  localRotation: {r.transform.localRotation}");
                    sb.AppendLine($"  worldBounds: {BoundsLine(r.bounds)}");
                    sb.AppendLine($"  root Y min/max: {RootYRange(instance.transform, r.bounds)}");
                }

                sb.AppendLine();
                sb.AppendLine("=== Transform candidates (renderer world bounds Y min, +Y facing ratio) ===");
                var candidates = new[]
                {
                    ("current prefab", Matrix4x4.identity),
                    ("mesh identity, no parent -90X", Matrix4x4.Rotate(Quaternion.identity)),
                    ("mesh only -90X", Matrix4x4.Rotate(Quaternion.Euler(-90, 0, 0))),
                    ("mesh only +90X", Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0))),
                    ("mesh only 180Z", Matrix4x4.Rotate(Quaternion.Euler(0, 0, 180))),
                    ("parent -90X + child 180Z (pre-fix)", Matrix4x4.Rotate(Quaternion.Euler(-90, 0, 0)) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, 180))),
                    ("parent +90X", Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0))),
                    ("-90X then 180Y", Matrix4x4.Rotate(Quaternion.Euler(-90, 0, 0)) * Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0))),
                };

                foreach (var (label, matrix) in candidates)
                {
                    Bounds b = TransformBounds(mirrorMesh.bounds, matrix);
                    float facing = FacingRatio(mirrorMesh, matrix, Vector3.up);
                    sb.AppendLine($"{label}: Y[{b.min.y:F4},{b.max.y:F4}] facing+Y={facing:P1}");
                }

                sb.AppendLine();
                sb.AppendLine("=== Best single rotation on mesh (max +Y facing, Ymin>=0) ===");
                var best = FindBestRotation(mirrorMesh);
                foreach (var row in best)
                    sb.AppendLine(row);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        if (storage2 != null)
        {
            sb.AppendLine();
            sb.AppendLine("=== Storage2 reference (instantiated) ===");
            GameObject s2 = (GameObject)PrefabUtility.InstantiatePrefab(storage2);
            try
            {
                foreach (var r in s2.GetComponentsInChildren<Renderer>(true))
                {
                    sb.AppendLine($"Renderer: {GetPath(r.transform)}");
                    sb.AppendLine($"  localRotation: {r.transform.localRotation}");
                    sb.AppendLine($"  worldBounds: {BoundsLine(r.bounds)}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(s2);
            }
        }

        Write(sb);
        EditorApplication.Exit(0);
    }

    private static List<string> FindBestRotation(Mesh mesh)
    {
        var results = new List<(string label, float facing, float ymin, Quaternion rot)>();
        int[] xs = { -90, 0, 90, 180 };
        int[] ys = { 0, 90, 180, 270 };
        int[] zs = { 0, 90, 180, 270 };

        foreach (int x in xs)
        foreach (int y in ys)
        foreach (int z in zs)
        {
            if (x == 0 && y == 0 && z == 0)
                continue;
            var q = Quaternion.Euler(x, y, z);
            var m = Matrix4x4.Rotate(q);
            Bounds b = TransformBounds(mesh.bounds, m);
            float facing = FacingRatio(mesh, m, Vector3.up);
            results.Add(($"Euler({x},{y},{z})", facing, b.min.y, q));
        }

        return results
            .OrderByDescending(r => r.facing)
            .ThenBy(r => Math.Abs(r.ymin))
            .Take(12)
            .Select(r => $"{r.label}: facing+Y={r.facing:P1} Ymin={r.ymin:F4}")
            .ToList();
    }

    private static string AnalyzeMeshNormals(Mesh mesh, Matrix4x4 toWorld, string viewLabel)
    {
        Vector3[] verts = mesh.vertices;
        Vector3[] norms = mesh.normals;
        int[] tris = mesh.triangles;
        if (verts == null || norms == null || tris == null || tris.Length == 0)
            return "  (no readable mesh data)";

        Vector3 viewDir = Vector3.down; // camera above looking down
        int front = 0;
        int back = 0;
        int normalMismatch = 0;
        int samples = 0;

        for (int i = 0; i < tris.Length; i += 3)
        {
            int i0 = tris[i];
            int i1 = tris[i + 1];
            int i2 = tris[i + 2];
            Vector3 p0 = toWorld.MultiplyPoint3x4(verts[i0]);
            Vector3 p1 = toWorld.MultiplyPoint3x4(verts[i1]);
            Vector3 p2 = toWorld.MultiplyPoint3x4(verts[i2]);
            Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            Vector3 vertNormal = toWorld.MultiplyVector(
                (norms[i0] + norms[i1] + norms[i2]) / 3f).normalized;

            float faceDot = Vector3.Dot(faceNormal, viewDir);
            if (faceDot > 0.01f)
                front++;
            else if (faceDot < -0.01f)
                back++;

            if (Vector3.Dot(faceNormal, vertNormal) < 0f)
                normalMismatch++;

            samples++;
        }

        float mismatchPct = samples > 0 ? (100f * normalMismatch / samples) : 0f;
        return $"  {viewLabel}: frontFacing={front} backFacing={back} normalMismatch={mismatchPct:F1}%";
    }

    private static float FacingRatio(Mesh mesh, Matrix4x4 toWorld, Vector3 worldUp)
    {
        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;
        Vector3 viewDir = -worldUp.normalized;
        int front = 0;
        int total = 0;
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 p0 = toWorld.MultiplyPoint3x4(verts[tris[i]]);
            Vector3 p1 = toWorld.MultiplyPoint3x4(verts[tris[i + 1]]);
            Vector3 p2 = toWorld.MultiplyPoint3x4(verts[tris[i + 2]]);
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            if (n.sqrMagnitude < 1e-12f)
                continue;
            n.Normalize();
            total++;
            if (Vector3.Dot(n, viewDir) > 0f)
                front++;
        }

        return total > 0 ? (float)front / total : 0f;
    }

    private static Bounds TransformBounds(Bounds local, Matrix4x4 m)
    {
        var corners = new Vector3[8];
        Vector3 c = local.center;
        Vector3 e = local.extents;
        int idx = 0;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sy = -1; sy <= 1; sy += 2)
        for (int sz = -1; sz <= 1; sz += 2)
            corners[idx++] = m.MultiplyPoint3x4(c + Vector3.Scale(e, new Vector3(sx, sy, sz)));

        var b = new Bounds(corners[0], Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            b.Encapsulate(corners[i]);
        return b;
    }

    private static string RootYRange(Transform root, Bounds worldBounds)
    {
        Vector3 min = root.InverseTransformPoint(worldBounds.min);
        Vector3 max = root.InverseTransformPoint(worldBounds.max);
        float yMin = Mathf.Min(min.y, max.y);
        float yMax = Mathf.Max(min.y, max.y);
        return $"[{yMin:F4}, {yMax:F4}]";
    }

    private static string BoundsLine(Bounds b) =>
        $"  center={b.center} size={b.size} min={b.min} max={b.max}";

    private static void DumpHierarchy(Transform t, StringBuilder sb, int depth)
    {
        string indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}{t.name} pos={t.localPosition} rot={t.localRotation.eulerAngles} scale={t.localScale}");
        for (int i = 0; i < t.childCount; i++)
            DumpHierarchy(t.GetChild(i), sb, depth + 1);
    }

    private static string GetPath(Transform t)
    {
        var names = new List<string>();
        while (t != null)
        {
            names.Add(t.name);
            t = t.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static void Write(StringBuilder sb)
    {
        string full = Path.Combine(Directory.GetCurrentDirectory(), OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, sb.ToString());
        Debug.Log($"GEStorageUnit audit written to {OutputPath}\n{sb}");
    }
}
#endif
