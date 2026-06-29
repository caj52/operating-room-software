using UnityEngine;

[RequireComponent(typeof(LineRenderer), typeof(SphereCollider))]
public class ClearanceLineCollisionMinimal : MonoBehaviour
{
    [Header("Colors")]
    public Color normalColor = new Color(0f, 1f, 0f, 0.5f);   // green
    public Color collisionColor = new Color(1f, 0f, 0f, 0.5f); // red

    [Header("Collider")]
    public float colliderRadius = 0.5f;

    private LineRenderer lr;
    private SphereCollider trig;
    private int wallLayer;
    private MaterialPropertyBlock _colorBlock;
    private Color _currentColor;

    private void Awake()
    {
        lr = GetComponent<LineRenderer>();
        if (lr.sharedMaterial == null)
            lr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        SetColor(normalColor);

        trig = GetComponent<SphereCollider>();
        trig.isTrigger = true;
        trig.radius = colliderRadius;

        wallLayer = LayerMask.NameToLayer("Wall");
    }

    private void OnEnable()
    {
        // sync immediately with current UI toggle state (if the script exists in scene)
        TryApplyToggleNow();
        // subscribe to changes
        UI_ToggleClearanceLines.ClearanceLinesToggled.AddListener(OnClearanceToggleChanged);
    }

    private void OnDisable()
    {
        UI_ToggleClearanceLines.ClearanceLinesToggled.RemoveListener(OnClearanceToggleChanged);
    }

    private void OnClearanceToggleChanged()
    {
        TryApplyToggleNow();
    }

    private void TryApplyToggleNow()
    {
        // enable/disable the trigger based on the UI toggle, and reset to green when disabled
        bool active = UI_ToggleClearanceLines.IsActive;
        if (trig) trig.enabled = active;
        if (!active) SetColor(normalColor);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsClearanceChecksOn()) return;
        if (IsClearanceOrWall(other)) SetColor(collisionColor);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsClearanceChecksOn()) return;
        if (other.CompareTag("ClearanceLine")) SetColor(collisionColor);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsClearanceChecksOn()) return;
        if (IsClearanceOrWall(other)) SetColor(normalColor);
    }

    private bool IsClearanceChecksOn() => trig && trig.enabled;

    private bool IsClearanceOrWall(Collider other)
    {
        if (other.CompareTag("ClearanceLine")) return true;

        // Same layer logic as your bigger script:
        if (other.gameObject.layer == wallLayer)
        {
            string n = other.gameObject.name;
            if (!n.Contains("Ceil") && !n.Contains("Floor")) return true;
        }
        return false;
    }

    private void SetColor(Color c)
    {
        if (lr == null || _currentColor == c) return;
        _currentColor = c;
        MaterialColorUtility.SetLineRendererColor(lr, c, ref _colorBlock);
    }
}
