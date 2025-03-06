using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Controls the settings for EZ-CDN
    /// </summary>
    [Serializable]
    public class AssetBundleManagerSettings
    {
        [NonSerialized]
        private const string _apiKeyString = "Unity_CCD_API_Key";
        [NonSerialized]
        private static AssetBundleManagerSettings _instance;
        [NonSerialized]
        private List<string> _allUnityObjectTypes;
        [NonSerialized]
        private static int _selectedIndex;
        [NonSerialized]
        private static string _filter;

        public bool TypesInitialized { get; set; }

        public List<TypeAssembly> Types 
        { get; set; } = new List<TypeAssembly>();

        /// <summary>
        /// What level of debug logging to output to the console
        /// </summary>
        public LogLevel LogLevel { get; set; }

        /// <summary>
        /// Uses a one-number Major semantic version. 
        /// Breaking changes should increase the version. 
        /// Games that were published with a lower major 
        /// version will NOT download updates from any other
        /// version. A good rule of thumb is this: If your 
        /// major version changes in your game, this should 
        /// probably change to, as breaking code changes 
        /// can affect asset bundles
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// Includes a local copy of the latest assetbundles
        /// on Build. Will automatically trigger an assetbundle 
        /// build before an app build and copy them into
        /// Assets/StreamingAssets/AssetBundles. If this is
        /// true, the app will use the local copy when there is 
        /// no internet connection or when the cloud copy of
        /// the assetbundle is not available. (Cached assets
        /// are still used first)
        /// </summary>
        public bool KeepLocalCopy { get; set; }

        /// <summary>
        /// Unity CCD has a tendency to fail miserably when
        /// there is a lot of concurrent downloads from a single IP.
        /// See forum posts here:
        /// <see href="https://forum.unity.com/threads/windows-platform-failed-to-download-from-ccd-properly.1270739/"/>
        /// <see href="https://forum.unity.com/threads/max-concurrent-web-requests-setting-best-practices-for-mobile.1133422/"/>
        /// It is recommended to leave this at default (3),
        /// but advanced users can mess around with it
        /// </summary>
        public int MaxConcurrentDownloads { get; set; } = 3;

        /// <summary>
        /// Loads the existing settings from the 
        /// Resources folder or creates new settings 
        /// if the file does not exist
        /// </summary>
        /// <returns></returns>
        public static AssetBundleManagerSettings Get()
        {
            return _instance ??= Load();
        }

        private static string SettingsResourcePath => 
            Application.dataPath + "/Resources/AssetBundleManager/settings.json";

        /// <summary>
        /// Ensures proper folders exist in /Assets/ to store settings files
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(Application.dataPath + "/Resources/"))
            {
                Directory.CreateDirectory(Application.dataPath + "/Resources/");
            }

            if (!Directory.Exists(Application.dataPath + "/Resources/AssetBundleManager/"))
            {
                Directory.CreateDirectory(Application.dataPath + "/Resources/AssetBundleManager/");
            }
        }

        private static AssetBundleManagerSettings Load()
        {
            try
            {
#if UNITY_EDITOR
                if (!File.Exists(SettingsResourcePath)) 
                {
                    Log.Write(LogLevel.Log, $"Asset bundle manager settings file does not exist. Creating at {SettingsResourcePath}");
                    var newSettings = new AssetBundleManagerSettings();
                    var newSettingsJsonString = JsonConvert.SerializeObject(newSettings);
                    EnsureDirectoriesExist();

                    File.WriteAllText(SettingsResourcePath, newSettingsJsonString);
                    return newSettings;
                }
                string text = File.ReadAllText(SettingsResourcePath);
#else
                string text = Resources.Load<TextAsset>("AssetBundleManager/settings").text;
#endif
                var settings = JsonConvert.DeserializeObject<AssetBundleManagerSettings>(text);
                if (settings == null)
                {
                    Debug.LogError("Settings was null");
                }
                _instance = settings;
                return _instance;
            }
            catch
            {
#if UNITY_EDITOR
                var newSettings = new AssetBundleManagerSettings();
                var newSettingsJsonString = JsonConvert.SerializeObject(newSettings);
                EnsureDirectoriesExist();
                File.WriteAllText(SettingsResourcePath, newSettingsJsonString);
#endif
                Debug.LogError("Could not load AssetBundleManager settings");
                throw;
            }
        }

#if UNITY_EDITOR
        public void Save()
        {
            try
            {
                BuildTargetNames.Clear();
                if (!TypesInitialized)
                {
                    Types.Clear();
                    Types = new List<TypeAssembly>()
                    {
                        new TypeAssembly("UnityEngine.Sprite", "UnityEngine.CoreModule"),
                        new TypeAssembly("UnityEngine.Material", "UnityEngine.CoreModule"),
                        new TypeAssembly("UnityEngine.Texture2D", "UnityEngine.CoreModule"),
                        new TypeAssembly("UnityEditor.SceneAsset", "UnityEditor.CoreModule"),
                        new TypeAssembly("UnityEngine.Font", "UnityEngine.TextRenderingModule"),
                        new TypeAssembly("UnityEngine.GameObject", "UnityEngine.CoreModule"),
                        new TypeAssembly("UnityEngine.Animator", "UnityEngine.AnimationModule"),
                        new TypeAssembly("UnityEngine.AnimatorOverrideController", "UnityEngine.AnimationModule"),
                        new TypeAssembly("UnityEngine.Animation", "UnityEngine.AnimationModule"),
                        new TypeAssembly("UnityEngine.AnimationClip", "UnityEngine.AnimationModule"),
                    };
                    TypesInitialized = true;
                }
                
                var targets = (BuildTarget[])Enum.GetValues(typeof(BuildTarget));
                foreach (var target in targets)
                {
                    BuildTargetNames[(int)target] = target.ToString();
                }
                var newSettingsJsonString = JsonConvert.SerializeObject(this);
                EnsureDirectoriesExist();
                File.WriteAllText(SettingsResourcePath, newSettingsJsonString);
                _instance = null;
            }
            catch
            {
                Debug.LogError("Could not save AssetBundleManager settings");
                throw;
            }
        }

        /// <summary>
        /// Sensitive info - stored in PlayerPrefs in the editor 
        /// and ONLY used in the editor - never sent to a runtime build
        /// </summary>
        public string GetApiKey()
        { 

                if (!PlayerPrefs.HasKey(_apiKeyString)) return null;
                return PlayerPrefs.GetString(_apiKeyString);
        }
#endif

        public string UnityProjectId { get; set; }
        public string ActiveEnvironmentId { get; set; }
        public bool UseEditorAssetsIfAble { get; set; } = true;

        public Dictionary<string, EnvironmentVariables> VariablesByEnvironment 
            { get; set; } = new Dictionary<string, EnvironmentVariables>();

        public Dictionary<RuntimePlatform, int> BuildTargetsByPlatform =>
            GetVariablesFromEnvironment(ActiveEnvironmentId)?.BuildTargetsByPlatform;

        public Dictionary<int, string> BucketsByBuildTarget =>
            GetVariablesFromEnvironment(ActiveEnvironmentId)?.BucketsByBuildTarget;

        public Dictionary<RuntimePlatform, int> EditorListOrderBuildTargetsByPlatform =>
        GetVariablesFromEnvironment(ActiveEnvironmentId)?.EditorListOrderBuildTargetsByPlatform;


        public Dictionary<int, string> BuildTargetNames 
            { get; set; } = new Dictionary<int, string>();

        public string GetPlatformBucketId(RuntimePlatform platform)
        {
            if (BuildTargetsByPlatform.TryGetValue(platform, out var buildTarget))
            {
                if (BucketsByBuildTarget.TryGetValue(buildTarget, out string bucketId))
                {
                    return bucketId;
                }
            }

            throw new Exception($"Bucket ID and/or build target not properly configured for runtime platform {platform}. Please stop the program and go to SplenSoft -> AssetBundles -> Settings and make sure everything is correct");
        }

        //https://[projectid].client-api.unity3dusercontent.com/client_api/v1/environments/{environmentname}/buckets/{bucketid}/release_by_badge/{badgename}/entry_by_path/content/

        private string GetBucketURL() => $"https://{UnityProjectId?.Trim()}.client-api.unity3dusercontent.com/client_api/v1/environments/{ActiveEnvironmentId?.Trim()}/buckets/{GetPlatformBucketId(Application.platform)?.Trim()}/";

        /// <returns>A client-side API url for Unity CCD to retrieve assets</returns>
        public string GetAssetBundleURL() => $"{GetBucketURL()}release_by_badge/{Version}/entry_by_path/content/?path=";

        private EnvironmentVariables GetVariablesFromEnvironment(string environmentId)
        {
            if (string.IsNullOrWhiteSpace(environmentId)) return null;
            if (!VariablesByEnvironment.TryGetValue(environmentId, out EnvironmentVariables value))
            {
                value = new EnvironmentVariables();
                VariablesByEnvironment[environmentId] = value;
            }
            return value;
        }

        [Serializable]
        public class EnvironmentVariables
        {
            public Dictionary<RuntimePlatform, int> BuildTargetsByPlatform = new Dictionary<RuntimePlatform, int>();
            public Dictionary<RuntimePlatform, int> EditorListOrderBuildTargetsByPlatform = new Dictionary<RuntimePlatform, int>();
            public Dictionary<int, string> BucketsByBuildTarget = new Dictionary<int, string>();
        } 

        [NonSerialized]
        Vector2 _scrollPos;
#if UNITY_EDITOR
        public void DisplayEditorFields()
        {
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Settings not available in Play Mode");
                return;
            }

            EditorGUI.BeginChangeCheck();
            var label = new GUIContent("Project ID", "Project ID from the UCD dashboard");
            UnityProjectId = EditorGUILayout.TextField(label, UnityProjectId)?.Trim();

            label = new GUIContent("API Key", "API key from the UCD dashboard");
            var newKey = EditorGUILayout.PasswordField(label, GetApiKey())?.Trim();

            if (newKey != GetApiKey())
            {
                PlayerPrefs.SetString(_apiKeyString, newKey);
            }

            label = new GUIContent("Version", "Apps published with a specific version will not download asset bundles from any other version");
            Version = EditorGUILayout.IntField(label, Version);

            label = new GUIContent("Log Level", "Minimum severity for logging to the console");
            LogLevel = (LogLevel)EditorGUILayout.EnumPopup(label, LogLevel);

            label = new GUIContent("Use Editor Assets", "Toggle this off to test downloading asset bundles from the CDN in the editor");
            UseEditorAssetsIfAble = EditorGUILayout.Toggle(label, UseEditorAssetsIfAble);

            label = new GUIContent("Include Local Copy", "Builds asset bundles and copies them to StreamAssets before app build. Useful as a fallback if game is played without internet before a local cache is made, but will increase app size.");
            KeepLocalCopy = EditorGUILayout.Toggle(label, KeepLocalCopy);

            label = new GUIContent("Max Concurrent Downloads", "Maximum concurrent downloads to the Unity CCD. Too many of these can cause bad things to to happen. Recommended to leave it at default (3)");
            MaxConcurrentDownloads = EditorGUILayout.IntField(label, MaxConcurrentDownloads);

            var environmentsSettings = EnvironmentsSettings.Get();

            List<string> environmentNameList = 
                                    environmentsSettings
                                    .EnvironmentKeysByName
                                    .Keys
                                    .ToList();

            if (string.IsNullOrWhiteSpace(ActiveEnvironmentId) && 
                environmentsSettings.EnvironmentKeysByName.Count > 0)
            {
                ActiveEnvironmentId = environmentsSettings.EnvironmentKeysByName[environmentNameList[0]];
            }

            if (!string.IsNullOrWhiteSpace(ActiveEnvironmentId) && (environmentNameList.Count == 0 || 
                !environmentsSettings.EnvironmentKeysByName.ContainsValue(ActiveEnvironmentId))) 
            {
                ActiveEnvironmentId = null;
            }

            if (environmentNameList.Count > 0 && !string.IsNullOrWhiteSpace(ActiveEnvironmentId))
            {
                int index = 0;
                if (ActiveEnvironmentId != null)
                {
                    var possibleIndex = environmentNameList
                                        .IndexOf(ActiveEnvironmentId);

                    if (possibleIndex != -1)
                    {
                        index = possibleIndex;
                    }
                    else
                    {
                        ActiveEnvironmentId = null;
                    }
                }

                label = new GUIContent("Environment", "The current environment for the editor");
                index = EditorGUILayout.Popup(label, index, environmentNameList.ToArray());
                ActiveEnvironmentId = environmentsSettings.EnvironmentKeysByName[environmentNameList[index]];
            }
            else if (environmentNameList.Count == 0 || string.IsNullOrWhiteSpace(ActiveEnvironmentId))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("You have not created any environments, or an environment is missing its key.");
                EditorGUILayout.LabelField("Edit your environments by clicking the button below.");
                EditorGUILayout.Space();
            }

            if (GUILayout.Button("Manage Environments"))
            {
                EnvironmentSettingsWindow.ShowWindow();
            }

            if (!string.IsNullOrWhiteSpace(ActiveEnvironmentId) && environmentNameList.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Bucket IDs");

                if (GUILayout.Button("Generate Buckets"))
                {
                    GenerateBuckets();
                }

                string toggleGuid = "89244fa6-69b1-4877-9611-e57a46a86de1";
                bool oldValue = PlayerPrefs.GetInt(toggleGuid) == 1;

                label = new GUIContent(
                    $"Manually Edit Ids", 
                    $"Toggle this if you have existing Bucket IDs or want to use your own bucket system"
                );

                bool manuallyEditBucketIds = 
                        EditorGUILayout.Toggle(label, oldValue);

                if (manuallyEditBucketIds != oldValue) 
                {
                    PlayerPrefs.SetInt(
                        toggleGuid, 
                        manuallyEditBucketIds ? 1 : 0
                    );
                }

                if (BuildTargetsByPlatform.Count > 0) 
                {
                    bool needsSave = false;
                    foreach (var kvp in BuildTargetsByPlatform)
                    {
                        if (!EditorListOrderBuildTargetsByPlatform.ContainsKey(kvp.Key))
                        {
                            int i = 0;
                            var values = EditorListOrderBuildTargetsByPlatform
                                .Values
                                .ToList();

                            while (values.Contains(i++));

                            EditorListOrderBuildTargetsByPlatform[kvp.Key] = i;
                            needsSave = true;
                        }
                    }

                    if (needsSave)
                    {
                        Save();
                        return;
                    }

                    EditorGUI.indentLevel++;
                    _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

                    List<KeyValuePair<RuntimePlatform, int>> sortedList = BuildTargetsByPlatform
                        .OrderBy(kvp => EditorListOrderBuildTargetsByPlatform[kvp.Key])
                        .ToList();

                    foreach (var kvp in sortedList)
                    {
                        EditorGUILayout.BeginHorizontal();
                        var platform = kvp.Key;
                        

                        List<RuntimePlatform> selections = ValidRuntimePlatforms()
                            .Concat(new List<RuntimePlatform>() { platform })
                            .ToList();

                        var rpStrings = selections
                            .Select(x => x.ToString())
                            .ToArray();

                        int existingIndex = Array.IndexOf(rpStrings, platform.ToString());
                        if (existingIndex == -1)
                        {
                            // runtime no longer supported by Unity
                            existingIndex = 0;
                        }

                        label = new GUIContent($"Platform", $"Runtime environment for this app");
                        EditorGUILayout.LabelField(label, GUILayout.Width(100));
                        int newIndex = EditorGUILayout.Popup(existingIndex, rpStrings);

                        if (newIndex != existingIndex) 
                        {
                            var newPlat = Enum.Parse<RuntimePlatform>(rpStrings[newIndex]);
                            BuildTargetsByPlatform.Add(newPlat, kvp.Value);
                            BuildTargetsByPlatform.Remove(platform);
                            int oldOrder = EditorListOrderBuildTargetsByPlatform[platform];
                            EditorListOrderBuildTargetsByPlatform.Remove(platform);
                            EditorListOrderBuildTargetsByPlatform[newPlat] = oldOrder;
                            EditorGUILayout.EndHorizontal();
                            Save();
                            break;
                        }

                        BuildTargetsByPlatform.TryGetValue(platform, out int buildTargetIndex);

                        var buildTarget = buildTargetIndex != 0 ? (BuildTarget)buildTargetIndex : ValidBuildTargets().First();

                        var strings = ValidBuildTargets().Select(x => x.ToString()).ToArray();
                        int stringBuildTargetIndex = strings.ToList().IndexOf(buildTarget.ToString());

                        if (stringBuildTargetIndex == -1)
                        {
                            // build target no longer supported by Unity
                            stringBuildTargetIndex = 0;
                        }

                        label = new GUIContent("Build target", "The build target associated with this runtime platform");
                        EditorGUILayout.LabelField(label, GUILayout.Width(100));
                        int result = EditorGUILayout.Popup(stringBuildTargetIndex, strings);

                        BuildTarget resultBuildTarget = Enum.Parse<BuildTarget>(strings[result]);

                        if ((int)resultBuildTarget != buildTargetIndex)
                        {
                            Log.Write(LogLevel.Log, $"Seeing {platform} build target to {resultBuildTarget}");
                            BuildTargetsByPlatform[platform] = (int)resultBuildTarget;
                            EditorGUILayout.EndHorizontal();
                            break;
                        }
                        
                        if (GUILayout.Button("Remove"))
                        {
                            BuildTargetsByPlatform.Remove(kvp.Key);
                            EditorGUILayout.EndHorizontal();
                            break;
                        }
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();

                        EditorGUILayout.LabelField("ID:", GUILayout.Width(40));
                        BucketsByBuildTarget.TryGetValue(buildTargetIndex, out string id);

                        if (manuallyEditBucketIds)
                        {
                            string newId = EditorGUILayout.TextField(id)?.Trim();
                            if (newId != BucketsByBuildTarget[buildTargetIndex])
                            {
                                BucketsByBuildTarget[buildTargetIndex] = newId;
                                Save();
                                break;
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField(id);
                        }

                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.Space();
                    }

                    HandleAddRuntimeButton();
                    EditorGUILayout.EndScrollView();
                    
                    EditorGUI.indentLevel--;

                    Assembly[] assemblies = AppDomain
                                        .CurrentDomain
                                        .GetAssemblies();

                    if (_allUnityObjectTypes == null)
                    {
                        _allUnityObjectTypes = new List<string>();

                        foreach (Assembly a in assemblies)
                        {
                            foreach (Type t in a.GetTypes())
                            {
                                if (!typeof(UnityEngine.Object).IsAssignableFrom(t))
                                    continue;

                                string assembly = t.Assembly.GetName().Name;
                                string type = !string.IsNullOrWhiteSpace(t.Namespace) ? $"{t.Namespace}.{t.Name}" : t.Name;

                                //Log.Write(LogLevel.Log, $"{type}, {assembly}");

                                if (_instance.Types.Any(x => x.TypeName == type && x.AssemblyName == assembly))
                                    continue;

                                _allUnityObjectTypes.Add($"{type}, {assembly}");
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(_filter))
                        {
                            _allUnityObjectTypes = _allUnityObjectTypes
                                        .Where(x => x.ToLower().Contains(_filter.ToLower()))
                                        .ToList();
                        }
                    }

                    EditorGUILayout.LabelField("Included types:");
                    EditorGUI.indentLevel++;

                    foreach (var item in Types)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(item.TypeName);
                        if (GUILayout.Button("Remove"))
                        {
                            _allUnityObjectTypes.Add($"{item.TypeName}, {item.AssemblyName}");
                            Types.Remove(item);
                            EditorGUILayout.EndHorizontal();
                            Save();
                            break;
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.Space();
                    label = new GUIContent("Filter", "Search for object types");
                    string newFilter = EditorGUILayout.TextField(label, _filter);

                    if (newFilter != _filter)
                    {
                        _allUnityObjectTypes = null;
                        _filter = newFilter;
                        return;
                    }

                    if (_allUnityObjectTypes.Count > 0)
                    {
                        EditorGUILayout.BeginHorizontal();

                        _selectedIndex = EditorGUILayout.Popup(
                            _selectedIndex,
                            _allUnityObjectTypes.ToArray()
                        );

                        if (GUILayout.Button("Add"))
                        {
                            var items = _allUnityObjectTypes[_selectedIndex]
                                .Split(new string[] { ", " }, StringSplitOptions.None);

                            Types.Add(new TypeAssembly
                            {
                                TypeName = items[0],
                                AssemblyName = items[1]
                            });
                            _allUnityObjectTypes.RemoveAt(_selectedIndex);
                            Save();
                            _selectedIndex = 0;
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUI.indentLevel--;
                }
                else 
                {
                    HandleAddRuntimeButton();
                }
            }

            if (EditorGUI.EndChangeCheck()) 
            {
                Save();
            }
        }

        private void HandleAddRuntimeButton()
        {
            if (ValidRuntimePlatforms().Count > 0 && GUILayout.Button("Add Runtime"))
            {
                var key = ValidRuntimePlatforms().First();
                var value = (int)ValidBuildTargets().First();
                BuildTargetsByPlatform.Add(key, value);

                int largest = EditorListOrderBuildTargetsByPlatform
                              .Values
                              .OrderByDescending(x => x)
                              .FirstOrDefault();

                if (largest != default) largest++;
                EditorListOrderBuildTargetsByPlatform[key] = largest;
                Save();
            }
        }

        public List<BuildTarget> ValidBuildTargets() => 
            ((BuildTarget[])Enum.GetValues(typeof(BuildTarget)))
            .Where(x => !IsObsolete(x) && x != BuildTarget.NoTarget)
            .ToList();

        private List<RuntimePlatform> ValidRuntimePlatforms() =>
            ((RuntimePlatform[])Enum.GetValues(typeof(RuntimePlatform)))
            .Where(x => !IsObsolete(x) && !BuildTargetsByPlatform.ContainsKey(x))
            .ToList();

        public void GenerateBuckets()
        {
            var targets = (BuildTarget[])Enum.GetValues(typeof(BuildTarget));
            foreach (BuildTarget target in targets) 
            {
                if (target == BuildTarget.NoTarget || IsObsolete(target)) continue;

                string command = $"buckets create {UnityProjectId} {target}";
                AssetBundleManager.RunCliCommands(new List<string>() { command }, out _);
            }

            string command2 = $"buckets list {UnityProjectId}";
            AssetBundleManager.RunCliCommands(new List<string>() { command2 }, out List<string> output);
            bool atLeastOneMatch = false;
            foreach (var item in output)
            {
                Match match = Regex.Match(item, @"-\s(.*?)\s\((.*?)\)");
                if (match.Success) 
                { 
                    string buildTargetName = match.Groups[1].Value;
                    string bucketId = match.Groups[2].Value;
                    int buildTarget = (int)Enum.Parse(typeof(BuildTarget), buildTargetName);
                    BucketsByBuildTarget[buildTarget] = bucketId;
                    Log.Write(LogLevel.Log, $"Stored {buildTargetName} bucket Id {bucketId}");
                    atLeastOneMatch = true;
                }
            }

            if (!atLeastOneMatch)
            {
                Debug.LogError("No generated buckets were detected. Please check your Project ID, API Key and Environment ID and try again.");
            }

            Save();
        }

        public static bool IsObsolete(Enum value)
        {
            var fi = value.GetType().GetField(value.ToString());
            var attributes = (ObsoleteAttribute[])
                fi.GetCustomAttributes(typeof(ObsoleteAttribute), false);
            return (attributes != null && attributes.Length > 0);
        }
#endif
    }
}