using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Logs outlet/cover facing state before elevation <c>camera.Render()</c>.
/// Facing must already be correct from attach/load (<see cref="AttachmentPoint"/>).
/// </summary>
public static class ElevationOutletCaptureDiagnostics
{
    public static void LogBeforeRender(IList<Selectable> assemblySelectables, Camera camera)
    {
        return;

        var sb = new StringBuilder(2048);
        sb.AppendLine("[ElevOutletDiag] ---- before camera.Render ----");
        sb.AppendLine(
            $"cam={camera.name} cullMask={camera.cullingMask} pos={camera.transform.position} " +
            $"fwd={camera.transform.forward} orthoSize={camera.orthographicSize}");

        int selectableLayer = LayerMask.NameToLayer("Selectable");
        int reported = 0;
        int culledPlates = 0;

        foreach (var sel in assemblySelectables)
        {
            if (sel == null)
                continue;

            string meta = sel.MetaData.Name ?? "";
            bool interesting =
                meta.IndexOf("Outlet", System.StringComparison.OrdinalIgnoreCase) >= 0
                || meta.IndexOf("Gas", System.StringComparison.OrdinalIgnoreCase) >= 0
                || sel.name.IndexOf("Outlet", System.StringComparison.OrdinalIgnoreCase) >= 0
                || sel.name.IndexOf("Gas", System.StringComparison.OrdinalIgnoreCase) >= 0
                || sel.name.StartsWith("BoomHeadAttachment_");
            if (!interesting)
                continue;

            sb.AppendLine(
                $"SEL name={sel.name} meta={meta} active={sel.gameObject.activeInHierarchy} " +
                $"layer={sel.gameObject.layer}({LayerMask.LayerToName(sel.gameObject.layer)})");

            MeshRenderer cover = null;
            if (sel.name.StartsWith("BoomHeadAttachment_") &&
                sel.TryGetComponent(out cover))
            {
                sb.AppendLine(
                    $"  COVER enabled={cover.enabled} mats=[{(cover.sharedMaterial != null ? cover.sharedMaterial.name : "null")}]");
            }

            Vector3 coverOut = default;
            bool haveCoverOut = TryAttachmentCoverNormal(sel.transform, out coverOut);

            foreach (var mr in sel.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null)
                    continue;

                var mf = mr.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                var b = mr.bounds;
                Vector3 toCam = (camera.transform.position - b.center).normalized;
                float towardCam = 0f;
                Vector3 avgNormal = default;
                if (mesh != null && mesh.normals != null && mesh.normals.Length > 0)
                {
                    var acc = Vector3.zero;
                    var norms = mesh.normals;
                    int step = Mathf.Max(1, norms.Length / 64);
                    int n = 0;
                    for (int i = 0; i < norms.Length; i += step)
                    {
                        acc += mr.transform.TransformDirection(norms[i]);
                        n++;
                    }
                    if (n > 0)
                    {
                        avgNormal = acc / n;
                        towardCam = Vector3.Dot(avgNormal.normalized, toCam);
                    }
                }

                float coverVsFace = haveCoverOut && avgNormal.sqrMagnitude > 1e-6f
                    ? Vector3.Dot(avgNormal.normalized, coverOut.normalized)
                    : float.NaN;

                bool layerVisible = (camera.cullingMask & (1 << mr.gameObject.layer)) != 0;
                string mats = "";
                int cull = -1;
                Color baseCol = default;
                bool isGasPlate = false;
                if (mr.sharedMaterials != null)
                {
                    for (int i = 0; i < mr.sharedMaterials.Length; i++)
                    {
                        var mat = mr.sharedMaterials[i];
                        if (mat == null)
                        {
                            mats += "[null]";
                            continue;
                        }
                        mats += mat.name;
                        if (mat.name.StartsWith("GasOutletPlate_", System.StringComparison.Ordinal))
                            isGasPlate = true;
                        if (mat.HasProperty("_Cull"))
                            cull = Mathf.RoundToInt(mat.GetFloat("_Cull"));
                        if (mat.HasProperty("_BaseColor"))
                            baseCol = mat.GetColor("_BaseColor");
                        if (i + 1 < mr.sharedMaterials.Length)
                            mats += "|";
                    }
                }

                if (isGasPlate && mr.enabled && towardCam < 0f && cull == 2)
                    culledPlates++;

                sb.AppendLine(
                    $"  MR go={mr.gameObject.name} path={GetPath(mr.transform)} " +
                    $"active={mr.gameObject.activeInHierarchy} enabled={mr.enabled} " +
                    $"layer={mr.gameObject.layer}({LayerMask.LayerToName(mr.gameObject.layer)}) " +
                    $"layerInCull={layerVisible} " +
                    $"mats=[{mats}] cullMode={cull} baseColor={baseCol} " +
                    $"boundsSize={b.size} towardCamDot={towardCam:F3} " +
                    $"coverVsFaceDot={(float.IsNaN(coverVsFace) ? "n/a" : coverVsFace.ToString("F3"))} " +
                    $"mesh={(mesh != null ? mesh.name : "null")} verts={(mesh != null ? mesh.vertexCount : 0)}");
                reported++;
            }
        }

        sb.AppendLine(
            $"[ElevOutletDiag] reportedMeshRenderers={reported} selectableLayer={selectableLayer} " +
            $"gasPlatesFacingAwayWithBackCull={culledPlates}");
        if (culledPlates > 0)
            sb.AppendLine(
                "[ElevOutletDiag] FAIL: GasOutletPlate(s) face away from camera with Cull Back — will be missing in PDF");
        Debug.Log(sb.ToString());
    }

    static bool TryAttachmentCoverNormal(Transform from, out Vector3 coverOut)
    {
        coverOut = default;
        Transform t = from;
        while (t != null)
        {
            if (t.name.StartsWith("BoomHeadAttachment_") &&
                t.TryGetComponent<MeshRenderer>(out var cover) &&
                t.TryGetComponent<MeshFilter>(out var mf) &&
                mf.sharedMesh != null &&
                mf.sharedMesh.normals != null &&
                mf.sharedMesh.normals.Length > 0)
            {
                var norms = mf.sharedMesh.normals;
                var acc = Vector3.zero;
                int step = Mathf.Max(1, norms.Length / 64);
                int n = 0;
                for (int i = 0; i < norms.Length; i += step)
                {
                    acc += cover.transform.TransformDirection(norms[i]);
                    n++;
                }
                if (n > 0)
                {
                    coverOut = acc / n;
                    return coverOut.sqrMagnitude > 1e-6f;
                }
            }
            t = t.parent;
        }
        return false;
    }

    static string GetPath(Transform t)
    {
        var parts = new List<string>(8);
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
