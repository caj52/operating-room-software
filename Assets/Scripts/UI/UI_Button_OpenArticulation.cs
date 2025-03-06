using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_Button_OpenArticulation : MonoBehaviour
{
    private Button button;
    public GameObject ArticulationPanel;
    // Start is called before the first frame update
    void Start()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(() => OnButtonClicked());
    }

    private void OnButtonClicked()
    {
        if (ArticulationPanel != null)
        {
            ArticulationPanel.SetActive(!ArticulationPanel.activeSelf);
        }
    }
}
