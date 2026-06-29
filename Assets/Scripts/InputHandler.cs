using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Singleton class
/// </summary>
public class InputHandler : MonoBehaviour
{
    private static readonly KeyCode[] TrackedKeys = { KeyCode.Escape, KeyCode.Delete };
    private static readonly int UILayer = LayerMask.NameToLayer("UI");
    private static PointerEventData _pointerEventData;
    private static readonly List<RaycastResult> RaycastResults = new List<RaycastResult>();

    private static InputHandler Instance { get; set; }
    public static Vector2 MouseDeltaPixels { get; private set; }
    public static Vector2 MouseDeltaScreenPercentage { get; private set; }
    public static EventHandler<KeyStateChangedEventArgs> KeyStateChanged;
    public static bool MouseWasDownOverUI { get; private set; }

    private KeyState[] _keyStates;
    private static float _timeClickHeldDown;
    private bool _isClicking;
    private Vector2 _mousePosLastFrame;
    private static float _mouseTotalScreenPercentageDistanceWhileClicked;

    public static bool WasProperClick => _timeClickHeldDown < 0.25f &&
        _mouseTotalScreenPercentageDistanceWhileClicked < 0.1f;

    public static bool IsPointerOverUIElement()
    {
        if (EventSystem.current == null) return false;

        if (_pointerEventData == null)
            _pointerEventData = new PointerEventData(EventSystem.current);

        _pointerEventData.position = Input.mousePosition;
        RaycastResults.Clear();
        EventSystem.current.RaycastAll(_pointerEventData, RaycastResults);

        for (int index = 0; index < RaycastResults.Count; index++)
        {
            if (RaycastResults[index].gameObject.layer == UILayer)
                return true;
        }

        return false;
    }

    private void Awake()
    {
        Instance = this;
        _keyStates = new KeyState[TrackedKeys.Length];
    }

    private void Update()
    {
        for (int i = 0; i < TrackedKeys.Length; i++)
        {
            var keyCode = TrackedKeys[i];

            var newValue = Input.GetKeyDown(keyCode) ? KeyState.PressedThisFrame :
                Input.GetKeyUp(keyCode) ? KeyState.ReleasedThisFrame :
                Input.GetKey(keyCode) ? KeyState.HeldThisFrame :
                KeyState.None;

            bool valueChanged = newValue != _keyStates[i];
            _keyStates[i] = newValue;
            if (valueChanged)
                KeyStateChanged?.Invoke(this, new KeyStateChangedEventArgs(keyCode, newValue));
        }

        if (Input.GetMouseButtonDown(0))
        {
            _isClicking = true;
            _timeClickHeldDown = 0f;
            _mouseTotalScreenPercentageDistanceWhileClicked = 0f;
            MouseWasDownOverUI = IsPointerOverUIElement();
        }
        else if (Input.GetMouseButtonUp(0))
        {
            _isClicking = false;
        }
        else if (Input.GetMouseButton(0))
        {
            _mouseTotalScreenPercentageDistanceWhileClicked += MouseDeltaScreenPercentage.magnitude;
            _timeClickHeldDown += Time.deltaTime;
        }

        UpdateMouseDelta();
    }

    private void UpdateMouseDelta()
    {
        MouseDeltaPixels = (Vector2)Input.mousePosition - _mousePosLastFrame;
        _mousePosLastFrame = Input.mousePosition;
        MouseDeltaScreenPercentage = new Vector2(MouseDeltaPixels.x / Screen.width, MouseDeltaPixels.y / Screen.height);
    }
}

public enum KeyState
{
    None,
    PressedThisFrame,
    HeldThisFrame,
    ReleasedThisFrame
}

public class KeyStateChangedEventArgs : EventArgs
{
    public KeyStateChangedEventArgs(KeyCode keyCode, KeyState state)
    {
        KeyCode = keyCode;
        KeyState = state;
    }
    public KeyCode KeyCode { get; }
    public KeyState KeyState { get; }
}
