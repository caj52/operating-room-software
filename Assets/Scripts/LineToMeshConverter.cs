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

    // Special case: allow same-parent collisions for this parent Selectable
    private const string TandemCoverName = "Boom - Tandem Ceiling Cover";

    // Track active, relevant clearance-line collisions for this object
    private readonly HashSet<Collider> _activeRelevantCollisions = new HashSet<Collider>();

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

        // Ensure initial state is green
        SetRendererColor(this, new Color(0f, 1f, 0f, 0.5f));

        UpdateMesh();
    }

    void Update()
    {
        UpdateMesh();

        // Fallback: if no relevant collisions are currently around, ensure color goes back to green.
        UpdateCollisionColorFallback();
    }

    private bool IsTandemParent(Transform parent)
    {
        if (parent == null) return false;
        var sel = parent.GetComponent<Selectable>();
        return sel != null && sel.UIButtonName == TandemCoverName;
    }

    private static string GetSelectableName(Transform t)
    {
        if (t == null) return "Unknown";
        var sel = t.GetComponentInParent<Selectable>();
        if (sel != null && !string.IsNullOrEmpty(sel.UIButtonName)) return sel.UIButtonName;
        return t.name;
    }

    private void UpdateCollisionColorFallback()
    {
        if (sphereCollider == null) return;

        // Clean up destroyed/invalid colliders from the set
        _activeRelevantCollisions.RemoveWhere(c => c == null);

        Vector3 center = transform.TransformPoint(sphereCollider.center);
        float radius = Mathf.Max(sphereCollider.radius, 0.1f);
        Collider[] colliders = Physics.OverlapSphere(center, radius);

        bool touchingWall = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider other = colliders[i];
            if (other == null || other.gameObject == gameObject) continue;

            if (other.gameObject.layer == LayerMask.NameToLayer("Wall") &&
                !other.gameObject.name.Contains("Ceil") &&
                !other.gameObject.name.Contains("Floor"))
            {
                touchingWall = true;
                break;
            }
        }

        // If we are not colliding with any relevant line and not touching a wall, ensure color returns to green
        if (_activeRelevantCollisions.Count == 0 && !touchingWall)
        {
            SetRendererColor(this, new Color(0f, 1f, 0f, 0.5f));
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
            // Valid collision if from different parent OR same parent with special Tandem Cover parent
            bool differentParent = this.transform.parent != other.transform.parent;
            bool sameParentTandem = this.transform.parent == other.transform.parent && IsTandemParent(this.transform.parent);
            if (differentParent || sameParentTandem)
            {
                _activeRelevantCollisions.Add(other);
                Debug.Log("Anas => Collision between clearance lines (valid context)");
                SetRendererColor(this, new Color(1f, 0f, 0f, 0.5f)); // Red
                SetRendererColor(other.GetComponent<LineToMeshConverter>(), new Color(1f, 0f, 0f, 0.5f));

                if (UI_ToggleProximityAlerts.IsActive && !Selectable.IsInElevationPhotoMode)
                {
                    if (sameParentTandem)
                    {
                        UI_DialogPrompt.Open("Collision is possible between both arms",
                        new ButtonAction
                        {
                            ButtonText = "Ok",
                            Action = () =>
                            {
                                UI_DialogPrompt.Close();
                            },
                        });
                    }
                    else
                    {
                        string thisName = GetSelectableName(transform);
                        string otherName = GetSelectableName(other.transform);
                        UI_DialogPrompt.Open($"Collision possible between {thisName} and {otherName}",
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
               
            }
        }
        if (other.gameObject.layer == LayerMask.NameToLayer("Wall") && !other.gameObject.name.Contains("Ceil") && !other.gameObject.name.Contains("Floor"))
        {
            Debug.Log("Anas => Touching the Wall!");
            SetRendererColor(this, new Color(1f, 0f, 0f, 0.5f));

            if (UI_ToggleProximityAlerts.IsActive && !Selectable.IsInElevationPhotoMode)
            {
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
     
    }
    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("ClearanceLine"))
        {
            bool differentParent = this.transform.parent != other.transform.parent;
            bool sameParentTandem = this.transform.parent == other.transform.parent && IsTandemParent(this.transform.parent);
            if (differentParent || sameParentTandem)
            {
                _activeRelevantCollisions.Add(other);
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
            _activeRelevantCollisions.Remove(other);
            Debug.Log("Anas not Detected with another LineObject!");
            if (_activeRelevantCollisions.Count == 0)
            {
                SetRendererColor(this, new Color(0, 1, 0, 0.5f)); // Green
            }
            // Optionally set the other to green as well, but it may still be colliding with something else
            // SetRendererColor(other.GetComponent<LineToMeshConverter>(), new Color(0, 1, 0, 0.5f));
        }
        if (other.gameObject.layer == LayerMask.NameToLayer("Wall") && !other.gameObject.name.Contains("Ceil") && !other.gameObject.name.Contains("Floor"))
        {
            Debug.Log("Anas => Exited from wall contact");
            // Only set to green if no relevant line collisions remain
            if (_activeRelevantCollisions.Count == 0)
            {
                SetRendererColor(this, new Color(0, 1, 0, 0.5f));
            }
        }
    }

    private void OnDisable()
    {
        // Ensure we don't leave stale red color when object gets disabled/destroyed
        SetRendererColor(this, new Color(0f, 1f, 0f, 0.5f));
        _activeRelevantCollisions.Clear();
    }

    private void SetRendererColor(LineToMeshConverter renderer, Color color)
    {
        if (renderer == null) return;
        var lr = renderer.GetComponent<LineRenderer>();
        if (lr == null) return;

        // Set LineRenderer colors directly (more reliable than material color alone)
        lr.startColor = color;
        lr.endColor = color;

        // Also try to set material color if supported by the shader
        var mat = lr.material;
        if (mat != null && mat.HasProperty("_Color"))
        {
            mat.color = color;
        }
    }

}
