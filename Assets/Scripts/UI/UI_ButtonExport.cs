using UnityEngine;

/// <summary>
/// Legacy one-click object export button. Kept for prefab wiring; always hidden —
/// object export lives under Object Exports… (UI_ButtonExportObj → UI_ExportOptions).
/// </summary>
public class UI_ButtonExport : MonoBehaviour
{
    private void Awake()
    {
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        // Prefab/event wiring may re-enable this; keep it out of the toolbar.
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    public void UpdateLabel() { }

    public void UpdateVisibility()
    {
        gameObject.SetActive(false);
    }

    /// <summary>Legacy prefab wiring — opens the object exports hub.</summary>
    public void Export()
    {
        if (!ExportRequest.HasSelection())
            return;

        UI_ExportOptions.Open();
    }

    public void OpenExportOptions()
    {
        UI_ExportOptions.Open();
    }
}
