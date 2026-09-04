using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class RectangleToMeshCollider : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private BoxCollider boxCollider;
    public float height = 0.25f;
    Selectable selectable;
    private int _overlapCount;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        selectable = transform.parent != null
            ? transform.parent.GetComponent<Selectable>()
            : GetComponentInParent<Selectable>();

        if (height < 0.05f)
            height = 0.25f;

        boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null) boxCollider = gameObject.AddComponent<BoxCollider>();
        boxCollider.isTrigger = true;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.isKinematic = true;
        UpdateMesh();
        SetRendererColor(Color.green);
    }

    void Update()
    {
        UpdateMesh();
    }

    void UpdateMesh()
    {
        if (lineRenderer == null || boxCollider == null)
            return;

        int pointCount = lineRenderer.positionCount;
        if (pointCount < 3) return;

        Vector3[] points = new Vector3[pointCount];
        lineRenderer.GetPositions(points);

        bool world = lineRenderer.useWorldSpace;
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < pointCount; i++)
        {
            Vector3 p = world ? transform.InverseTransformPoint(points[i]) : points[i];
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 size = max - min;
        Vector3 center = (min + max) * 0.5f;
        Vector3 localUp = transform.InverseTransformDirection(Vector3.up);
        if (localUp.sqrMagnitude < 1e-6f)
            localUp = Vector3.up;
        localUp.Normalize();

        size.x += Mathf.Abs(localUp.x) * height;
        size.y += Mathf.Abs(localUp.y) * height;
        size.z += Mathf.Abs(localUp.z) * height;
        center += localUp * (height * 0.5f);

        boxCollider.center = center;
        boxCollider.size = size;
    }

    private bool IsOwnEquipment(Collider other)
    {
        if (other == null) return true;
        if (selectable == null)
            selectable = GetComponentInParent<Selectable>();
        if (selectable == null) return false;
        return other.transform.IsChildOf(selectable.transform);
    }

    private static bool IsIgnoredBoundary(Collider other)
    {
        string n = other.name;
        return n.Contains("Ceil") || n.Contains("Floor");
    }

    private bool IsBlocking(Collider other)
    {
        if (IsOwnEquipment(other))
            return false;
        if (other.CompareTag("ClearanceLine"))
            return transform.parent != other.transform.parent;
        if (other.gameObject.layer == LayerMask.NameToLayer("Wall") && !IsIgnoredBoundary(other))
            return true;
        var otherSel = other.GetComponentInParent<Selectable>();
        return otherSel != null && otherSel != selectable;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsBlocking(other))
            return;

        _overlapCount++;
        SetRendererColor(Color.red);

        if (!UI_ToggleProximityAlerts.IsActive || Selectable.IsInElevationPhotoMode || selectable == null)
            return;

        string label = string.IsNullOrEmpty(selectable.UIButtonName) ? selectable.name : selectable.UIButtonName;
        if (other.CompareTag("ClearanceLine"))
        {
            UI_DialogPrompt.Open($"{label} is colliding with {other.name}",
                new ButtonAction { ButtonText = "Ok", Action = UI_DialogPrompt.Close });
        }
        else
        {
            UI_DialogPrompt.Open($"{label} is touching the wall!",
                new ButtonAction { ButtonText = "Ok", Action = UI_DialogPrompt.Close });
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsBlocking(other))
            return;
        SetRendererColor(Color.red);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsBlocking(other))
            return;
        _overlapCount = Mathf.Max(0, _overlapCount - 1);
        if (_overlapCount == 0)
            SetRendererColor(Color.green);
    }

    private void SetRendererColor(Color color)
    {
        if (lineRenderer == null)
            return;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        if (lineRenderer.material != null && lineRenderer.material.HasProperty("_Color"))
            lineRenderer.material.color = color;
    }
}
