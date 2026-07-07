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
        EnsureScaleOptionPresent();

        _dropdown.onValueChanged.AddListener(UpdateGizmoMode);
        Selectable.SelectionChanged += UpdateActiveState;

        gameObject.SetActive(false);
    }

    /// <summary>
    /// Scale was historically added only as a scene override; older CDN Main scenes may still ship 2 options.
    /// </summary>
    private void EnsureScaleOptionPresent()
    {
        if (_dropdown.options.Count >= 3)
            return;

        _dropdown.options.Add(new TMP_Dropdown.OptionData("Scale"));
        _dropdown.RefreshShownValue();
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
        ArticualtionToolScript.gizmoChanged?.Invoke();
    }
}