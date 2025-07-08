using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class LineToMeshConverter : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private MeshFilter meshFilter;
    private SphereCollider sphereCollider;
    private Mesh mesh;

    public float lineThickness = 0.1f;
    public float widthScale = 0.5f;
    public int circleResolution = 8;
    Selectable selectable;
    void Start()
    {

        lineRenderer = GetComponent<LineRenderer>();
        meshFilter = GetComponent<MeshFilter>();
        selectable = transform.parent.GetComponent<Selectable>();
        // Get or add a SphereCollider component
        sphereCollider = GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = gameObject.AddComponent<SphereCollider>();
        }
        sphereCollider.isTrigger = true;

        if (mesh == null)
        {
            mesh = new Mesh();
            meshFilter.mesh = mesh;
        }

        UpdateMesh();
    }

    void Update()
    {
        UpdateMesh();

        Collider[] colliders = Physics.OverlapSphere(transform.position, 0.5f);
        if (colliders.Length == 0)
        {
            Debug.Log("Anas => No collision detected, should trigger exit");
        }
    }

    void UpdateMesh()
    {
        int pointCount = lineRenderer.positionCount;
        if (pointCount < 2) return;

        Vector3[] positions = new Vector3[pointCount];
        lineRenderer.GetPositions(positions);

        GenerateMesh(positions);
    }

    void GenerateMesh(Vector3[] positions)
    {
        mesh.Clear();

        int segmentCount = positions.Length - 1;
        int vertexCount = segmentCount * circleResolution * 2;
        int triangleCount = segmentCount * circleResolution * 6;

        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[triangleCount];

        Quaternion rotation = transform.rotation;

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 direction = (positions[i + 1] - positions[i]).normalized;
            Vector3 up = Vector3.Cross(direction, Vector3.forward).normalized;
            Vector3 right = Vector3.Cross(direction, up).normalized;

            up = rotation * up;
            right = rotation * right;

            for (int j = 0; j < circleResolution; j++)
            {
                float angle = (float)j / circleResolution * Mathf.PI * 2;
                float x = Mathf.Cos(angle) * lineThickness * widthScale;
                float z = Mathf.Sin(angle) * lineThickness * widthScale;

                vertices[i * circleResolution + j] = positions[i] + up * x + right * z;
                vertices[i * circleResolution + j + circleResolution] = positions[i + 1] + up * x + right * z;
            }

            for (int j = 0; j < circleResolution; j++)
            {
                int current = i * circleResolution + j;
                int next = i * circleResolution + (j + 1) % circleResolution;

                int front1 = current;
                int front2 = next;
                int back1 = current + circleResolution;
                int back2 = next + circleResolution;

                int baseIndex = (i * circleResolution + j) * 6;

                triangles[baseIndex] = front1;
                triangles[baseIndex + 1] = back1;
                triangles[baseIndex + 2] = front2;

                triangles[baseIndex + 3] = front2;
                triangles[baseIndex + 4] = back1;
                triangles[baseIndex + 5] = back2;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (sphereCollider != null)
        {
            sphereCollider.center = mesh.bounds.center;
            sphereCollider.radius = Mathf.Max(mesh.bounds.extents.x, mesh.bounds.extents.y, mesh.bounds.extents.z);
        }
        DestroyImmediate(GetComponent<MeshFilter>());
        DestroyImmediate(GetComponent<MeshRenderer>());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("ClearanceLine"))
        {
            if (this.transform.parent != other.transform.parent)
            {
                Debug.Log("Anas => Not Same Highest Selectable and Intersaction");
                SetRendererColor(this, new Color(1f, 0f, 0f, 0.5f)); // Red
                SetRendererColor(other.GetComponent<LineToMeshConverter>(), new Color(1f, 0f, 0f, 0.5f));
                UI_DialogPrompt.Open($"{selectable.UIButtonName} is colliding with the {other.gameObject.name}",
                 new ButtonAction
                 {
                     ButtonText = "Ok",
                     Action = () =>
                     {
                         UI_DialogPrompt.Close();
                     },
                 });
            }
        }
        if (other.gameObject.layer == LayerMask.NameToLayer("Wall") && !other.gameObject.name.Contains("Ceil") && !other.gameObject.name.Contains("Floor"))
        {
            Debug.Log("Anas => Touching the Wall!");
            SetRendererColor(this, new Color(1f, 0f, 0f, 0.5f));
            UI_DialogPrompt.Open($"{selectable.UIButtonName} is touching the wall!",
            new ButtonAction
            {
                ButtonText = "Ok",
                Action = () =>
                {
                    UI_DialogPrompt.Close();
                },
            });
        }
    }
    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("ClearanceLine"))
        {
            if (this.transform.parent != other.transform.parent)
            {
                SetRendererColor(this, new Color(1f, 0f, 0f, 0.5f)); // Red
                SetRendererColor(other.GetComponent<LineToMeshConverter>(), new Color(1f, 0f, 0f, 1f));
            }
        }
    }
    private void OnTriggerExit(Collider other)
    {
        Debug.Log("Anas=> Collision not Detected", other.gameObject);
        if (other.CompareTag("ClearanceLine"))
        {
            Debug.Log("Anas not Detected with another LineObject!");
            SetRendererColor(this, new Color(0, 1, 0, 0.5f)); // Green
            SetRendererColor(other.GetComponent<LineToMeshConverter>(), new Color(0, 1, 0, 0.5f));
        }
        if (other.gameObject.layer == LayerMask.NameToLayer("Wall") && !other.gameObject.name.Contains("Ceil") && !other.gameObject.name.Contains("Floor"))
        {
            Debug.Log("Anas => Exited from wall contact");
            SetRendererColor(this, new Color(0, 1, 0, 0.5f));
        }
    }

    private void SetRendererColor(LineToMeshConverter renderer, Color color)
    {
        if (renderer != null && renderer.GetComponent<LineRenderer>() != null && renderer.GetComponent<LineRenderer>().materials.Length > 0)
        {
            renderer.GetComponent<LineRenderer>().materials[0].color = color;
        }
    }

}
