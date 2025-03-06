using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

public class GizmoDimensions : MonoBehaviour
{
    private TMP_Dropdown _dropdown;

    private void Awake()
    {
        _dropdown = GetComponent<TMP_Dropdown>();

        _dropdown.onValueChanged.AddListener(UpdateGizmoDimension);
        Selectable.SelectionChanged += UpdateActiveState;

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateActiveState;
        _dropdown.onValueChanged.RemoveListener(UpdateGizmoDimension);
    }

    void UpdateActiveState()
    {
        if (Selectable.SelectedSelectables.Count > 0)
        {
            if (Selectable.SelectedSelectables.Any(x => x.AllowInverseControl))
            {
                gameObject.SetActive(true);
                Invoke("DelayedSync", 0.05f);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    void DelayedSync()
    {
        _dropdown.value = 0;
        UpdateGizmoDimension(0);
    }

    void UpdateGizmoDimension(int selection)
    {
        foreach (var selectable in Selectable
        .SelectedSelectables)
        {
            GizmoHandler handler = selectable.GetComponent<GizmoHandler>();

            if (handler == null || !selectable.AllowInverseControl) continue;
            if (selection == 0)
            {
                handler._translateGizmo.Gizmo.MoveGizmo.Set2DModeEnabled(false);
            }
            else
            {
                handler._translateGizmo.Gizmo.MoveGizmo.Set2DModeEnabled(true);
            }
        }
    }
}
