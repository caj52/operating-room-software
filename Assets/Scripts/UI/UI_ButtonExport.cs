using TMPro;
using UnityEngine;

/// <summary>
/// Toolbar button: one-click export for the selected object only.
/// Hidden when nothing is selected (room exports live under Room exports…).
/// </summary>
public class UI_ButtonExport : MonoBehaviour
{
    private TMP_Text _label;

    private void Awake()
    {
        _label = GetComponentInChildren<TMP_Text>(true);
        Selectable.SelectionChanged += UpdateVisibility;
        UpdateVisibility();
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateVisibility;
    }

    private void OnEnable()
    {
        UpdateVisibility();
    }

    public void UpdateLabel() => UpdateVisibility();

    private void UpdateVisibility()
    {
        bool hasSelection = ExportRequest.HasSelection();
        gameObject.SetActive(hasSelection);

        if (!hasSelection)
            return;

        if (_label == null)
            _label = GetComponentInChildren<TMP_Text>(true);
        if (_label != null)
            _label.text = "Export object 3D model";
    }

    /// <summary>One-click export of the selected object's 3D model.</summary>
    public void Export()
    {
        if (!ExportRequest.HasSelection())
            return;

        ExportOrchestrator.Run(ExportRequest.CreateDefaultsForSelection());
    }

    public void OpenExportOptions()
    {
        UI_ExportOptions.Open();
    }
}
