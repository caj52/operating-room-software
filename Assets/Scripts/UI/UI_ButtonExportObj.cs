using TMPro;
using UnityEngine;

/// <summary>
/// Opens export options. Object mode is 3D-model options only; room mode is the full package.
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
            : "Export room options…";
    }

    public void ExportObj()
    {
        UI_ExportOptions.Open();
    }

    /// <summary>Legacy prefab wiring — same as ExportObj (folder / room options hub).</summary>
    public void OpenObjOptions()
    {
        ExportObj();
    }
}
