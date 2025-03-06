using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Events;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine.SceneManagement;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Automatically downloads and 
    /// instantiates prefabs on app start
    /// </summary>
    [ManagedAsset]
    [CreateAssetMenu(
        fileName = "AutoInstantiator",
        menuName = "ScriptableObjects/AutoInstantiator",
        order = 1)]
    public class AutoInstantiator : ScriptableObject
    {
#if UNITY_EDITOR
        [CustomEditor(typeof(AutoInstantiator))]
        private class AutoInstantiator_Inspector : Editor
        {
            private List<GameObject> _existingObjects = new();

            private AutoInstantiator _instance;

            public override void OnInspectorGUI()
            {
                if (_instance == null)
                {
                    _instance = target as AutoInstantiator;
                }

                EditorGUILayout.HelpBox("After creating this " +
                "ScriptableObject, make sure to at least do " +
                "one Dry Run (Tools -> Easy CDN -> Dry Run) " +
                "before testing.", MessageType.Info);

                DrawDefaultInspector();

                if (!string.IsNullOrEmpty(_instance.AssetBundleNameToAdd))
                {
                    _instance.AssetBundleNames.Add(_instance.AssetBundleNameToAdd);
                    _instance.AssetBundleNameToAdd = null;
                    EditorUtility.SetDirty(target);
                }

                _instance.AssetBundleNames = _instance.AssetBundleNames
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();

                for (int i = 0; i < _instance.AssetBundleNames.Count; i++)
                {
                    string assetBundleName = _instance.AssetBundleNames[i];

                    while (_existingObjects.Count < _instance.AssetBundleNames.Count)
                        _existingObjects.Add(null);

                    if (_existingObjects[i] == null)
                    {
                        string[] paths = AssetDatabase
                            .GetAssetPathsFromAssetBundle(assetBundleName);

                        string existingAssetPath = paths.Length == 0 ?
                            null : paths[0];

                        if (existingAssetPath != null)
                        {
                            _existingObjects[i] = (GameObject)AssetDatabase
                                .LoadAssetAtPath(existingAssetPath, typeof(GameObject));
                        }
                        else
                        {
                            throw new Exception("Asset path was null");
                        }
                    }

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(_existingObjects[i].name);
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        _instance.AssetBundleNames.RemoveAt(i);
                        EditorUtility.SetDirty(target);
                        _existingObjects.RemoveAt(i);
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
#endif
        public static UnityEvent OnJobsStarted
        { get; } = new UnityEvent();

        public static UnityEvent<float> OnProgressChanged
        { get; } = new UnityEvent<float>();

        public static UnityEvent OnJobsFinished
        { get; } = new UnityEvent();

        [field: SerializeField,
        AssetBundleReference(typeof(GameObject), "Add Prefab")]
        private string AssetBundleNameToAdd { get; set; }

        [field: SerializeField, HideInInspector]
        private List<string> AssetBundleNames
        { get; set; } = new List<string>();

    
        public static async void OnAppStart()
        {

            // get the scriptable objects
            var task = AssetBundleManager
                .GetAssetBundleNames(typeof(AutoInstantiator));

            await task;
            if (!Application.isPlaying) return;

            if (task.Result.Length == 0) return;

            var tasks =
                new List<Task<AutoInstantiator>>();

            foreach (string assetBundleName in task.Result)
            {
                var assetTask = AssetBundleManager
                    .GetAsset<AutoInstantiator>(assetBundleName);

                tasks.Add(assetTask);
            }

            while (tasks.Any(x => !x.IsCompleted))
            {
                await Task.Yield();
                if (!Application.isPlaying) return;
            }

            // instantiate the asset bundles in the scriptable objects
            var instantiationTasks =
                new List<Task<GameObject>>();

            var progresses =
                new Dictionary<Progress<AssetRetrievalProgress>, float>();

            int totalAssetBundleNames =
                tasks.Sum(x => x.Result.AssetBundleNames.Count);

            OnJobsStarted?.Invoke();

            foreach (var completedTask in tasks)
            {
                completedTask.Result
                .AssetBundleNames.ForEach(bundleName =>
                {
                    var progress =
                        new Progress<AssetRetrievalProgress>();

                    progress.ProgressChanged += (_, p) =>
                    {
                        progresses[progress] = p.Progress;

                        OnProgressChanged?
                            .Invoke(progresses.Values.Sum() /
                            totalAssetBundleNames);
                    };

                    progresses.Add(progress, 0);

                    var assetTask = AssetBundleManager
                        .GetAsset<GameObject>
                        (bundleName, progress);

                    instantiationTasks.Add(assetTask);
                });
            }

            while (instantiationTasks.Any(x => !x.IsCompleted))
            {
                await Task.Yield();
                if (!Application.isPlaying) return;
            }

            instantiationTasks.ForEach(finalTask =>
            {
                var newObj = Instantiate(finalTask.Result);
            });

            OnJobsFinished?.Invoke();
        }
    }
}