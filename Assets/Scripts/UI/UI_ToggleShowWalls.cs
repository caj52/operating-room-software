using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The UI Toggle Show Walls controls the logic for showing or hiding walls
/// </summary>
public class UI_ToggleShowWalls : MonoBehaviour
{
    Toggle _toggle; // internal reference to the toggle object

    private void Awake()
    {
        _toggle = GetComponent<Toggle>();
        _toggle.isOn = true;

        // lambda delegate for the toggle's value changing. 
        _toggle.onValueChanged.AddListener((x) => {
            RoomBoundary.ToggleAllWallOpaque(_toggle.isOn);
        });
    }
}