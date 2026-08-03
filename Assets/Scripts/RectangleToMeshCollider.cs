using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(LineRenderer))]
public class RectangleToMeshCollider : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private MeshFilter meshFilter;
    private Mesh mesh;
    private BoxCollider boxCollider;
    public float height = 0.1f;
    Selectable selectable;
    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        meshFilter = GetComponent<MeshFilter>();
        selectable = transform.parent.GetComponent<Selectable>();
        if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();

        var meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();

        mesh = new Mesh();
        meshFilter.mesh = mesh;

        boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null) boxCollider = gameObject.AddComponent<BoxCollider>();
        boxCollider.isTrigger = true;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.isKinematic = true;
        UpdateMesh();

        DestroyImmediate(GetComponent<MeshFilter>());
        DestroyImmediate(GetComponent<MeshRenderer>());
    }

    void Update()
    {
        UpdateMesh();
    }

    void UpdateMesh()
    {
        int pointCount = lineRenderer.positionCount;
        if (pointCount < 3) return;

        Vector3[] points = new Vector3[pointCount];
        lineRenderer.GetPositions(points);

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        for (int i = 0; i < pointCount - 1; i++)
        {
            Vector3 p1 = points[i];
            Vector3 p2 = points[i + 1];

            vertices.Add(p1);
            vertices.Add(p1 + Vector3.up * height);
            vertices.Add(p2 + Vector3.up * height);
            vertices.Add(p2);

            int offset = i * 4;
            triangles.Add(offset);
            triangles.Add(offset + 1);
            triangles.Add(offset + 2);
            triangles.Add(offset);
            triangles.Add(offset + 2);
            triangles.Add(offset + 3);
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        boxCollider.center = mesh.bounds.center;
        boxCollider.size = mesh.bounds.size;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("ClearanceLine"))
        {
            if (transform.parent != other.transform.parent)
            {
                Debug.Log($"{name} collided with {other.name}");
                SetRendererColor(Color.red);
                if (UI_ToggleProximityAlerts.IsActive && !Selectable.IsInElevationPhotoMode)
                {
                    UI_DialogPrompt.Open($"{selectable.UIButtonName} is colliding with {other.name}",
                           new ButtonAction { ButtonText = "Ok", Action = UI_DialogPrompt.Close });
                }
   
            }
        }

        if (other.gameObject.layer == LayerMask.NameToLayer("Wall") &&
            !other.name.Contains("Ceil") && !other.name.Contains("Floor"))
        {
            Debug.Log($"{name} touching Wall!");
            SetRendererColor(Color.red);

            if (UI_ToggleProximityAlerts.IsActive && !Selectable.IsInElevationPhotoMode)
            {
                UI_DialogPrompt.Open($"{selectable.UIButtonName} is touching the wall!",
                new ButtonAction { ButtonText = "Ok", Action = UI_DialogPrompt.Close });
            }

        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("ClearanceLine") && transform.parent != other.transform.parent)
        {
            SetRendererColor(Color.red);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"{name} no longer colliding with {other.name}");
        SetRendererColor(Color.green);
    }

    private void SetRendererColor(Color color)
    {
        if (lineRenderer != null && lineRenderer.materials.Length > 0)
        {
            lineRenderer.materials[0].color = color;
        }
    }
}
