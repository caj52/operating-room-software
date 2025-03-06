using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The UI Toggle Light Switch controls the logic for the UI Light Toggle
/// It only shows when a Light Factory containing Selectable has been selected
/// </summary>
public class UI_ToggleLightSwitch : MonoBehaviour
{
    Toggle _toggle; // internal reference to the toggle object
    LightFactory _selectedLight = null; // internal reference to the current selected light, to reduce getcomponent calls

    private void Awake()
    {
        _toggle = GetComponent<Toggle>();

        Selectable.SelectionChanged += UpdateActiveState;

        gameObject.SetActive(false); // turn off the UI element on awake, as nothing is selected

        // lambda delegate for the toggle's value changing. 
        // This is done instead of the Editor to avoid an edge-case of the value applying to subsequently clicked lights
        _toggle.onValueChanged.AddListener(UpdateLightState);
    }

    private void UpdateLightState(bool isOn)
    {
        if (isOn != _selectedLight.isOn())
        {
            _selectedLight.SwitchLight();
        }
    }

    private void UpdateActiveState()
    {
        // store result of the selectable & light checks for multiple uses
        bool active = Selectable.SelectedSelectables.Count > 0 && 
            Selectable.SelectedSelectables.Any(x => x.GetComponent<LightFactory>() != null);

        // if there is a current selectable and it is a light, we display the UI
        gameObject.SetActive(active); 

        if (active)
        {
            Selectable.SelectedSelectables.ForEach(x =>
            {
                if (x.GetComponent<LightFactory>() != null)
                {
                    _selectedLight = x.gameObject.GetComponent<LightFactory>(); // store the value for reuse
                    _toggle.isOn = _selectedLight.isOn(); // set the toggle to match the current state of the light (ON/OFF)
                }
            });
        }
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateActiveState;
        _toggle.onValueChanged.RemoveListener(UpdateLightState);
    }
}