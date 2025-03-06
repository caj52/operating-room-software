using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IgnoreInverseScaling : MonoBehaviour
{
    [field: SerializeField] public bool IgnoreX { get; private set; } = true;
    [field: SerializeField] public bool IgnoreY { get; private set; } = true;
    [field: SerializeField] public bool IgnoreZ { get; private set; } = true;
}
