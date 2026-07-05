using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Selection click tracing — writes to AssetPipelineDiagnostics.log (same paths as room-load logging).
/// </summary>
public static class SelectionDiagnostics
{
    public static bool Enabled { get; set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Enabled)
            return;

        var probe = new GameObject(nameof(SelectionClickProbe));
        probe.AddComponent<SelectionClickProbe>();
        Object.DontDestroyOnLoad(probe);

        ConfigurationManager.OnRoomLoadComplete.AddListener(AuditLoadedSelectables);
        AssetPipelineDiagnostics.Log("Selection", "diagnostics enabled — log clicks + post-load collider audit");
    }

    public static void LogMouseUp(Selectable s, bool blockedByUi)
    {
        if (!Enabled || s == null)
            return;

        AssetPipelineDiagnostics.Log("Selection",
            $"MOUSE_UP object={s.name} guid={s.GUID} blockedByUi={blockedByUi} " +
            DescribeColliderState(s.gameObject));
    }

    public static void LogSelectBlocked(Selectable s, string reason)
    {
        if (!Enabled || s == null)
            return;

        AssetPipelineDiagnostics.Log("Selection",
            $"SELECT_BLOCKED object={s.name} guid={s.GUID} reason={reason}");
    }

    public static void LogSelectOk(Selectable s)
    {
        if (!Enabled || s == null)
            return;

        AssetPipelineDiagnostics.Log("Selection",
            $"SELECT_OK object={s.name} guid={s.GUID} relatedCount={s.RelatedSelectables?.Count ?? 0}");
    }

    public static void LogClickRaycast(Camera cam)
    {
        if (!Enabled || cam == null)
            return;

        bool overUi = InputHandler.IsPointerOverUIElement();
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, float.MaxValue);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        var sb = new StringBuilder();
        sb.Append($"CLICK overUi={overUi} hits={hits.Length}");
        int n = Mathf.Min(hits.Length, 8);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hits[i];
            Selectable sel = h.collider.GetComponentInParent<Selectable>();
            sb.Append($" | [{i}] dist={h.distance:F2} collider={h.collider.name} enabled={h.collider.enabled} ");
            sb.Append($"go={h.collider.gameObject.name} layer={h.collider.gameObject.layer} ");
            sb.Append($"selectable={(sel != null ? sel.name : "none")} ");
            sb.Append($"selectableOnCollider={(h.collider.GetComponent<Selectable>() != null)}");
        }

        AssetPipelineDiagnostics.Log("Selection", sb.ToString());
    }

    private static void AuditLoadedSelectables()
    {
        if (!Enabled)
            return;

        var unclickable = new List<string>();
        foreach (Selectable s in Object.FindObjectsOfType<Selectable>(true))
        {
            if (s == null || !s.gameObject.activeInHierarchy)
                continue;

            if (!HasEnabledCollider(s.gameObject))
                unclickable.Add($"{s.name} guid={s.GUID} {DescribeColliderState(s.gameObject)}");
        }

        AssetPipelineDiagnostics.Log("Selection",
            $"LOAD_AUDIT selectables={Object.FindObjectsOfType<Selectable>(true).Length} unclickable={unclickable.Count}");
        foreach (string line in unclickable.Take(50))
            AssetPipelineDiagnostics.Log("Selection", $"LOAD_AUDIT_UNCLICKABLE {line}");
        if (unclickable.Count > 50)
            AssetPipelineDiagnostics.Log("Selection", $"LOAD_AUDIT ... and {unclickable.Count - 50} more");
    }

    private static bool HasEnabledCollider(GameObject root)
    {
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c != null && c.enabled && !c.isTrigger)
                return true;
        }
        return false;
    }

    private static string DescribeColliderState(GameObject root)
    {
        Collider[] cols = root.GetComponentsInChildren<Collider>(true);
        int enabled = cols.Count(c => c != null && c.enabled);
        int enabledNonTrigger = cols.Count(c => c != null && c.enabled && !c.isTrigger);
        int disabledConvexMesh = cols.OfType<MeshCollider>().Count(c => c.convex && !c.enabled);
        bool selectableOnRoot = root.GetComponent<Selectable>() != null;
        Collider rootCol = root.GetComponent<Collider>();
        return $"colliders={cols.Length} enabled={enabled} enabledNonTrigger={enabledNonTrigger} " +
               $"disabledConvexMesh={disabledConvexMesh} rootCollider={(rootCol != null ? rootCol.GetType().Name + "/enabled=" + rootCol.enabled : "none")} " +
               $"selectableOnRoot={selectableOnRoot}";
    }

    private sealed class SelectionClickProbe : MonoBehaviour
    {
        private void Update()
        {
            if (!Enabled)
                return;

            if (SceneManager.GetActiveScene().name == "ObjectEditor")
                return;

            if (!Input.GetMouseButtonUp(0))
                return;

            Camera cam = Camera.main;
            if (cam == null && OperatingRoomCamera.LiveCamera != null)
                cam = OperatingRoomCamera.LiveCamera.GetComponent<Camera>();
            if (cam != null)
                LogClickRaycast(cam);
        }
    }
}
