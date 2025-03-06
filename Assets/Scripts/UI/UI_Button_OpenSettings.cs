using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opens the <see cref="UI_SettingsMenu"/> singleton GameObject
/// </summary>
public class UI_Button_OpenSettings : MonoBehaviour
{
    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(UI_SettingsMenu.Open);
    }

    private void OnDestroy()
    {
        _button.onClick.RemoveListener(UI_SettingsMenu.Open);
    }
}