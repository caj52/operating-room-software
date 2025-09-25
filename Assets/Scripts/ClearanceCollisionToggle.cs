using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ClearanceCollisionToggle : MonoBehaviour
{
    [Header("What to tint")]
    [Tooltip("If empty, all LineRenderers on this object and its children will be tinted.")]
    public List<LineRenderer> lineRenderers = new List<LineRenderer>();

    [Header("Colors")]
    public Color normalColor = new Color(0f, 1f, 0f, 0.5f); // green
    public Color collidingColor = new Color(1f, 0f, 0f, 0.5f); // red

    [Header("Collision Filters")]
    [Tooltip("Leave empty to react to any collider.")]
    public string reactToTag = "";
    [Tooltip("Limit reactions to these layers. (~0 = all layers).")]
    public LayerMask reactToLayers = ~0;
    [Tooltip("Use trigger events (OnTriggerEnter/Exit). Turn OFF to use OnCollisionEnter/Exit.")]
    public bool useTrigger = true;

    // State
    public bool IsColliding { get; private set; }
    private int overlapCount = 0;
    private static readonly int _ColorId = Shader.PropertyToID("_Color");
    private static readonly int _BaseColorId = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock _mpb;

    void Awake()
    {
        // Auto-wire line renderers if not set
        if (lineRenderers == null || lineRenderers.Count == 0)
        {
            foreach (var lr in GetComponentsInChildren<LineRenderer>(includeInactive: true))
                if (lr) lineRenderers.Add(lr);
        }

        // If a collider exists here (e.g., added by LineToMeshConverter), we’ll receive its events too.
        // If you’re using trigger collisions, ensure collider.isTrigger = true on that component.
        _mpb = new MaterialPropertyBlock();
        ApplyColor(normalColor);
    }

    // ---------- Collision (Trigger) ----------
    void OnTriggerEnter(Collider other)
    {
        if (!useTrigger) return;
        if (!ShouldReact(other.gameObject)) return;
        EnterContact();
    }

    void OnTriggerExit(Collider other)
    {
        if (!useTrigger) return;
        if (!ShouldReact(other.gameObject)) return;
        ExitContact();
    }

    // ---------- Collision (Physics) ----------
    void OnCollisionEnter(Collision other)
    {
        if (useTrigger) return;
        if (!ShouldReact(other.gameObject)) return;
        EnterContact();
    }

    void OnCollisionExit(Collision other)
    {
        if (useTrigger) return;
        if (!ShouldReact(other.gameObject)) return;
        ExitContact();
    }

    // ---------- Helpers ----------
    private bool ShouldReact(GameObject other)
    {
        if (((1 << other.layer) & reactToLayers) == 0) return false;
        if (!string.IsNullOrEmpty(reactToTag) && !other.CompareTag(reactToTag)) return false;
        return true;
    }

    private void EnterContact()
    {
        overlapCount++;
        if (overlapCount == 1)
        {
            IsColliding = true;
            ApplyColor(collidingColor);
        }
    }

    private void ExitContact()
    {
        overlapCount = Mathf.Max(0, overlapCount - 1);
        if (overlapCount == 0)
        {
            IsColliding = false;
            ApplyColor(normalColor);
        }
    }

    private void ApplyColor(Color c)
    {
        if (lineRenderers == null) return;

        foreach (var lr in lineRenderers)
        {
            if (!lr) continue;

            // Primary (works for most LineRenderer materials):
            lr.startColor = c;
            lr.endColor = c;

            // Fallback for shaders that read _Color/_BaseColor:
            lr.GetPropertyBlock(_mpb);
            _mpb.SetColor(_ColorId, c);
            _mpb.SetColor(_BaseColorId, c);
            lr.SetPropertyBlock(_mpb);
        }
    }

    // Optional manual API if you want to force a state from elsewhere:
    public void ForceColliding(bool on)
    {
        overlapCount = on ? Mathf.Max(overlapCount, 1) : 0;
        IsColliding = on;
        ApplyColor(on ? collidingColor : normalColor);
    }
}
