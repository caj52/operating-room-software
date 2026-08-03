using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UI_MeasurementButton : MonoBehaviour
{
    public static UnityEvent Toggled = new();

    /// <summary>
    /// Sticky preference for the Measurement Mode toolbar toggle.
    /// Tags must follow this — not Selectable init, which used to force every
    /// measurable on at Start regardless of the toggle.
    /// </summary>
    public static bool MeasurementModeEnabled { get; private set; }

    public Toggle _toggle;
    private List<Measurable> _currentMeasurables = new();

    private void Awake()
    {
        _toggle = GetComponent<Toggle>();

        Selectable.SelectionChanged += UpdateLogic;
        gameObject.SetActive(false);
        MeasurementModeEnabled = _toggle != null && _toggle.isOn;
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateLogic;
    }

    private void UpdateLogic()
    {
        // Clear previous selection's tags so they don't stick after reselection.
        SetCurrentMeasurablesActive(false);

        bool hasMeasurables = Selectable.SelectedSelectables.Count > 0
            && Selectable.SelectedSelectables.Sum(x => x != null ? x.Measurables.Count : 0) > 0;

        gameObject.SetActive(hasMeasurables);
        if (!hasMeasurables)
        {
            _currentMeasurables = null;
            return;
        }

        _currentMeasurables = Selectable.SelectedSelectables
            .Where(x => x != null)
            .SelectMany(x => x.Measurables)
            .Where(m => m != null)
            .ToList();

        SetCurrentMeasurablesActive(MeasurementModeEnabled);
        if (_toggle != null)
            _toggle.SetIsOnWithoutNotify(MeasurementModeEnabled);
    }

    public void OnToggle(bool isOn)
    {
        MeasurementModeEnabled = isOn;
        SetCurrentMeasurablesActive(isOn);
        Toggled?.Invoke();
    }

    void SetCurrentMeasurablesActive(bool active)
    {
        if (_currentMeasurables == null)
            return;
        for (int i = 0; i < _currentMeasurables.Count; i++)
        {
            var item = _currentMeasurables[i];
            if (item != null)
                item.SetActive(active);
        }
    }
}
