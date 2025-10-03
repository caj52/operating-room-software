using System.Collections;
using System.Collections.Generic;
using System.IO;
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

    private string currentSaveMode = ""; // "room" or "configuration"

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
                if (Selectable.SelectedSelectables.Count == 0)
                {
                    header.text = "Save Room";
                    currentSaveMode = "room";
                }
                else
                {
                    header.text = "Save Configuration";
                    currentSaveMode = "configuration";
                }

                FreeLookCam.Instance.isLocked = true;
                savePanel.SetActive(true);
        });

        b_Confirm.onClick.AddListener(() =>
        {
            if (string.IsNullOrWhiteSpace(fileName.text))
            {
                header.text = "Please enter a name";
                header.color = Color.red;
                return;
            }
            
            string sanitizedName = fileName.text.Replace(" ", "_");
            
            if (currentSaveMode == "room")
            {
                CheckAndSaveRoom(sanitizedName);
            }
            else
            {
                CheckAndSaveConfiguration(sanitizedName);
            }
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
    
    private void CheckAndSaveRoom(string name)
    {
        string folder = Application.persistentDataPath + $"/Saved/";
        string configName = ConfigurationManager.Instance.ReplaceInvalidChars(name) + ".json";
        string path = Path.Combine(folder, configName);
        
        if (File.Exists(path))
        {
            // Hide save panel before showing the dialog
            savePanel.SetActive(false);
            PromptOverwrite(name, true);
        }
        else
        {
            CompleteSaveRoom(name);
        }
    }
    
    private void CheckAndSaveConfiguration(string name)
    {
        string folder = Application.persistentDataPath + $"/Saved/Configs/";
        string configName = ConfigurationManager.Instance.ReplaceInvalidChars(name) + ".json";
        string path = Path.Combine(folder, configName);
        
        if (File.Exists(path))
        {
            // Hide save panel before showing the dialog
            savePanel.SetActive(false);
            PromptOverwrite(name, false);
        }
        else
        {
            CompleteSaveConfiguration(name);
        }
    }
    
    private void PromptOverwrite(string name, bool isRoom)
    {
        UI_DialogPrompt.Open(
            $"A {(isRoom ? "room" : "configuration")} named '{name}' already exists.",
            new ButtonAction("Overwrite", () => {
                // Explicitly close the dialog prompt
                UI_DialogPrompt.Close();
                
                // Make sure the save panel is closed
                savePanel.SetActive(false);
                FreeLookCam.Instance.isLocked = false;
                
                if (isRoom)
                    CompleteSaveRoom(name);
                else
                    CompleteSaveConfiguration(name);
            }),
            new ButtonAction("Rename", () => {
                // Explicitly close the dialog prompt
                UI_DialogPrompt.Close();
                
                // Re-show the save panel to allow renaming
                savePanel.SetActive(true);
                FreeLookCam.Instance.isLocked = true;
            }));
    }
    
    private void CompleteSaveRoom(string name)
    {
        // Hide the save panel first
        savePanel.SetActive(false);
        FreeLookCam.Instance.isLocked = false;
        fileName.text = "";
        
        // Call our internal save method that will suppress the ConfigurationManager's dialog
        StartCoroutine(SaveRoomAndShowDialog(name));
    }
    
    private IEnumerator SaveRoomAndShowDialog(string name)
    {
        // Save the room using ConfigurationManager
        ConfigurationManager.Instance.SaveRoom(name);
        
        // Wait a small amount of time to let ConfigurationManager's dialog appear
        yield return new WaitForSeconds(0.1f);
        

    }
    
    private void CompleteSaveConfiguration(string name)
    {
        // Save the configuration
        ConfigurationManager.Instance.SaveConfiguration(name);
        
        // Show a clearer success message
        string folder = Application.persistentDataPath + $"/Saved/Configs/";
        UI_DialogPrompt.Open(
            $"Configuration '{name}' saved successfully!",
            new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = folder),
            new ButtonAction("Done"));
        
        // Clear input field
        fileName.text = "";
        FreeLookCam.Instance.isLocked = false;
        savePanel.SetActive(false);
    }
}
