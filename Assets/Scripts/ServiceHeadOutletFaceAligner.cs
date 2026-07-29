using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Service-head HV/LV attachment plates are solid meshes with no outlet cutouts.
/// Outlets/gas/AV hang under AttachPoints, but their face normals often point into
/// the head (elev logs: cover towardCamDot≈+0.7, GasOutletPlate_Red≈−0.4 with
/// cullMode 2), so the red duplex is back-face culled while the grey cover paints
/// the camera-facing view.
///
/// Before elevation (and on Start): hide the solid cover when the attachment has
/// child accessories, and 180°-flip any accessory mesh whose face points opposite
/// the cover outward normal.
/// </summary>
[DisallowMultipleComponent]
public class ServiceHeadOutletFaceAligner : MonoBehaviour
{
    [SerializeField] bool _aligned;
    [SerializeField] bool _coverHandled;

    void Start() => TryAlign();

    public static void AlignAllInAssembly(IList<Selectable> assemblySelectables)
    {
        if (assemblySelectables == null)
            return;

        var seen = new HashSet<int>();
        foreach (var sel in assemblySelectables)
        {
            if (sel == null || !seen.Add(sel.GetInstanceID()))
                continue;

            if (IsBoomHeadAttachment(sel))
            {
                HideCoverIfPopulated(sel);
                continue;
            }

            if (!IsServiceHeadAccessory(sel))
                continue;

            var aligner = sel.GetComponent<ServiceHeadOutletFaceAligner>();
            if (aligner == null)
                aligner = sel.gameObject.AddComponent<ServiceHeadOutletFaceAligner>();
            aligner.TryAlign();
        }
    }

    public void TryAlign()
    {
        var cover = FindAttachmentCoverRenderer();
        if (cover == null)
            return;

        if (!_coverHandled)
        {
            if (cover.enabled)
            {
                cover.enabled = false;
                Debug.Log(
                    $"[ElevOutletDiag] ServiceHeadOutletFaceAligner hid cover on " +
                    $"'{GetAttachmentRootName(cover.transform)}' (no cutouts; was occluding outlets)",
                    this);
            }
            _coverHandled = true;
        }

        if (_aligned)
            return;

        var faceRenderer = FindFaceRenderer();
        if (faceRenderer == null)
        {
            _aligned = true;
            return;
        }

        Vector3 coverOut = AverageWorldNormal(cover);
        Vector3 faceOut = AverageWorldNormal(faceRenderer);
        if (coverOut.sqrMagnitude < 1e-6f || faceOut.sqrMagnitude < 1e-6f)
            return;

        float coverVsFace = Vector3.Dot(faceOut.normalized, coverOut.normalized);
        if (coverVsFace >= 0f)
        {
            _aligned = true;
            Debug.Log(
                $"[ElevOutletDiag] ServiceHeadOutletFaceAligner ok '{name}' " +
                $"coverVsFaceDot={coverVsFace:F3}",
                this);
            return;
        }

        Transform meshRoot = faceRenderer.transform;
        while (meshRoot.parent != null && meshRoot.parent != transform)
            meshRoot = meshRoot.parent;

        meshRoot.Rotate(0f, 180f, 0f, Space.Self);
        _aligned = true;

        float after = Vector3.Dot(AverageWorldNormal(faceRenderer).normalized, coverOut.normalized);
        Debug.Log(
            $"[ElevOutletDiag] ServiceHeadOutletFaceAligner flipped '{name}' " +
            $"coverVsFaceDot {coverVsFace:F3} -> {after:F3}",
            this);
    }

    static bool IsBoomHeadAttachment(Selectable sel) =>
        sel != null && sel.name.StartsWith("BoomHeadAttachment_");

    static bool IsServiceHeadAccessory(Selectable sel)
    {
        if (sel == null || IsBoomHeadAttachment(sel))
            return false;
        Transform t = sel.transform.parent;
        while (t != null)
        {
            if (t.name.StartsWith("BoomHeadAttachment_"))
                return true;
            t = t.parent;
        }
        return false;
    }

    static void HideCoverIfPopulated(Selectable attachment)
    {
        if (!attachment.TryGetComponent<MeshRenderer>(out var cover) || !cover.enabled)
            return;

        bool hasChildAccessory = false;
        foreach (var child in attachment.GetComponentsInChildren<Selectable>(true))
        {
            if (child != null && child != attachment)
            {
                hasChildAccessory = true;
                break;
            }
        }
        if (!hasChildAccessory)
            return;

        cover.enabled = false;
        Debug.Log(
            $"[ElevOutletDiag] ServiceHeadOutletFaceAligner hid cover on '{attachment.name}' " +
            $"(populated attachment; solid plate has no cutouts)",
            attachment);
    }

    MeshRenderer FindAttachmentCoverRenderer()
    {
        Transform t = transform.parent;
        while (t != null)
        {
            if (t.name.StartsWith("BoomHeadAttachment_"))
            {
                if (t.TryGetComponent<MeshRenderer>(out var mr))
                    return mr;
                break;
            }
            t = t.parent;
        }
        return null;
    }

    static string GetAttachmentRootName(Transform t)
    {
        while (t != null)
        {
            if (t.name.StartsWith("BoomHeadAttachment_"))
                return t.name;
            t = t.parent;
        }
        return "?";
    }

    MeshRenderer FindFaceRenderer()
    {
        MeshRenderer idPlate = null;
        MeshRenderer fallback = null;
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null || !mr.enabled)
                continue;
            if (mr.gameObject.name.StartsWith("Sphere"))
                continue;

            if (mr.sharedMaterials != null)
            {
                foreach (var mat in mr.sharedMaterials)
                {
                    if (mat != null && mat.name.StartsWith("GasOutletPlate_"))
                    {
                        idPlate = mr;
                        break;
                    }
                }
            }
            if (idPlate != null)
                break;
            if (fallback == null)
                fallback = mr;
        }
        return idPlate != null ? idPlate : fallback;
    }

    static Vector3 AverageWorldNormal(MeshRenderer mr)
    {
        var mf = mr.GetComponent<MeshFilter>();
        var mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null || mesh.normals == null || mesh.normals.Length == 0)
            return mr.transform.forward;

        var norms = mesh.normals;
        var acc = Vector3.zero;
        int step = Mathf.Max(1, norms.Length / 64);
        int n = 0;
        for (int i = 0; i < norms.Length; i += step)
        {
            acc += mr.transform.TransformDirection(norms[i]);
            n++;
        }
        return n > 0 ? acc / n : mr.transform.forward;
    }
}
