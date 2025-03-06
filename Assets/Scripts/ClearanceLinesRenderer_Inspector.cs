#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public partial class ClearanceLinesRenderer : MonoBehaviour
{
    [CustomEditor(typeof(ClearanceLinesRenderer))]
    public class ClearanceLinesRenderer_Inspector : Editor
    {
        private ClearanceLinesRenderer _component;

        public override void OnInspectorGUI()
        {
            if (_component == null)
            {
                _component = target as ClearanceLinesRenderer;
            }

            EditorGUI.BeginChangeCheck();

            _component.Type = (RendererType)EditorGUILayout.EnumPopup(
                label: new GUIContent(
                    text: "Renderer Type"
                    //tooltip: "Determines "
                ),
                selected: _component.Type
            );

            if (_component.Type == RendererType.ArmAssembly)
            {
                // _component.BufferSize = EditorGUILayout.FloatField(
                //    value: _component.BufferSize,
                //    label: new GUIContent(
                //        text: "Buffer Size",
                //        tooltip: "Added to circular size of clearance lines"
                //    )
                //);

                _component.IncludeChildrenInMeasurement = EditorGUILayout.Toggle(
                    value: _component.IncludeChildrenInMeasurement,
                    label: new GUIContent(
                        text: "Include Children",
                        tooltip: "Uses children meshes when determining clearance size. Only use for \"head\" parts"
                    )
                );
            }
            else if (_component.Type == RendererType.Door) 
            {
                _component.DoorHinge = (Transform)EditorGUILayout.ObjectField(
                    label: new GUIContent(
                        text: "Hinge Transform",
                        tooltip: "A transform representing the location of a door's hinge on the XZ plane. The distance between the door hinge and strike will determine the size of the clearance lines."
                    ),
                    obj: _component.DoorHinge,
                    objType: typeof(Transform),
                    allowSceneObjects: true
                );

                _component.DoorStrike = (Transform)EditorGUILayout.ObjectField(
                    label: new GUIContent(
                        text: "Strike Transform",
                        tooltip: "A transform representing the location of a door's strike on the XZ plane. The distance between the door hinge and strike will determine the size of the clearance lines."
                    ),
                    obj: _component.DoorStrike,
                    objType: typeof(Transform),
                    allowSceneObjects: true
                );

                _component.DoorSwingAngle = EditorGUILayout.FloatField(
                    value: _component.DoorSwingAngle,
                    label: new GUIContent(
                        text: "Swing Angle",
                        tooltip: "How far the door swings open, in degrees. Default: 90"
                    )
                );
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif
