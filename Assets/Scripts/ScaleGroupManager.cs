using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ScaleGroupManager : MonoBehaviour
{
    public static ScaleGroupManager Instance; 
    public static Action<string, Selectable.ScaleLevel> OnScaleLevelChanged;
    public static Action<string, float> OnZScaleChanged;

}
