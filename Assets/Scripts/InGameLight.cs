using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InGameLight : MonoBehaviour
{
    private static List<InGameLight> Lights { get; } = new List<InGameLight>();

    private void Awake()
    {
        Lights.Add(this);
    }

    private void OnDestroy()
    {
        Lights.Remove(this);
    }

    public static void ToggleLights(bool toggle)
    {
        Lights.ForEach(light => { light.gameObject.SetActive(toggle); });
    }
}
