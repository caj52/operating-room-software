using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RTG;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pixl3D.UI.Worldspace
{
    internal class AutoRotator : MonoBehaviour
    {
#if UNITY_EDITOR
        [CustomEditor(typeof(AutoRotator))]
        private class CustomInspector : UnityEditor.Editor
        {
            private AutoRotator _script;

            public override void OnInspectorGUI()
            {
                if (!_script)
                {
                    _script = target as AutoRotator;
                }

                EditorGUI.BeginChangeCheck();

                if (!_script.RotateTowardPlayer)
                {
                    _script.RotateTowardCameraPlane = EditorGUILayout.Toggle("Rotate toward main camera plane", _script.RotateTowardCameraPlane);
                }

                if (!_script.RotateTowardCameraPlane)
                {
                    _script.RotateTowardPlayer = EditorGUILayout.Toggle("Rotate toward player", _script.RotateTowardPlayer);
                }

                if (!_script.RotateTowardPlayer && !_script.RotateTowardCameraPlane)
                {
                    _script.RotationSpeed = EditorGUILayout.FloatField(new GUIContent("Rotation Speed", "Degrees per second. Switch to negative values to change spin direction"), _script.RotationSpeed);
                    _script.RotationalAxis = (RotationalAxisEnum)EditorGUILayout.EnumPopup("Rotational Axis", _script.RotationalAxis);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(target);
                }
            }
        }
#endif

        [field: SerializeField] private bool RotateTowardPlayer { get; set; }
        [field: SerializeField] private bool RotateTowardCameraPlane { get; set; }
        [field: SerializeField] private float RotationSpeed { get; set; } = 75f;
        [field: SerializeField] private RotationalAxisEnum RotationalAxis { get; set; } = RotationalAxisEnum.Y;

        private bool _isVisible = true;

        private enum RotationalAxisEnum
        {
            X,
            Y,
            Z
        }

        private void OnBecameVisible()
        {
            _isVisible = true;
        }

        private void OnBecameInvisible()
        {
            _isVisible = false;
        }

        private void Update()
        {
            if (_isVisible)
            {
                if (RotateTowardCameraPlane)
                {
                    gameObject.RotateTowardCameraPlane();
                }
                else if (RotateTowardPlayer)
                {
                    //gameObject.RotateTowardPlayerCamera(Core.Character.Camera.transform.position, false);
                }
                else
                {
                    transform.Rotate(GetRotation());
                }
            }
        }

        private Vector3 GetRotation()
        {
            var rotation = Time.deltaTime * RotationSpeed;
            var x = RotationalAxis == RotationalAxisEnum.X ? rotation : 0;
            var y = RotationalAxis == RotationalAxisEnum.Y ? rotation : 0;
            var z = RotationalAxis == RotationalAxisEnum.Z ? rotation : 0;
            return new Vector3(x, y, z);
        }
    }
}