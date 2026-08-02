#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws hang AttachmentPoints clearly on ceiling mounts so you can see them
/// even if mesh import is still catching up.
/// </summary>
public static class CeilingMountHangGizmos
{
    // Must use UnityEditor.GizmoType — project defines its own GizmoType (Move/Rotate/Scale).
    [DrawGizmo(UnityEditor.GizmoType.Selected | UnityEditor.GizmoType.Active | UnityEditor.GizmoType.NonSelected)]
    static void Draw(Selectable selectable, UnityEditor.GizmoType gizmoType)
    {
        if (selectable == null || !Measurable.IsCeilingMountName(selectable.name))
            return;

        var aps = selectable.GetComponentsInChildren<AttachmentPoint>(true);
        for (int i = 0; i < aps.Length; i++)
        {
            var ap = aps[i];
            if (ap == null)
                continue;
            if (ap.GetComponentInParent<Selectable>(true) != selectable)
                continue;

            Vector3 p = ap.transform.position;
            Gizmos.color = new Color(0.1f, 0.95f, 0.3f, 0.95f);
            Gizmos.DrawSphere(p, 0.025f);
            Gizmos.DrawLine(p, p + Vector3.down * 0.12f);
            Handles.color = Gizmos.color;
            Handles.Label(p + Vector3.right * 0.03f, $"hang: {ap.name}");
        }
    }
}
#endif
