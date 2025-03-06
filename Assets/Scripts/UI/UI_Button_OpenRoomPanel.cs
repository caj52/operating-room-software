using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class UI_Button_OpenRoomPanel : MonoBehaviour
{
    private Button button;
    public GameObject RoomsPanel;
    // Start is called before the first frame update
    void Start()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(()=>OnButtonClicked());
    }

    private void OnButtonClicked()
    {
        if (RoomsPanel != null)
        {
            RoomsPanel.SetActive(!RoomsPanel.activeSelf);
        }
    }

  
}
