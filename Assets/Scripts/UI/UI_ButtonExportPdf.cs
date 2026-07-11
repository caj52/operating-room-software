using TMPro;
using UnityEngine;

/// <summary>
/// Boom-only toolbar button: one-click elevation PDF for the selected boom assembly.
/// </summary>
public class UI_ButtonExportPdf : MonoBehaviour
{
    private TMP_Text _label;

    private void Awake()
    {
        _label = GetComponentInChildren<TMP_Text>(true);
        Selectable.SelectionChanged += OnSelectionChanged;
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        bool show = Selectable.SelectedSelectables.Count > 0 &&
            Selectable.SelectedSelectables[0].IsArmAssembly;
        gameObject.SetActive(show);
        if (show && _label != null)
            _label.text = "Export boom elevation PDF";
    }

    public void ExportPdf()
    {
        if (!ExportRequest.SelectionIsArmAssembly())
            return;

        ExportOrchestrator.Run(new ExportRequest
        {
            Scope = ExportScope.SelectedObject,
            IncludeObj = false,
            IncludeElevations = true,
            IncludeProposal = false,
            IncludeSnapshots = false,
            ElevationMode = ElevationExportMode.PerAssembly,
            ObjOptions = ObjExportOptions.CreateDefaults()
        });
    }
}
