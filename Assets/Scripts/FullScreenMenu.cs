using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class FullScreenMenu : MonoBehaviour
{
    public static UnityEvent OnAllMenusClosed { get; } = new();

    private static List<FullScreenMenu> _openMenus = new();
    public static bool IsOpen => _openMenus.Count > 0;

    private void OnEnable()
    {
        if (!_openMenus.Contains(this))
            _openMenus.Add(this);
    }

    private void OnDisable()
    {
        _openMenus.Remove(this);

        if (!IsOpen)
        {
            OnAllMenusClosed?.Invoke();
        }
    }
}