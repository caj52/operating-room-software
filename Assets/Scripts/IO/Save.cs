using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Save : MonoBehaviour
{
    private static Save Instance { get; set; }

    [Header("Constant UI")]
    public GameObject savePanel;
    public Button b_Save;
    public Button b_Confirm;
    public Button[] b_Cancel;
    public TMP_InputField fileName;

    [Header("Dynamic UI")]
    public TMP_Text header;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {

    }

    public static void Close()
    {
        Instance.savePanel.SetActive(false);
    }

    void Start()
    {
        UI_LoadingScreensSwitcher LoadingScreen = FindObjectOfType<UI_LoadingScreensSwitcher>(true);
        LoadingScreen.itemLoadingScreen.SetActive(true);
        LoadingScreen.mainLoadingScreen.SetActive(false);

        b_Save.onClick.AddListener(() =>
        {
            if (string.IsNullOrEmpty(FullRoomSave.GetRoomPath()))
            {
                UI_DialogPrompt.Open(
                         $"Please Export Room First",
                          new ButtonAction("OK"));
            }
            else
            {
                if (Selectable.SelectedSelectables.Count == 0)
                {
                    header.text = "Save Room";
                }
                else
                {
                    header.text = "Save Configuration";
                }

                FreeLookCam.Instance.isLocked = true;
                savePanel.SetActive(true);
            }

        });

        b_Confirm.onClick.AddListener(() =>
        {
            if (Selectable.SelectedSelectables.Count == 0)
            {
                ConfigurationManager.Instance.SaveRoom(fileName.text.Replace(" ", "_"));
            }
            else
            {
                ConfigurationManager.Instance.SaveConfiguration(fileName.text.Replace(" ", "_"));
            }

            fileName.text = "";
            FreeLookCam.Instance.isLocked = false;
            savePanel.SetActive(false);
        });

        foreach (Button b in b_Cancel)
        {
            b.onClick.AddListener(() =>
            {
                savePanel.SetActive(false);
                FreeLookCam.Instance.isLocked = false;
            });
        }

        savePanel.SetActive(false);
    }
}
