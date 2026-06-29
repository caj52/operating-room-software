using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class ClearanceLineColorToggle : MonoBehaviour
{
    [Header("Colors")]
    public Color normalColor = new Color(0f, 1f, 0f, 0.5f);   // green
    public Color collideColor = new Color(1f, 0f, 0f, 0.5f);  // red

    private Renderer rend;
    private int wallLayer;
    private bool _subscribed;
    private Selectable _selectable;
    private MaterialPropertyBlock _colorBlock;
    private Color _currentColor;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        wallLayer = LayerMask.NameToLayer("Wall");
        ApplyColor(normalColor);

        // Ensure we have a kinematic Rigidbody for trigger events
        var rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = true;
        rb.useGravity = false;

        _selectable = GetComponentInParent<Selectable>();

        // Subscribe to the clearance lines toggle so we can react even while inactive
        SubscribeOnce();
        ApplyVisibilityFromClearanceToggle();
    }

    private void SubscribeOnce()
    {
        if (_subscribed) return;
        UI_ToggleClearanceLines.ClearanceLinesToggled.AddListener(OnClearanceToggleChanged);
        _subscribed = true;
    }

    void OnDestroy()
    {
        if (_subscribed)
        {
            UI_ToggleClearanceLines.ClearanceLinesToggled.RemoveListener(OnClearanceToggleChanged);
            _subscribed = false;
        }
    }

    private void OnClearanceToggleChanged()
    {
        ApplyVisibilityFromClearanceToggle();
    }

    private void ApplyVisibilityFromClearanceToggle()
    {
        bool shouldBeActive = UI_ToggleClearanceLines.IsActive;
        if (gameObject.activeSelf != shouldBeActive)
        {
            gameObject.SetActive(shouldBeActive);
            if (!shouldBeActive)
            {
                // Reset tint when hidden
                ApplyColor(normalColor);
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // Ignore collisions with other clearance lines entirely
        if (other.CompareTag("ClearanceLine")) return;

        if (IsWall(other))
        {
            ApplyColor(collideColor);
            TryShowProximityAlert($"{GetDisplayName()} is touching the wall!");
            return;
        }
    }

    void OnTriggerStay(Collider other)
    {
        // Ignore collisions with other clearance lines entirely
        if (other.CompareTag("ClearanceLine")) return;

        if (IsWall(other))
        {
            ApplyColor(collideColor);
        }
    }

    void OnTriggerExit(Collider other)
    {
        // Ignore collisions with other clearance lines entirely
        if (other.CompareTag("ClearanceLine")) return;

        if (IsWall(other))
        {
            ApplyColor(normalColor);
        }
    }

    bool IsWall(Collider other)
    {
        if (other.gameObject.layer == wallLayer)
        {
            string n = other.gameObject.name;
            return !n.Contains("Ceil") && !n.Contains("Floor");
        }
        return false;
    }

    void TryShowProximityAlert(string message)
    {
        if (!UI_ToggleProximityAlerts.IsActive) return;

        UI_DialogPrompt.Open(message,
            new ButtonAction
            {
                ButtonText = "Ok",
                Action = () => { UI_DialogPrompt.Close(); }
            });
    }

    string GetDisplayName()
    {
        if (_selectable != null && !string.IsNullOrEmpty(_selectable.UIButtonName))
            return _selectable.UIButtonName;
        return gameObject.name;
    }

    void ApplyColor(Color c)
    {
        if (!rend || _currentColor == c) return;
        _currentColor = c;
        MaterialColorUtility.SetColor(rend, c, ref _colorBlock);
    }
}
