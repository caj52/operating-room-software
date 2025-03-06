using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main settings menu opened with a "gear" button in the Main 
/// scene. Singleton
/// </summary>
public class UI_SettingsMenu : MonoBehaviour
{
    public static UI_SettingsMenu 
    Instance { get; private set; }

    [field: SerializeField] 
    private Button ButtonCloseMenu { get; set; }
    
    private void Awake()
    {
        Instance = this;
        ButtonCloseMenu.onClick.AddListener(Close);

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        ButtonCloseMenu.onClick.RemoveListener(Close);
    }

    private static void Close()
    {
        Instance.gameObject.SetActive(false);
    }

    internal static void Open()
    {
        Instance.gameObject.SetActive(true);
    }
}