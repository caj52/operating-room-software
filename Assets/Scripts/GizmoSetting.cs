using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Class of primitives that is directly serialized 
/// to/from online database as part of 
/// <see cref="Selectable.MetaData"/>
/// </summary>
[Serializable]
public class GizmoSetting
{
    [field: SerializeField] 
    public Axis Axis { get; set; }

    [field: SerializeField] 
    public GizmoType GizmoType { get; set; }

    [field: SerializeField] 
    public bool Unrestricted { get; set; } = true;

    [field: SerializeField] 
    public float MaxValue { get; set; }

    [field: SerializeField] 
    public float MinValue { get; set; }

    [field: SerializeField] 
    public bool IgnoreScale { get; set; } = false;

    /// <summary>
    /// Only allows this gizmo setting if the selectable is a 
    /// root object, i.e. not attached to an attachment point
    /// </summary>
    [field: SerializeField,
    Tooltip("Gizmo setting is only allowed if the object " +
    "is a root object, i.e. not attached to an " +
    "attachment point")] 
    public bool OnlyIfRoot { get; set; }

    /// <summary>
    /// When calculating max and min heights for elevation 
    /// photos, treats min as max and vice versa (fixes 
    /// some issues with y-axis rotations)
    /// </summary>
    [field: SerializeField] public bool Invert { get; set; }

    public float GetMaxValue() => Unrestricted ? 
        float.MaxValue : MaxValue;

    public float GetMinValue() => Unrestricted ? 
        float.MinValue : MinValue;

    public void SetMinMaxValues(float min, float max)
    {
        Unrestricted = false;
        MinValue = min;
        MaxValue = max;
    }
}