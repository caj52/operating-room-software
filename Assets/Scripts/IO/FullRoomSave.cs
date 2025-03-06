using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FullRoomSave : MonoBehaviour
{
    private static FullRoomSave Instance { get; set; }

    [Header("Constant UI")]
    public GameObject savePanel;
    public Button b_Save;
    public Button b_Confirm;
    public Button[] b_Cancel;
    public TMP_InputField fileName;
    public string RoomName;
    [Header("Dynamic UI")]
    public TMP_Text header;

    private void Awake()
    {
        Instance = this;
    }

    public static void Close()
    {
        Instance.savePanel.SetActive(false);
    }

    void Start()
    {

        b_Save.onClick.AddListener(() =>
        {
            header.text = "Please Enter Room Name";

            FreeLookCam.Instance.isLocked = true;
            savePanel.SetActive(true);
        });

        b_Confirm.onClick.AddListener(() =>
        {
           
            if (!string.IsNullOrEmpty(fileName.text))
            {
               
                RoomName = Path.Combine(Application.persistentDataPath, fileName.text);
                if (Directory.Exists(RoomName))
                {
                    header.text = "Room Already Exists Try Different Name";
                    header.color = Color.red;
                    fileName.text = "";
                    FreeLookCam.Instance.isLocked = true;
                }
                else
                {
                    Directory.CreateDirectory(RoomName);
                    UI_ObjExportOptions.Open();
                    FreeLookCam.Instance.isLocked = true;
                    
                    fileName.text = "";
                    header.color = Color.black;
                    FreeLookCam.Instance.isLocked = false;
                    savePanel.SetActive(false);
                }
            }
          


        });

        b_Cancel[0].onClick.AddListener(() =>
        {
            savePanel.SetActive(false);
            FreeLookCam.Instance.isLocked = false;
        });

        savePanel.SetActive(false);
    }

    public static string GetRoomPath() => Instance.RoomName;
}
