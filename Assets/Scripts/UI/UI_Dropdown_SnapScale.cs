using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class UI_Dropdown_SnapScale : MonoBehaviour
{
    private TMP_Dropdown _dropdown;

    private void Awake()
    {
        _dropdown = GetComponent<TMP_Dropdown>();

        _dropdown.onValueChanged.AddListener(UpdateSnapScale);
        Selectable.SelectionChanged += UpdateSnapScale;

        SetActive();
        UI_ToggleSnapping.SnappingToggled.AddListener(SetActive);
    }

    private void OnDestroy()
    {
        _dropdown.onValueChanged.RemoveListener(UpdateSnapScale);
        Selectable.SelectionChanged -= UpdateSnapScale;

        UI_ToggleSnapping.SnappingToggled.RemoveListener(SetActive);
    }

    void SetActive()
    {
        if (UI_ToggleSnapping.SnappingEnabled)
        {
            gameObject.SetActive(true);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    void UpdateSnapScale()
    {
        UpdateSnapScale(_dropdown.value);
    }

    async void UpdateSnapScale(int selection)
    {
        if(Selectable.SelectedSelectables.Count == 0) 
            return;

        if (!Selectable.SelectedSelectables
        .Any(x => x.GetComponent<GizmoHandler>() != null))
            return;


        await Task.Yield();
        switch (selection)
        {
            case 0:
                SetSnaps(0.0127f);
                break;
            case 1:
                SetSnaps(0.0254f);
                break;
            case 2:
                SetSnaps(0.0762f);
                break;
            case 3:
                SetSnaps(0.1524f);
                break;
            case 4:
                SetSnaps(0.3048f);
                break;
        }
    }

    void SetSnaps(float snap)
    {
        foreach (var selectable in  Selectable.SelectedSelectables) 
        {
            GizmoHandler currentHandler = selectable.GetComponent<GizmoHandler>();

            currentHandler._translateGizmo.Gizmo.MoveGizmo.Settings3D.SetXSnapStep(snap);
            currentHandler._translateGizmo.Gizmo.MoveGizmo.Settings3D.SetYSnapStep(snap);
            currentHandler._translateGizmo.Gizmo.MoveGizmo.Settings3D.SetZSnapStep(snap);

            currentHandler._translateGizmo.Gizmo.MoveGizmo.Settings2D.SetXSnapStep(snap);
            currentHandler._translateGizmo.Gizmo.MoveGizmo.Settings2D.SetYSnapStep(snap);

            //currentHandler._rotateGizmo.Gizmo.RotationGizmo.Settings3D.SetAxisSnapStep(0, snap);
            //currentHandler._rotateGizmo.Gizmo.RotationGizmo.Settings3D.SetAxisSnapStep(1, snap);
            //currentHandler._rotateGizmo.Gizmo.RotationGizmo.Settings3D.SetAxisSnapStep(2, snap);
        }
    }
}
