#if UNITY_EDITOR
using SplenSoft.AssetBundles;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class EzcdnSampleScene : MonoBehaviour
{
    public Text TextConnectionStatus;

    private void Awake()
    {
        var settings = AssetBundleManagerSettings.Get();

        if (settings.UseEditorAssetsIfAble)
        {
            TextConnectionStatus.text = "\"Use Editor Assets\" is true in the settings." +
            "\nThese assets will be instantiated from the local editor cache!\nTo test downloading from the internet, ensure you have build at least once\n (SplenSoft -> Asset Bundles -> Build), uncheck the 'Use Editor Assets' box in settings\nand then enter play mode again.";
        }
        else
        {
            TextConnectionStatus.text = "These assets will be downloaded from the internet or from the local cache. To test a fresh download, use SplenSoft -> Asset Bundles -> Clear Cache";
        }
    }

    [CustomEditor(typeof(EzcdnSampleScene))]
    private class EzcdnSampleScene_Inspector : Editor
    {
        private EzcdnSampleScene _instance;

        public override void OnInspectorGUI()
        {
            if (_instance == null)
            {
                _instance = target as EzcdnSampleScene;
            }

            EditorGUILayout.HelpBox("This sample scene is meant to demonstrate some workflows for getting asset bundles from the CDN. You can press the play button in the editor and press two on-screen buttons to show the CDN in action. Open the console log to see important info showing when the action is happening.", MessageType.Info);

            EditorGUILayout.HelpBox("BEFORE YOU BEGIN:\n\nPlease ensure you have followed the 'Getting Started' guide in the documentation and have supplied your project Id, API key and environment ID in the settings.", MessageType.Warning);

            if (GUILayout.Button("Open Documentation"))
            {
                Application.OpenURL("https://github.com/SplenSoft/ezcdn-public/wiki/");
            }
        }
    }

    public void RemoveTestObjects()
    {
        FindObjectsByType<GameObject>(FindObjectsSortMode.None)
            .Where(x => x.GetComponent<MeshRenderer>() != null)
            .ToList()
            .ForEach(obj => Destroy(obj));
    }
}
#endif