using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Utility for capturing and restoring component enabled states and custom component data.
/// This focuses on safe, opt-in serialization through ISaveStateProvider to avoid
/// brittle reflection-based capture of every field while still supporting UnityEngine.Object references.
/// </summary>
public static class SaveUtility
{
    /// <summary>
    /// Represents enabled state of common Unity components.
    /// </summary>
    [Serializable]
    public class ComponentEnabledState
    {
        public string path;      // hierarchy path of the component's GameObject
        public string type;      // component type full name
        public bool? enabled;    // enabled flag if component supports it
        public bool? active;     // activeSelf for GameObject (reserved)
    }

    /// <summary>
    /// Arbitrary state for custom components implementing ISaveStateProvider.
    /// </summary>
    [Serializable]
    public class SerializedComponentState
    {
        public string path;          // hierarchy path of the component's GameObject
        public string type;          // component type full name (component)
        public string stateType;     // saved state type (for deserialization)
        public string jsonPayload;   // serialized custom state
        public int version;          // optional version for schema evolution
    }

    /// <summary>
    /// Interface for components that can provide their own save/load snapshot in JSON.
    /// Implement this on MonoBehaviours that have complex internal state.
    /// </summary>
    public interface ISaveStateProvider
    {
        /// <summary>
        /// Return a serializable struct/class capturing state. It will be serialized to JSON.
        /// </summary>
        object CaptureState(out int version);

        /// <summary>
        /// Restore a previously saved state. The version corresponds to the saved version to support migration.
        /// </summary>
        void RestoreState(object state, int version);
    }

    /// <summary>
    /// Optional hooks you can implement to run logic around save/load.
    /// </summary>
    public interface ISaveHooks
    {
        void OnBeforeSave();
        void OnAfterLoad();
    }

    /// <summary>
    /// JSON converter for UnityEngine.Object references used inside custom state objects.
    /// Stores a path and optional component type so references can be resolved on load.
    /// </summary>
    public class UnityObjectReferenceConverter : JsonConverter
    {
        private const string KeyType = "$type";
        private const string KeyPath = "path";
        private const string KeyCompType = "componentType";

        public override bool CanConvert(Type objectType)
        {
            return typeof(UnityEngine.Object).IsAssignableFrom(objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var jo = new JObject();
            jo[KeyType] = value.GetType().AssemblyQualifiedName;

            if (value is GameObject go)
            {
                jo[KeyPath] = ConfigurationManager.GetGameObjectPath(go);
            }
            else if (value is Component comp)
            {
                jo[KeyPath] = ConfigurationManager.GetGameObjectPath(comp.gameObject);
                jo[KeyCompType] = comp.GetType().AssemblyQualifiedName;
            }
            else
            {
                // Unsupported Unity object types: write null
                jo = null;
            }

            if (jo == null) writer.WriteNull();
            else jo.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var jo = JObject.Load(reader);
            string path = jo[KeyPath]?.ToString();
            string compTypeName = jo[KeyCompType]?.ToString();

            if (string.IsNullOrEmpty(path)) return null;
            var go = GameObject.Find(path);
            if (go == null) return null;

            if (!string.IsNullOrEmpty(compTypeName))
            {
                var t = Type.GetType(compTypeName);
                if (t == null) return null;
                return go.GetComponent(t) as Component;
            }

            if (typeof(Component).IsAssignableFrom(objectType))
            {
                // No component type info; can't resolve
                return null;
            }

            if (typeof(GameObject).IsAssignableFrom(objectType) || objectType == typeof(UnityEngine.Object))
            {
                return go;
            }

            return null;
        }
    }

    private static readonly JsonSerializerSettings _stateJsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.None,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        PreserveReferencesHandling = PreserveReferencesHandling.None,
        TypeNameHandling = TypeNameHandling.None,
        Converters = new List<JsonConverter> { new UnityObjectReferenceConverter() }
    };

    /// <summary>
    /// Components whose enabled flag must NOT be captured/restored.
    /// CCDIK disables itself at the end of Start() after creating its Target and joints;
    /// restoring enabled=false on load prevents Start() from ever running, leaving
    /// Target unassigned (NRE when selecting a light with inverse control).
    /// CCDIKJoint instances are runtime-generated by CCDIK and managed by it.
    /// </summary>
    private static bool IsRuntimeManagedComponent(Component comp)
        => comp is CCDIK || comp is CCDIKJoint;

    public static List<ComponentEnabledState> CaptureEnabledStates(GameObject go)
    {
        var list = new List<ComponentEnabledState>();
        string path = ConfigurationManager.GetGameObjectPath(go);

        // Built-in common components with enabled property
        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null) continue;
            if (IsRuntimeManagedComponent(comp)) continue;
            try
            {
                var type = comp.GetType();
                bool handled = false;

                if (comp is Behaviour behaviour)
                {
                    list.Add(new ComponentEnabledState
                    {
                        path = path,
                        type = type.AssemblyQualifiedName,
                        enabled = behaviour.enabled
                    });
                    handled = true;
                }
                else if (comp is Renderer renderer)
                {
                    list.Add(new ComponentEnabledState
                    {
                        path = path,
                        type = type.AssemblyQualifiedName,
                        enabled = renderer.enabled
                    });
                    handled = true;
                }
                else if (comp is Collider collider)
                {
                    list.Add(new ComponentEnabledState
                    {
                        path = path,
                        type = type.AssemblyQualifiedName,
                        enabled = collider.enabled
                    });
                    handled = true;
                }
                else if (comp is Canvas canvas)
                {
                    list.Add(new ComponentEnabledState
                    {
                        path = path,
                        type = type.AssemblyQualifiedName,
                        enabled = canvas.enabled
                    });
                    handled = true;
                }

                if (!handled)
                {
                    // best-effort: try to reflect an "enabled" property
                    var prop = type.GetProperty("enabled");
                    if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead)
                    {
                        bool value = (bool)prop.GetValue(comp, null);
                        list.Add(new ComponentEnabledState
                        {
                            path = path,
                            type = type.AssemblyQualifiedName,
                            enabled = value
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveUtility] Enabled state capture failed on {comp?.GetType().Name}: {ex.Message}");
            }
        }

        return list;
    }

    public static void RestoreEnabledStates(GameObject go, List<ComponentEnabledState> states)
    {
        if (states == null || states.Count == 0) return;
        string path = ConfigurationManager.GetGameObjectPath(go);

        foreach (var state in states.Where(s => s.path == path))
        {
            try
            {
                var type = Type.GetType(state.type);
                if (type == null) continue;
                var comp = go.GetComponent(type) as Component;
                if (comp == null) continue;
                if (IsRuntimeManagedComponent(comp)) continue; // see IsRuntimeManagedComponent docs

                if (state.enabled.HasValue)
                {
                    if (comp is Behaviour behaviour) behaviour.enabled = state.enabled.Value;
                    else if (comp is Renderer renderer) renderer.enabled = state.enabled.Value;
                    else if (comp is Collider collider) collider.enabled = state.enabled.Value;
                    else if (comp is Canvas canvas) canvas.enabled = state.enabled.Value;
                    else
                    {
                        var prop = type.GetProperty("enabled");
                        if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite)
                        {
                            prop.SetValue(comp, state.enabled.Value, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveUtility] Enabled state restore failed for {state.type}: {ex.Message}");
            }
        }
    }

    public static List<SerializedComponentState> CaptureCustomComponentStates(GameObject go)
    {
        var list = new List<SerializedComponentState>();
        string path = ConfigurationManager.GetGameObjectPath(go);
        var providers = go.GetComponents<MonoBehaviour>().OfType<ISaveStateProvider>();
        foreach (var provider in providers)
        {
            try
            {
                int version;
                object state = provider.CaptureState(out version);
                string payload = JsonConvert.SerializeObject(state, _stateJsonSettings);
                list.Add(new SerializedComponentState
                {
                    path = path,
                    type = provider.GetType().AssemblyQualifiedName,
                    stateType = state != null ? state.GetType().AssemblyQualifiedName : null,
                    jsonPayload = payload,
                    version = version
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveUtility] CaptureCustomComponentStates failed on {provider.GetType().Name}: {ex.Message}");
            }
        }
        return list;
    }

    public static void RestoreCustomComponentStates(GameObject go, List<SerializedComponentState> states)
    {
        if (states == null || states.Count == 0) return;
        string path = ConfigurationManager.GetGameObjectPath(go);

        foreach (var s in states.Where(s => s.path == path))
        {
            try
            {
                var type = Type.GetType(s.type);
                if (type == null) continue;
                var comp = go.GetComponent(type) as MonoBehaviour;
                if (comp == null) continue;

                if (comp is ISaveStateProvider provider)
                {
                    object stateObj = null;
                    if (!string.IsNullOrEmpty(s.stateType) && !string.IsNullOrEmpty(s.jsonPayload))
                    {
                        var stateType = Type.GetType(s.stateType);
                        if (stateType != null)
                        {
                            stateObj = JsonConvert.DeserializeObject(s.jsonPayload, stateType, _stateJsonSettings);
                        }
                    }
                    provider.RestoreState(stateObj, s.version);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SaveUtility] RestoreCustomComponentStates failed for {s.type}: {ex.Message}");
            }
        }
    }
}
