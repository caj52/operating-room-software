using System;
using UnityEngine;

/// <summary>
/// Invokes an action at most once per interval (wall-clock seconds).
/// </summary>
public class ThrottledInvoker
{
    private readonly float _intervalSeconds;
    private float _lastInvokeTime = float.NegativeInfinity;

    public ThrottledInvoker(float intervalSeconds)
    {
        _intervalSeconds = intervalSeconds;
    }

    public void Invoke(Action action)
    {
        float now = Time.unscaledTime;
        if (now - _lastInvokeTime < _intervalSeconds)
            return;

        _lastInvokeTime = now;
        action?.Invoke();
    }

    public void InvokeImmediate(Action action)
    {
        _lastInvokeTime = Time.unscaledTime;
        action?.Invoke();
    }
}
