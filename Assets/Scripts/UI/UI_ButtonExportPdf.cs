using TMPro;
using UnityEngine;

/// <summary>
/// Legacy toolbar entry for boom elevation PDF. Hidden — use Object Exports →
/// Elevation PDF when a boom is selected.
/// </summary>
public class UI_ButtonExportPdf : MonoBehaviour
{
    private void Awake()
    {
        gameObject.SetActive(false);
    }

    public void ExportPdf()
    {
        if (!ExportRequest.SelectionIsArmAssembly())
            return;

        UI_ExportOptions.Open();
    }
}
