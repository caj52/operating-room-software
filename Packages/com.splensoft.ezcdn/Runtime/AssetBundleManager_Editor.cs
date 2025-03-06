#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using System.IO;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Text;
using UnityEditor;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SplenSoft.AssetBundles
{
    public static partial class AssetBundleManager
    {
        /// <summary>
        /// Returns true if the Asset Bundle Manager is currently 
        /// processing and packaging assets on an AssetBundles -> Build command
        /// </summary>
        public static bool IsPackagingAssets { get; private set; }

        private static List<Type> _cachedTypes;
        private static List<Type> GetAssetTypes()
        {
            if (_cachedTypes == null)
            {
                var settings = AssetBundleManagerSettings.Get();
                //find ManagedAsset attributes
                var additionalTypes = new List<Type>();
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (Type t in a.GetTypes())
                    {
                        var attr = Attribute.GetCustomAttribute(t, typeof(ManagedAssetAttribute));
                        if (attr != null && !additionalTypes.Contains(t))
                        {
                            additionalTypes.Add(t);
                        }
                    }
                }

                // get types from AssetBundleManager prefab
                List<Type> existingTypes = settings.Types
                    .Select(x =>
                    {
                        var type = Type.GetType($"{x.TypeName}, {x.AssemblyName}");
                        if (type != null) return type;
                        Log.Write(LogLevel.Warning, $"Type {x.TypeName} from assembly {x.AssemblyName} does not exist (it may have been removed from the project). You should remove it from the Asset Bundle Manager's type settings.");
                        return null;
                    })
                    .Where(x => x != null).ToList();

                _cachedTypes = existingTypes.Concat(additionalTypes).ToList();
            }
            
            return _cachedTypes;
        }

        private static Dictionary<Type, object> _customAssetHandlers;
        private static Dictionary<Type, object> GetCustomAssetHandlers()
        {
            if (_customAssetHandlers == null)
            {
                _customAssetHandlers = new Dictionary<Type, object>();
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (Type t in a.GetTypes())
                    {
                        bool isValid = typeof(CustomAssetHandler).IsAssignableFrom(t) && 
                            t != typeof(CustomAssetHandler);

                        if (isValid)
                        {
                            var instance = Activator.CreateInstance(t);
                            var type = ((CustomAssetHandler)instance).Type;
                            _customAssetHandlers.Add(type, instance);
                        }
                    }
                }
            }
            return _customAssetHandlers;
        }

        public static string AssetBundlePath => Path.Combine(
            Directory.GetCurrentDirectory(),
            "AssetBundles"
        );

        #region Menu items

        [MenuItem("Assets/Copy AssetBundle Name")]
        private static void CopyAssetBundleName()
        {
            string guid = Selection.assetGUIDs[0];
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path);
            string bundleName = importer.assetBundleName;
            GUIUtility.systemCopyBuffer = bundleName;
        }

        [MenuItem("Tools/Easy CDN/Debug/Print AssetBundle Data"), 
        Tooltip("Prints all AssetBundle names and the paths associated with them in the console.")]
        private static void PrintAssetBundleData()
        {
            Array.ForEach(AssetDatabase.GetAllAssetBundleNames(), name =>
            {
                Array.ForEach(AssetDatabase.GetAssetPathsFromAssetBundle(name), path =>
                {
                    Debug.Log($"AssetBundle: {name}, Asset: {path}");
                });
            });
        }

        [MenuItem("Tools/Easy CDN/Open Documentation", priority = 1)]
        private static void OpenDocumentation()
        {
            Application.OpenURL("https://github.com/SplenSoft/ezcdn-public/wiki");
        }

        [MenuItem("Assets/Copy AssetBundle Name", isValidateFunction: true)]
        private static bool ValidateCopyAssetBundleName()
        {
            return Selection.assetGUIDs.Length == 1;
        }

        [MenuItem("Tools/Easy CDN/Clean build", isValidateFunction: true)]
        private static bool ValidateMenu_Regenerate()
        {
            return !Application.isPlaying;
        }

        [MenuItem("Tools/Easy CDN/Clear cache")]
        private static void ClearAssetBundleCache()
        {
            if (Caching.ClearCache())
            {
                EditorUtility.DisplayDialog("Success", "Asset bundle cache cleared successfully.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Failure", "Asset bundle cache could not be cleared at this time. Please try again later or restart the editor.", "OK");
            }
        }

        [MenuItem("Tools/Easy CDN/Clear cache", isValidateFunction: true)]
        private static bool ValidateClearAssetBundleCache()
        {
            return !Application.isPlaying;
        }

        [MenuItem("Tools/Easy CDN/Dry Run")]
        private static void RegenerateAssetBundleManager()
        {
            AssetDatabase.SaveAssets();

            Assembly[] loadedAssemblies = AppDomain
                .CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in loadedAssemblies)
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (typeof(AssetBundleProcessor).IsAssignableFrom(type) && 
                    type != typeof(AssetBundleProcessor))
                    {
                        var obj = Activator.CreateInstance(type);
                        if (obj is AssetBundleProcessor processor)
                        {
                            processor.OnPreprocessAssetBundles();
                        }
                        else
                        {
                            throw new Exception($"Could not cast " +
                            $"{type.Name} to {nameof(AssetBundleProcessor)}");
                        }
                    }
                }
            }

            if (!Directory.Exists(AssetBundlePath))
            {
                Directory.CreateDirectory(AssetBundlePath);
            }

            var searchString = new StringBuilder();
            foreach (var type in GetAssetTypes())
            {
                searchString.Append(" t:");
                searchString.Append(type.Name);
            }

            string searchStringString = searchString.ToString().Trim().ToLower();
            Log.Write(LogLevel.Log, $"Search string is {searchStringString}");

            var guids = AssetDatabase.FindAssets(searchStringString, new string[] { "Assets" }).ToList();
            Log.Write(LogLevel.Log, $"Asset search produced {guids.Count} guids");
            List<string> objPaths = guids.ConvertAll(x => AssetDatabase.GUIDToAssetPath(x));
            
            //var assetBundleBuilds = new List<AssetBundleBuild>();

            for (int i = 0; i < objPaths.Count; i++)
            {
                string path = objPaths[i];

                Log.Write(
                    LogLevel.Verbose, 
                    $"Preparing asset for bundling: Path is {path}");

                string guid = guids[i];
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    UpdateAssetForBundling(path, guid);
                }
                else
                {
                    Debug.LogError($"Guid was null for {path}");
                }
            }

            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Easy CDN/Dry Run", isValidateFunction: true)]
        private static bool ValidateRegenerateAssetBundleManager()
        {
            return !Application.isPlaying;
        }
        #endregion

        private static bool TryGetEditorAsset<T>(string assetBundleName, out T asset) where T : UnityEngine.Object
        {
            return TryGetEditorAsset(assetBundleName, out asset, out _);
        }

        private static bool TryGetEditorAsset<T>(string assetBundleName, out T asset, out string path) where T : UnityEngine.Object
        {
            asset = null;
            path = null;
            if (string.IsNullOrEmpty(assetBundleName)) return false;
            string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleName);
            if (assetPaths.Length == 0)
            {
                return false;
            }
            path = assetPaths[0];
            asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null;
        }

        private static void HandleAssetImporter(UnityEngine.Object obj, AssetImporter importer, string guid)
        {
            if (TryGetAssetBundleName(obj, importer, guid, out string assetBundleName))
            {
                importer.SetAssetBundleNameAndVariant(assetBundleName, null);
                EditorUtility.SetDirty(obj);
                EditorUtility.SetDirty(importer);
            }
        }

        private static bool TryGetAssetBundleName(UnityEngine.Object obj, AssetImporter importer, string guid, out string assetBundleName)
        {
            string prefix = null;
            assetBundleName = null;
            if (obj == null) return false;

            if (importer == null)
            {
                Log.Write(LogLevel.Warning, $"Importer was null for asset {obj.name}.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(guid))
            {
                Debug.LogError($"Guid was null for obj {obj.name}");
                return false;
            }

            // support for custom asset bundles not named by EZ-CDN
            if (importer.assetBundleName != null && 
                importer.assetBundleName.Contains("ezcdn_custom"))
            {
                return true;
            }

            foreach (var type in GetAssetTypes())
            {
                try
                {
                    Convert.ChangeType(obj, type);
                    
                    if (GetCustomAssetHandlers().TryGetValue(type, out object assetHandlerInstance)) 
                    {
                        //Log.Write(LogLevel.Log, $"Using custom asset handler for type {type.Name}");
                        var assetHandler = (CustomAssetHandler)assetHandlerInstance;
                        assetHandler.Handle(obj, out prefix);
                        prefix = prefix.ToLower();
                        break;
                    } 

                    prefix = type.Name.ToLower();
                    break;
                }
                catch (InvalidCastException) 
                {
                    // do nothing - this is just not a compatible type
                }
                catch
                {
                    throw;
                }
            }

            if (prefix == null)
            {
                var path = AssetDatabase.GetAssetPath(obj);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                Log.Write(LogLevel.Warning, $"No valid handling for {obj.name} " +
                                 $"... main type is " +
                                 $"{(type != null ? type.Name : "null")}"
                );
                
                return false;
            }

            assetBundleName = prefix + "_" + guid;
            importer.SetAssetBundleNameAndVariant(assetBundleName, null);
            EditorUtility.SetDirty(importer);
            return true;
        }

        /// <summary>
        /// Attempts to retrieve or generate asset bundle name. Will fail if asset type is not defined in EZCDN settings
        /// </summary>
        /// <returns>True if asset bundle name was found or generated</returns>
        public static bool TryGetAssetBundleName(UnityEngine.Object obj, out string assetBundleName)
        {
            assetBundleName = null;
            var path = AssetDatabase.GetAssetPath(obj);
            GUID guid = AssetDatabase.GUIDFromAssetPath(path);
            if (guid == default)
            {
                return false;
            }
            var guidString = guid.ToString();
            var importer = AssetImporter.GetAtPath(path);
            return TryGetAssetBundleName(obj, importer, guidString, out assetBundleName);
        }

        private static void PackageAssetBundles(List<BuildTargetBucket> buildTargetBuckets, bool cleanBuild = false)
        {
            IsPackagingAssets = true;
            try
            {
                if (cleanBuild)
                {
                    if (Directory.Exists(AssetBundlePath))
                    {
                        Directory.Delete(AssetBundlePath, true);
                    }
                }

                RegenerateAssetBundleManager();

                if (cleanBuild)
                {
                    AssetDatabase.RemoveUnusedAssetBundleNames();
                }

                foreach (var buildTargetBucket in buildTargetBuckets)
                {
                    string path = AssetBundlePath + "/" + buildTargetBucket.BuildTarget.ToString();
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }

                    BuildAssetBundleOptions flags = BuildAssetBundleOptions.AssetBundleStripUnityVersion;

                    if (cleanBuild)
                    {
                        flags |= BuildAssetBundleOptions.ForceRebuildAssetBundle;
                    }

                    var resultManifest = BuildPipeline.BuildAssetBundles(path, flags, buildTargetBucket.BuildTarget);

                    Log.Write(LogLevel.Log, $"Completed building asset bundles for {buildTargetBucket.BuildTarget}");
                    Log.Write(LogLevel.Log, $"Syncing asset bundles for {buildTargetBucket.BuildTarget} to Unity Cloud CDN");


                    string bucketId = buildTargetBucket.BucketId;

                    RunCliCommands(new List<string>()
                    {
                        $"config set bucket {bucketId}",
                        $"entries sync \"{AssetBundlePath}\\{buildTargetBucket.BuildTarget}\"",
                        $"releases create"
                    }, out var output);

                    //get release id
                    string releaseId = "";
                    output.First(x =>
                    {
                        var match = Regex.Match(x, @"Id:\s(.*)$");
                        if (match.Success) 
                        {
                            releaseId = match.Groups[1].Value;
                        }
                        return match.Success;
                    });

                    int version = AssetBundleManagerSettings.Get().Version;

                    RunCliCommands(new List<string>()
                    {
                        $"config set bucket {bucketId}",
                        $"badges add {version} {releaseId}",
                    }, out _);

                    Log.Write(LogLevel.Log, $"Finished syncing asset bundles for {buildTargetBucket.BuildTarget}");
                }
            }
            catch (InvalidOperationException)
            {
                Debug.LogError("Error while using Unity CCD CLI " +
                    "to publish asset bundles. Check your api key, " +
                    "project ID and environment ID and check your " +
                    "internet connection.");
            }
            catch
            {
                throw;
            }
            finally
            {
                IsPackagingAssets = false;
            }
        }

        /// <summary>
        /// EDITOR ONLY: Will use the included Unity Cloud Content Delivery CLI executable 
        /// for this Unity Editor platform to execute commands. Refer to the 
        /// <see href="https://docs.unity.com/ugs/manual/ccd/manual/UnityCCDCLI"> 
        /// Unity CLI documentation </see> for commands. Note: This will automatically log
        /// in with the provided API key in the plugin settings, so there's no need to provide
        /// an 'auth login' command. This will also automatically append the current environment
        /// id to each command, so there's no need to append --environment [id]
        /// </summary>
        /// <param name="commands"><see href="https://docs.unity.com/ugs/manual/ccd/manual/UnityCCDCLI"/></param>
        /// <param name="output">Output from cmd.exe or /bin/bash after running the supplied commands</param>
        /// <param name="verbose">Will append '--verbose' after every command. 
        /// Strongly recommended to leave as default (true)</param>
        public static void RunCliCommands(List<string> commands, out List<string> output, bool verbose = true)
        {
            var cmd = new Process();

            string fileName;
            string cliPath;
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                fileName = "cmd.exe";
                cliPath = "Packages\\com.splensoft.ezcdn\\Editor\\Windows\\ucd.bin";
            }
            else if (Application.platform == RuntimePlatform.OSXEditor)
            {
                fileName = "/bin/bash";
                cliPath = Path.GetFullPath("Packages\\com.splensoft.ezcdn\\Editor\\MacOS\\ucd");
            }
            else if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                fileName = "/bin/bash";
                cliPath = Path.GetFullPath("Packages\\com.splensoft.ezcdn\\Editor\\Linux\\ucd");
            }
            else
            {
                throw new Exception($"Unsupported platform {Application.platform}");
            }

            if (!File.Exists(cliPath))
            {
                throw new Exception($"Could not find CLI executable. {cliPath} does not exist.");
            }

            cmd.StartInfo.FileName = fileName;
            cmd.StartInfo.RedirectStandardInput = true;
            cmd.StartInfo.RedirectStandardOutput = true;
            cmd.StartInfo.CreateNoWindow = true;
            cmd.StartInfo.UseShellExecute = false;
            cmd.Start();
            string cdnEnvironment = AssetBundleManagerSettings.Get().ActiveEnvironmentId;
            string verboseString = verbose ? " --verbose" : "";
            cmd.StandardInput.WriteLine($"\"{cliPath}\" auth login {AssetBundleManagerSettings.Get().GetApiKey()}{verboseString}\r\n");
            commands.ForEach(c => 
                cmd.StandardInput.WriteLine($"\"{cliPath}\" {c} --environment {cdnEnvironment}{verboseString}\r\n")
            );
            cmd.StandardInput.Flush();
            cmd.StandardInput.Close();
            string output2 = cmd.StandardOutput.ReadToEnd();

            var separator = new string[] { Environment.NewLine, "\n" };
            var options = StringSplitOptions.RemoveEmptyEntries;
            output = new List<string>();
            foreach (string line in output2.Split(separator, options))
            {
                output.Add(line);
                Log.Write(LogLevel.Log, line);
            }

            if (!output.Contains("Login successful."))
            {
                Debug.LogError("CLI login was not successful. Double check your API key in the EZCDN settings");
            }
        }

        private static void UpdateAssetForBundling(string path, string guid)
        {
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath(path, typeof(UnityEngine.Object));
            if (obj == null)
            {
                Log.Write(LogLevel.Warning, $"Could not process file at path {path}");
                return;
            }
            var importer = AssetImporter.GetAtPath(path);

            HandleAssetImporter(obj, importer, guid);
            HandlePreprocessing(obj);
        }

        private static void HandlePreprocessing(UnityEngine.Object obj)
        {
            bool needsDirty = false;
            if (obj is IPreprocessAssetBundle preprocessAssetBundle)
            {
                preprocessAssetBundle.OnPreprocessAssetBundle();
                needsDirty = true;
            }
            else if (obj is GameObject gameObj)
            {
                var components = gameObj.GetComponents<Component>();
                
                foreach (var component in components)
                {
                    if (component is IPreprocessAssetBundle preprocessAssetBundle1)
                    {
                        preprocessAssetBundle1.OnPreprocessAssetBundle();
                        needsDirty = true;
                    }
                }
            }

            if (needsDirty)
            {
                EditorUtility.SetDirty(obj);
            }
        }

        private static void UpdateAssetForBundling(string path)
        {
            GUID guid = AssetDatabase.GUIDFromAssetPath(path);
            if (guid == default)
            {
                Debug.LogError($"Guid was null for path {path}");
                return;
            }
            UpdateAssetForBundling(path, guid.ToString());
        }
    }
}
#endif