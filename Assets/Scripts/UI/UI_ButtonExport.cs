using TMPro;
using UnityEngine;

/// <summary>
/// Toolbar Export button — room package, or selected-object 3D model only.
/// </summary>
public class UI_ButtonExport : MonoBehaviour
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

    public void UpdateLabel()
    {
        if (_label == null)
            _label = GetComponentInChildren<TMP_Text>(true);
        if (_label == null)
            return;

        _label.text = ExportRequest.HasSelection()
            ? "Export object 3D model"
            : "Export room";
    }

    /// <summary>One-click export for the current scope (room package or selected 3D model).</summary>
    public void Export()
    {
        ExportOrchestrator.Run(UI_ExportOptions.GetRequestForToolbar());
    }

    public void OpenExportOptions()
    {
        UI_ExportOptions.Open();
    }
}
