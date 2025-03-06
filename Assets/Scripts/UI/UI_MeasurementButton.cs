using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UI_MeasurementButton : MonoBehaviour
{
    public static UnityEvent Toggled = new();

    private Toggle _toggle;
    private List<Measurable> _currentMeasurables = new();

    private void Awake()
    {
        _toggle = GetComponent<Toggle>();

        Selectable.SelectionChanged += UpdateLogic;
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateLogic;
    }

    private void UpdateLogic()
    {
        bool active = Selectable.SelectedSelectables.Count > 0 && 
            Selectable.SelectedSelectables.Sum(x => x.Measurables.Count) > 0;

        gameObject.SetActive(active);
        if (active)
        {
            _currentMeasurables = Selectable.SelectedSelectables.SelectMany(x => x.Measurables).ToList();

            _toggle.SetIsOnWithoutNotify(active && 
                _currentMeasurables != null && 
                _currentMeasurables.Count > 0 && 
                _currentMeasurables[0] != null && 
                _currentMeasurables[0].IsActive);
        }
        else
        {
            _currentMeasurables = null;
        }
    }

    public void OnToggle(bool isOn)
    {
        if (_currentMeasurables != null) 
        {
            _currentMeasurables.ForEach(item =>
            {
                item.SetActive(isOn);
            });
        }

        Toggled?.Invoke();
    }
}
