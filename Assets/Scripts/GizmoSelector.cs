using System;
using TMPro;
using UnityEngine;

public class GizmoSelector : MonoBehaviour
{
    public static GizmoMode CurrentGizmoMode { get; private set; }
    public static EventHandler GizmoModeChanged;
    private TMP_Dropdown _dropdown;

    private void Awake()
    {
        _dropdown = GetComponent<TMP_Dropdown>();

        _dropdown.onValueChanged.AddListener(UpdateGizmoMode);
        Selectable.SelectionChanged += UpdateActiveState;

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        _dropdown.onValueChanged.RemoveListener(UpdateGizmoMode);
        Selectable.SelectionChanged -= UpdateActiveState;
    }

    private void UpdateGizmoMode(int selection)
    {
        SetGizmoMode((GizmoMode)selection);
    }

    private void UpdateActiveState()
    {
        gameObject.SetActive(Selectable.SelectedSelectables.Count > 0);
    }

    public void SetGizmoMode(GizmoMode gizmoMode)
    {
        bool isDifferent = gizmoMode != CurrentGizmoMode;
        CurrentGizmoMode = gizmoMode;
        if (isDifferent) 
        { 
            GizmoModeChanged?.Invoke(this, null);
        }
    }
}