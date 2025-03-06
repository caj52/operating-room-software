using System;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Apply this attribute to a 
    /// <see cref="SerializeField"/> <see cref="string"/> 
    /// field to reference an asset bundle name with the 
    /// mask of a normal Unity property drawer of the 
    /// supplied type
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class AssetBundleReferenceAttribute : PropertyAttribute
    {
        public AssetBundleReferenceAttribute(Type type, string label)
        {
            Type = type;
            Label = label;
        }

        public Type Type { get; }
        public string Label { get; }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(AssetBundleReferenceAttribute))]
    public class AssetBundleReferenceAttributeDrawer : PropertyDrawer
    {
        private Object _existingObject;

        public override float GetPropertyHeight(SerializedProperty property,
                                                GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position,
                                   SerializedProperty property,
                                   GUIContent label)
        {
            if (string.Compare(property.type, nameof(String), true) != 0)
            {
                // attribute was improperly added to a non-string field
                // so we will return the default property drawer
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            var assetBundleRefAttribute = (AssetBundleReferenceAttribute)attribute;
            Type type = assetBundleRefAttribute.Type;
            label.text = assetBundleRefAttribute.Label;

            string existingAssetBundleName = property.stringValue;

            if (_existingObject == null)
            {
                string[] paths = AssetDatabase.GetAssetPathsFromAssetBundle(existingAssetBundleName);
                string existingAssetPath = paths.Length == 0 ? null : paths[0];
                if (existingAssetPath != null)
                {
                    _existingObject = AssetDatabase.LoadAssetAtPath(existingAssetPath, type);
                }
            }
            
            Object newObj = EditorGUI.ObjectField(position, label, _existingObject, type, false);

            if (newObj == _existingObject) return;

            if (newObj == null && !string.IsNullOrEmpty(existingAssetBundleName)) 
            {
                property.stringValue = null;
                property.serializedObject.ApplyModifiedProperties();
                _existingObject = null;
                return;
            }

            if (AssetBundleManager.TryGetAssetBundleName(newObj, out string assetBundleName) && 
                assetBundleName != existingAssetBundleName)
            {
                property.stringValue = assetBundleName;
                property.serializedObject.ApplyModifiedProperties();
                _existingObject = null;
            }
            else
            {
                Debug.LogError($"No asset bundle name found for object {newObj.name} or asset type was not configured in Asset Bundle Manager prefab.");
            }
        }
    }
#endif
}