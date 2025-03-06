using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplenSoft.AssetBundles
{
    [Serializable]
    public class EnvironmentsSettings
    {
        public Dictionary<string, string> EnvironmentKeysByName 
            { get; set; } = new Dictionary<string, string>() { };

        [NonSerialized]
        private static string _settingsPath = Application.dataPath + "/Resources/AssetBundleManager/environmentsSettings.json";

        private static EnvironmentsSettings _instance;

        public static EnvironmentsSettings Get() => _instance ?? Load();

        private static EnvironmentsSettings Load()
        {
#if UNITY_EDITOR
            if (!File.Exists(_settingsPath))
            {
                Log.Write(LogLevel.Log, $"Environment settings does not exist. Creating at {_settingsPath}");
                string text = JsonConvert.SerializeObject(new EnvironmentsSettings());
                File.WriteAllText(_settingsPath, text);
            }
            string textLoad = File.ReadAllText(_settingsPath);
#else
            string textLoad = Resources.Load<TextAsset>("AssetBundleManager/environmentsSettings").text;
#endif
            _instance = JsonConvert.DeserializeObject<EnvironmentsSettings>(textLoad);
            return _instance;
        }
#if UNITY_EDITOR
        public void DisplayEditorFields()
        {
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Settings not available in Play Mode");
                return;
            }


            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Manage your environments on the Unity dashboard, then copy the names and IDs here.");

            if (GUILayout.Button("Manage Environments on Dashboard"))
            {
                Application.OpenURL("https://docs.unity.com/ugs/en-us/manual/overview/manual/service-environments#Managing_environments");
            }

            EditorGUILayout.Space();

            foreach (var item in EnvironmentKeysByName.Keys)
            {
                EditorGUILayout.BeginHorizontal();
                var label = new GUIContent("Name", "The name of the environment from the Unity Services Dashboard");
                EditorGUILayout.LabelField(label, GUILayout.Width(45));
                var newKey = EditorGUILayout.TextField(item)?.Trim();
                if (newKey != item)
                {
                    var value = EnvironmentKeysByName[item];
                    EnvironmentKeysByName.Remove(item);
                    EnvironmentKeysByName[newKey] = value;
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.EndChangeCheck();
                    Save();
                    break;
                }
                EditorGUILayout.Space(10);
                label = new GUIContent("ID", "The environment ID from the Unity Services dashboard");
                EditorGUILayout.LabelField(label, GUILayout.Width(20));
                var newValue = EditorGUILayout.TextField(EnvironmentKeysByName[item])?.Trim();
                if (newValue != EnvironmentKeysByName[item])
                {
                    EnvironmentKeysByName[item] = newValue;
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.EndChangeCheck();
                    Save();
                    break;
                }
                if (GUILayout.Button("Remove", GUILayout.Width(75)))
                {
                    EnvironmentKeysByName.Remove(item);
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.EndChangeCheck();
                    Save();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add New"))
            {
                EnvironmentKeysByName.Add($"New Environment {EnvironmentKeysByName.Count}", null);
                Save();
            }

            if (EditorGUI.EndChangeCheck())
            {
                Save();
            }
        }

        private void Save()
        {
            AssetBundleManagerSettings.EnsureDirectoriesExist();
            string text = JsonConvert.SerializeObject(this);
            try
            {
                File.WriteAllText(_settingsPath, text);
                _instance = null;
            }
            catch
            {
                Debug.LogError("Couldn't write environments settings file");
                throw;
            }
        }
#endif
    }
}
