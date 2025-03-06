//#if UNITY_EDITOR
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using UnityEditor;
//using UnityEditor.SceneManagement;
//using UnityEngine;

//[CustomEditor(typeof(ObjectMenu))]
//public class ObjectMenu_Inspector : Editor
//{
//    private ObjectMenu _component;
//    string topPathResources = "/Resources/Selectables/";

//    void OnEnable()
//    {
//        _component = target as ObjectMenu;
//    }

//    public override void OnInspectorGUI()
//    {
//        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
//        {
//            EditorGUI.BeginChangeCheck();

//            if (GUILayout.Button("Rebuild Resources Paths"))
//            {
//                List<string> validStrings = new List<string>
//                {
//                    "Selectables"
//                };

//                string[] foundPaths = Directory.GetDirectories(
//                    Application.dataPath + topPathResources,
//                    "*",
//                    SearchOption.AllDirectories
//                    );

//                foreach (string str in foundPaths)
//                {
//                    if (str.Contains("Deprecated")) continue;

//                    string corrected = str.Substring(str.IndexOf("Resources")).Replace("\\", "/").Replace("Resources/", "");
//                    validStrings.Add(corrected);
//                }

//                _component.BuiltInFolders = validStrings.ToArray();
//            }

//            if (EditorGUI.EndChangeCheck())
//            {
//                EditorUtility.SetDirty(target);
//            }

//            DrawDefaultInspector();
//        }
//        else
//        {
//            EditorGUILayout.HelpBox("This prefab is only editable in Prefab (Isolation) Mode", MessageType.Warning);
//            EditorGUILayout.HelpBox("Object Menu will populate Selectables automatically from the Resources folder. However, if you have made a new sub-folder, please open the Object Menu and click the 'Rebuild Resources Paths' button.", MessageType.Info);
//        }

//    }
//}

//#endif