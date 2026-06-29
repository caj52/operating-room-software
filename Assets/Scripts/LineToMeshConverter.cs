using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class LineToMeshConverter : MonoBehaviour
{
    private static readonly int WallLayer = LayerMask.NameToLayer("Wall");
    private static readonly Collider[] OverlapBuffer = new Collider[32];

    private LineRenderer lineRenderer;
    private MeshFilter meshFilter;
    private SphereCollider sphereCollider;
    private Mesh mesh;
    private MaterialPropertyBlock _colorBlock;

    public float lineThickness = 0.1f;
    public float widthScale = 0.5f;
    public int circleResolution = 8;
    Selectable selectable;

    private const string TandemCoverName = "Boom - Tandem Ceiling Cover";

    private readonly HashSet<Collider> _activeRelevantCollisions = new HashSet<Collider>();

    private Vector3[] _cachedLinePositions;
    private Vector3[] _previousLinePositions;
    private int _cachedPointCount;
    private Quaternion _cachedRotation;
    private Vector3[] _verticesBuffer;
    private int[] _trianglesBuffer;
    private Color _currentColor = new Color(0f, 1f, 0f, 0.5f);

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        meshFilter = GetComponent<MeshFilter>();
        selectable = transform.parent.GetComponent<Selectable>();

        sphereCollider = GetComponent<SphereCollider>();
        if (sphereCollider == null)
            sphereCollider = gameObject.AddComponent<SphereCollider>();
        sphereCollider.isTrigger = true;

        if (mesh == null)
        {
            mesh = new Mesh();
            meshFilter.mesh = mesh;
        }

        SetRendererColor(this, _currentColor);
        UpdateMeshIfDirty();
    }

    void Update()
    {
        UpdateMeshIfDirty();
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

        _activeRelevantCollisions.RemoveWhere(c => c == null);

        Vector3 center = transform.TransformPoint(sphereCollider.center);
        float radius = Mathf.Max(sphereCollider.radius, 0.1f);
        int count = Physics.OverlapSphereNonAlloc(center, radius, OverlapBuffer);

        bool touchingWall = false;

        for (int i = 0; i < count; i++)
        {
            Collider other = OverlapBuffer[i];
            if (other == null || other.gameObject == gameObject) continue;

            if (other.gameObject.layer == WallLayer &&
                !other.gameObject.name.Contains("Ceil") &&
                !other.gameObject.name.Contains("Floor"))
            {
                touchingWall = true;
                break;
            }
        }

        if (_activeRelevantCollisions.Count == 0 && !touchingWall)
            SetRendererColor(this, new Color(0f, 1f, 0f, 0.5f));
    }

    void UpdateMeshIfDirty()
    {
        int pointCount = lineRenderer.positionCount;
        if (pointCount < 2) return;

        if (_cachedLinePositions == null || _cachedLinePositions.Length < pointCount)
            _cachedLinePositions = new Vector3[pointCount];

        lineRenderer.GetPositions(_cachedLinePositions);

        Quaternion rotation = transform.rotation;
        if (PositionsUnchanged(_cachedLinePositions, pointCount, rotation))
            return;

        if (_previousLinePositions == null || _previousLinePositions.Length < pointCount)
            _previousLinePositions = new Vector3[pointCount];
        for (int i = 0; i < pointCount; i++)
            _previousLinePositions[i] = _cachedLinePositions[i];

        _cachedPointCount = pointCount;
        _cachedRotation = rotation;
        GenerateMesh(_cachedLinePositions, pointCount);
    }

    private bool PositionsUnchanged(Vector3[] positions, int count, Quaternion rotation)
    {
        if (_cachedPointCount != count || _cachedRotation != rotation)
            return false;

        if (_previousLinePositions == null || _previousLinePositions.Length < count)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (_previousLinePositions[i] != positions[i])
                return false;
        }

        return true;
    }

    void GenerateMesh(Vector3[] positions, int pointCount)
    {
        int segmentCount = pointCount - 1;
        int vertexCount = segmentCount * circleResolution * 2;
        int triangleCount = segmentCount * circleResolution * 6;

        if (_verticesBuffer == null || _verticesBuffer.Length < vertexCount)
            _verticesBuffer = new Vector3[vertexCount];
        if (_trianglesBuffer == null || _trianglesBuffer.Length < triangleCount)
            _trianglesBuffer = new int[triangleCount];

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

                _verticesBuffer[i * circleResolution + j] = positions[i] + up * x + right * z;
                _verticesBuffer[i * circleResolution + j + circleResolution] = positions[i + 1] + up * x + right * z;
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

                _trianglesBuffer[baseIndex] = front1;
                _trianglesBuffer[baseIndex + 1] = back1;
                _trianglesBuffer[baseIndex + 2] = front2;

                _trianglesBuffer[baseIndex + 3] = front2;
                _trianglesBuffer[baseIndex + 4] = back1;
                _trianglesBuffer[baseIndex + 5] = back2;
            }
        }

        mesh.Clear();
        mesh.SetVertices(_verticesBuffer, 0, vertexCount);
        mesh.SetTriangles(_trianglesBuffer, 0, triangleCount, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (sphereCollider != null)
        {
            sphereCollider.center = mesh.bounds.center;
            sphereCollider.radius = Mathf.Max(mesh.bounds.extents.x, mesh.bounds.extents.y, mesh.bounds.extents.z);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("ClearanceLine"))
        {
            bool differentParent = this.transform.parent != other.transform.parent;
            bool sameParentTandem = this.transform.parent == other.transform.parent && IsTandemParent(this.transform.parent);
            if (differentParent || sameParentTandem)
            {
                _activeRelevantCollisions.Add(other);
                Debug.Log("Anas => Collision between clearance lines (valid context)");
                SetRendererColor(this, new Color(1f, 0f, 0f, 0.5f));
                SetRendererColor(other.GetComponent<LineToMeshConverter>(), new Color(1f, 0f, 0f, 0.5f));

                if (UI_ToggleProximityAlerts.IsActive)
                {
                    if (sameParentTandem)
                    {
                        UI_DialogPrompt.Open("Collision is possible between both arms",
                        new ButtonAction
                        {
                            ButtonText = "Ok",
                            Action = () => { UI_DialogPrompt.Close(); },
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
                            Action = () => { UI_DialogPrompt.Close(); },
                        });
                    }
                }
            }
        }
        if (other.gameObject.layer == WallLayer && !other.gameObject.name.Contains("Ceil") && !other.gameObject.name.Contains("Floor"))
        {
            Debug.Log("Anas => Touching the Wall!");
            SetRendererColor(this, new Color(1f, 0f, 0f, 0.5f));

            if (UI_ToggleProximityAlerts.IsActive)
            {
                UI_DialogPrompt.Open($"{selectable.UIButtonName} is touching the wall!",
                new ButtonAction
                {
                    ButtonText = "Ok",
                    Action = () => { UI_DialogPrompt.Close(); },
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
                SetRendererColor(this, new Color(1f, 0f, 0f, 0.5f));
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
                SetRendererColor(this, new Color(0, 1, 0, 0.5f));
        }
        if (other.gameObject.layer == WallLayer && !other.gameObject.name.Contains("Ceil") && !other.gameObject.name.Contains("Floor"))
        {
            Debug.Log("Anas => Exited from wall contact");
            if (_activeRelevantCollisions.Count == 0)
                SetRendererColor(this, new Color(0, 1, 0, 0.5f));
        }
    }

    private void OnDisable()
    {
        SetRendererColor(this, new Color(0f, 1f, 0f, 0.5f));
        _activeRelevantCollisions.Clear();
    }

    private void SetRendererColor(LineToMeshConverter renderer, Color color)
    {
        if (renderer == null) return;
        var lr = renderer.lineRenderer != null ? renderer.lineRenderer : renderer.GetComponent<LineRenderer>();
        if (lr == null) return;

        if (renderer._currentColor == color) return;
        renderer._currentColor = color;

        MaterialColorUtility.SetLineRendererColor(lr, color, ref renderer._colorBlock);
    }
}
