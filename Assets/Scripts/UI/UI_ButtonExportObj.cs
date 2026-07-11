using TMPro;
using UnityEngine;

/// <summary>
/// Opens the exports hub: Room exports… (per deliverable) or Export folder… when an object is selected.
/// </summary>
public class UI_ButtonExportObj : MonoBehaviour
{
    private TMP_Text _label;

    private void Awake()
    {
        _label = GetComponentInChildren<TMP_Text>(true);
        Selectable.SelectionChanged += UpdateLabel;
        UpdateLabel();
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateLabel;
    }

    private void OnEnable()
    {
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (_label == null)
            _label = GetComponentInChildren<TMP_Text>(true);
        if (_label == null)
            return;

        _label.text = ExportRequest.HasSelection()
            ? "Export folder…"
            : "Room exports…";
    }

    public void ExportObj()
    {
        UI_ExportOptions.Open();
    }

    /// <summary>Legacy prefab wiring — same as ExportObj.</summary>
    public void OpenObjOptions()
    {
        ExportObj();
    }
}
