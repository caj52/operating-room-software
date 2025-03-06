using SplenSoft.AssetBundles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Handles visibility of some inspector 
/// properties for <see cref="SelectableMetaData"/>
/// when it's used as a serialized field
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class MetaDataHandlerAttribute : PropertyAttribute
{
    public MetaDataHandlerAttribute()
    {
    }
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(MetaDataHandlerAttribute))]
public class MetaDataHandlerAttributeDrawer : PropertyDrawer
{
    private Object _existingObject;

    public override float GetPropertyHeight(SerializedProperty property,
                                            GUIContent label)
    {
        var subProperty = property.FindPropertyRelative("<IsSubSelectable>k__BackingField");
        if (subProperty == null)
        {
            throw new Exception($"Could not find IsSubSelectable property. Did you rename a variable??");
        }

        if (subProperty.intValue == 1)
        {
            return 0f;
        }
        else
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }

    public override void OnGUI(Rect position,
                               SerializedProperty property,
                               GUIContent label)
    {
        if (string.Compare(property.type, nameof(SelectableMetaData), true) != 0)
        {
            // attribute was improperly added to a non-SelectableMetaData field
            // so we will return the default property drawer
            Debug.Log("MetaDataHandler attribute was placed on wrong type of field!");
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        var subProperty = property.FindPropertyRelative("<IsSubSelectable>k__BackingField");
        if (subProperty == null) 
        {
            throw new Exception($"Could not find IsSubSelectable property. Did you rename a variable??");
        }

        if (subProperty.intValue == 1)
        {
            // we hide all other values of metadata if this is a sub-selectable

            GUIContent toggleLabel = new GUIContent(
                "Is Sub Selectable",
                "True if selectable only appears as part of another \"parent\" selectable");

            bool newBool = EditorGUILayout.Toggle(toggleLabel, true);

            var subPartNameProp = property.FindPropertyRelative("<SubPartName>k__BackingField");
            string oldName = subPartNameProp.stringValue;

            string newName = EditorGUILayout.TextField("Part Name", oldName);
            bool needsSave = false;
            if (!newBool)
            {
                subProperty.intValue = 0;
                needsSave = true;
            }

            if (newName != oldName) 
            {
                subPartNameProp.stringValue = newName;
                needsSave = true;
            }

            if (needsSave)
            {
                subProperty.serializedObject.ApplyModifiedProperties();
            }
        }
        else
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }
    }
}
#endif