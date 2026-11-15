using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ClearanceLinesRenderer : MonoBehaviour
{
    #region Non-method Members
    private class MeshVertsData
    {
        public MeshVertsData(MeshFilter meshFilter)
        {
            MeshFilter = meshFilter;
        }

        public MeshFilter MeshFilter { get; }
        public Vector3[] Vertices { get; set; }
        public Vector3[] WorldVertices { get; set; }
    }

    public enum RendererType
    {
        ArmAssembly,
        Door,
        Azurion,
        PetCTscan,
        Allia
    }

    private static readonly float _sizeScalar = 0.0035f;
    private static readonly float _sizeScalarOrtho = 0.005f;
    private static readonly float _sizeScalarOrthoMax = 0.05f;

    private static List<Vector3> _circlePositions = new();

    public LineRenderer _lineRenderer;

    /// <summary>
    /// "Should be \"true\" on heads that can have attachements (i.e. boom head that can have added shelves)"
    /// </summary>
    [field: SerializeField] 
    private bool IncludeChildrenInMeasurement { get; set; }

    /// <summary>
    /// Adds a buffer amount to clearance lines to account for inaccuracies
    /// </summary>
    [field: SerializeField] private float BufferSize { get; set; }

    /// <summary>Only takes XZ data</summary>
    [field: SerializeField]private Transform DoorHinge { get; set; }

    /// <summary>Only takes XZ data</summary>
    [field: SerializeField] private Transform DoorStrike { get; set; }

    [field: SerializeField]
    private float DoorSwingAngle { get; set; } = 90f;

    [field: SerializeField]
    public RendererType Type { get; set; }

    private Selectable _highestSelectable;

    /// <summary> Can be null, be sure to check</summary>
    private Selectable _selectable;
    private List<Selectable> _trackedParentSelectables = new();
    private readonly List<Selectable> _assemblyScaleSubscribed = new();
    private List<MeshVertsData> _meshVertsDatas;
    private List<Vector3> _positions = new();
    private float _highestY;
    private float _lowestY;
    private float _farthestDistance;
    private float _clearanceYOffset;
    private bool _hasClearanceRadius;
    private Vector3 _placedPivot;
    private bool _needsUpdate = true;
    private bool _taskRunning = false;
    private bool _cancelTask = false;

    [SerializeField] float epsilon = 0.00001f;
   public Material renderMaterialColor;

    // Special offsets (in meters) derived from manufacturer guidance
    private const float TOP_HORIZONTAL_EXTRA = 0.150f;      // 150mm
    private const float BOTTOM_SPRING_EXTRA = 0.21216f;     // 212.16mm
    private const float BOTTOM_MOTORIZED_EXTRA = 0.260f; // 260mm

    // Cache last computed extra offset to trigger regen when parts change
    private float _lastSpecialOffset = 0f;
    private string _lastMeasureLog = "";
    private string _lastLoggedClearance = "";
    private Vector3 _petCtLogPos;
    private Vector3 _petCtLogEuler;
    #endregion

    #region Monobehaviour
    private void OnEnable()
    {
        //EventManager.OnChangeColorOfClearanceLineSelectable += CheckCircleIntersections;
        //EventManager.OnDefaultColorClearanceLine += SetDefaultColor;
    }
    private void Awake()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        _selectable = GetComponent<Selectable>();
        //if (BufferSize == 0) // Set default only if it's not set in Inspector
        //{
        //    BufferSize = 0.05f;
        //}
    }

    private void OnDestroy()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        Unsubscribe();
        if (_lineRenderer != null)
            Destroy(_lineRenderer.gameObject);
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        Subscribe();

        if (Type == RendererType.ArmAssembly)
        {
            Selectable probe = _selectable != null
                ? _selectable
                : (_trackedParentSelectables.Count > 0 ? _trackedParentSelectables[0] : GetComponentInParent<Selectable>());
            if (probe != null && probe.TryGetArmAssemblyRoot(out GameObject armAssemblyRoot))
            {
                _highestSelectable = armAssemblyRoot.GetComponent<Selectable>();
                RefreshAssemblyScaleSubscriptions();
            }
            else
            {
                Debug.LogError(
                    $"[Clearance] ArmAssembly CLR on '{name}' has no mount root — clearance disabled.",
                    this);
                enabled = false;
                return;
            }

        }
        CheckStatus();
    }

    private void Update()
    {
        //if (!UI_ToggleClearanceLines.IsActive) return;

        if (Type == RendererType.PetCTscan && UI_ToggleClearanceLines.IsActive)
            _needsUpdate = true;

        if (_needsUpdate && !_taskRunning)
        {
            UpdateLineRenderer();
        }

        if (_lineRenderer == null)
            return;

        var cam = Camera.main;
        if (cam == null)
            return;

        if (FreeLookCam.IsActive)
        {
            float distanceToCamera = Vector3.Distance(gameObject.transform.position, cam.transform.position);
            _lineRenderer.startWidth = _sizeScalar * distanceToCamera;
            _lineRenderer.endWidth = _sizeScalar * distanceToCamera;
        }
        else
        {
            float size = Mathf.Min(_sizeScalarOrthoMax, _sizeScalarOrtho * cam.orthographicSize);
            _lineRenderer.startWidth = size;
            _lineRenderer.endWidth = size;
        }

        if (Type == RendererType.ArmAssembly && _hasClearanceRadius
            && _lineRenderer.gameObject.activeInHierarchy)
        {
            Vector3 pivot = GetMountStack().position;
            if ((pivot - _placedPivot).sqrMagnitude > 0.000001f)
                PlaceArmCircle();
        }

        // For arm assemblies, if special-offset presence changes (parts added/removed), regenerate
        if (Type == RendererType.ArmAssembly && _highestSelectable != null)
        {
            float currentOffset = GetArmAssemblyExtraOffset();
            if (!Mathf.Approximately(currentOffset, _lastSpecialOffset))
            {
                _lastSpecialOffset = currentOffset;
                SetNeedsUpdate();
            }
        }
    }
    #endregion

    #region Events
    private void Subscribe()
    {
        var parent = transform.parent;

        while (parent != null)
        {
            if (parent.TryGetComponent(out Selectable selectable))
            {
                _trackedParentSelectables.Add(selectable);
            }

            parent = parent.parent;
        }

        _trackedParentSelectables.ForEach(selectable =>
        {
            selectable.ScaleUpdated.AddListener(SetNeedsUpdate);
        });

        UI_ToggleClearanceLines.ClearanceLinesToggled.AddListener(CheckStatus);

        // React to selectables being added/removed in scene (so we can detect our target arm parts)
        Selectable.ActiveSelectablesInSceneChanged.AddListener(SetNeedsUpdate);
    }

    private void Unsubscribe()
    {
        _trackedParentSelectables.ForEach(selectable =>
        {
            if (selectable != null && !selectable.IsDestroyed)
            {
                selectable.ScaleUpdated.RemoveListener(SetNeedsUpdate);
            }
        });
        ClearAssemblyScaleSubscriptions();
        UI_ToggleClearanceLines.ClearanceLinesToggled.RemoveListener(CheckStatus);
        Selectable.ActiveSelectablesInSceneChanged.RemoveListener(SetNeedsUpdate);
    }

    /// <summary>
    /// Parent-chain ScaleUpdated misses sibling parts on this mount.
    /// Subscribe this stack only, so the other tandem mount keeps its own ring.
    /// </summary>
    private void RefreshAssemblyScaleSubscriptions()
    {
        ClearAssemblyScaleSubscriptions();
        if (_highestSelectable == null)
            return;

        foreach (var sel in GetMountStack().GetComponentsInChildren<Selectable>(true))
        {
            if (sel == null || _trackedParentSelectables.Contains(sel))
                continue;
            sel.ScaleUpdated.AddListener(SetNeedsUpdate);
            _assemblyScaleSubscribed.Add(sel);
        }
    }

    private void ClearAssemblyScaleSubscriptions()
    {
        foreach (var sel in _assemblyScaleSubscribed)
        {
            if (sel != null && !sel.IsDestroyed)
                sel.ScaleUpdated.RemoveListener(SetNeedsUpdate);
        }
        _assemblyScaleSubscribed.Clear();
    }

    [RuntimeInitializeOnLoadMethod]
    private static void OnAppStart()
    {
        for (int i = 0; i < 361; i++)
        {
            _circlePositions.Add(Quaternion.AngleAxis(i, Vector3.up) * Vector3.forward);
        }
    }
    #endregion

    #region Logic
    private void CheckStatus()
    {
        if (Type == RendererType.ArmAssembly && _highestSelectable == null)
            return;

        if (_lineRenderer == null)
        {
            string prefabPath = Type == RendererType.PetCTscan
                ? "Prefabs/ClearanceLinesRendererRectangle"
                : "Prefabs/ClearanceLinesRenderer";
            var prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab == null)
                return;
            Transform parent = Type == RendererType.ArmAssembly
                ? _highestSelectable.transform
                : Type == RendererType.PetCTscan
                    ? transform
                    : transform.root;
            var newObj = Instantiate(prefab, parent);
            newObj.name = gameObject.name;
            if (Type == RendererType.PetCTscan)
            {
                newObj.transform.localPosition = Vector3.zero;
                newObj.transform.localRotation = Quaternion.identity;
                newObj.transform.localScale = Vector3.one;
            }
            else
            {
                newObj.transform.rotation = Quaternion.identity;
            }
            _lineRenderer = newObj.GetComponent<LineRenderer>();
            if (_lineRenderer == null)
                return;
        }
        _lineRenderer.gameObject.SetActive(UI_ToggleClearanceLines.IsActive);


#if UNITY_EDITOR
        if (Type == RendererType.Door)
        {
            _needsUpdate = true;
        }
#endif

        if (UI_ToggleClearanceLines.IsActive && _needsUpdate)
        {
            if (!_taskRunning)
            {
                UpdateLineRenderer();
            }
        }
    }

    private void ResetMeshVertsData()
    {
        if (_meshVertsDatas == null)
        {
            _meshVertsDatas = new();
            MeshFilter[] meshFilters = CollectMeshFiltersForClearance();
            for (int j = 0; j < meshFilters.Length; j++)
            {
                var filter = meshFilters[j];
                if (filter == null || filter.sharedMesh == null)
                    continue;
                Mesh mesh = filter.sharedMesh;
                // Bundled meshes are sometimes non-readable — .vertices would throw.
                if (!mesh.isReadable)
                    continue;
                Vector3[] verts;
                try
                {
                    verts = mesh.vertices;
                }
                catch (Exception)
                {
                    continue;
                }
                if (verts == null || verts.Length == 0)
                    continue;

                var data = new MeshVertsData(filter)
                {
                    Vertices = verts,
                };
                BakeWorldVertices(data);
                _meshVertsDatas.Add(data);
            }
        }
    }

    /// <summary>
    /// lossyScale is not local scale once a parent is rotated. Multiplying verts by it
    /// blew the ring up when an arm length changed (attach local Z jumped past 3).
    /// </summary>
    private static void BakeWorldVertices(MeshVertsData data)
    {
        var filter = data.MeshFilter;
        var verts = data.Vertices;
        if (filter == null || verts == null)
            return;

        var world = data.WorldVertices;
        if (world == null || world.Length != verts.Length)
            world = new Vector3[verts.Length];

        Transform tr = filter.transform;
        for (int i = 0; i < verts.Length; i++)
            world[i] = tr.TransformPoint(verts[i]);
        data.WorldVertices = world;
    }

    /// <summary>
    /// The direct child of the ceiling mount that this ring hangs from.
    /// A tandem cover has two of these, and each one keeps its own circle.
    /// </summary>
    private Transform GetMountStack()
    {
        if (_highestSelectable == null)
            return transform;

        Transform root = _highestSelectable.transform;
        if (transform == root)
            return transform;

        Transform stack = transform;
        while (stack.parent != null && stack.parent != root)
            stack = stack.parent;
        return stack.parent == root ? stack : transform;
    }

    /// <summary>
    /// Arm assemblies: meshes under this mount stack only. A tandem cover's boom
    /// and light are separate stacks and must not share a radius or a center.
    /// Other types: keep the authored IncludeChildren / self MeshFilter behavior.
    /// </summary>
    private MeshFilter[] CollectMeshFiltersForClearance()
    {
        if (Type == RendererType.ArmAssembly && _highestSelectable != null)
        {
            return GetMountStack()
                .GetComponentsInChildren<MeshFilter>(true)
                .Where(ShouldIncludeArmAssemblyMesh)
                .ToArray();
        }

        if (IncludeChildrenInMeasurement)
            return GetComponentsInChildren<MeshFilter>()
                .Where(mf => mf != null && mf.sharedMesh != null)
                .ToArray();

        var self = GetComponent<MeshFilter>();
        return self != null && self.sharedMesh != null
            ? new[] { self }
            : Array.Empty<MeshFilter>();
    }

    private static bool ShouldIncludeArmAssemblyMesh(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null)
            return false;
        if (!mf.gameObject.activeInHierarchy)
            return false;

        // Helper / overlay geometry — not part of equipment sweep.
        string n = mf.gameObject.name ?? "";
        if (n.IndexOf("Sphere", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (n.IndexOf("Measur", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (n.IndexOf("Clearance", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        // LineRenderer overlays (dims / clearance strokes) sometimes carry filters.
        if (mf.GetComponent<LineRenderer>() != null)
            return false;

        return true;
    }

    /// <summary>
    /// One ring per mount stack. Sibling rings on the same stack hide; the other
    /// stack on a tandem cover keeps its own ring.
    /// </summary>
    private bool IsPrimaryArmAssemblyClearance()
    {
        if (Type != RendererType.ArmAssembly || _highestSelectable == null)
            return true;

        Transform stack = GetMountStack();
        ClearanceLinesRenderer primary = null;
        int bestId = int.MaxValue;
        foreach (var clr in stack.GetComponentsInChildren<ClearanceLinesRenderer>(true))
        {
            if (clr == null || !clr.isActiveAndEnabled)
                continue;
            if (clr.Type != RendererType.ArmAssembly)
                continue;
            int id = clr.GetInstanceID();
            if (id < bestId)
            {
                bestId = id;
                primary = clr;
            }
        }
        return primary == this;
    }

    private void SetNeedsUpdate()
    {
        _needsUpdate = true;
        // A scale during the sweep discards that pass and runs again next frame.
        if (_taskRunning)
            _cancelTask = true;
        else
            _meshVertsDatas = null;

        if (Type == RendererType.ArmAssembly && _highestSelectable != null && !_taskRunning)
            RefreshAssemblyScaleSubscriptions();

        CheckStatus();
    }

    public void UpdateLineRenderer()
    {
        if (Type == RendererType.ArmAssembly)
        {
            UpdateLineRendererArmAssembly();
        }
        else if (Type == RendererType.Door)
        {
            UpdateLineRendererDoor();
        }
        else if (Type == RendererType.Azurion)
        {
            UpdateLineRendererAzurion();
        }
        else if (Type == RendererType.PetCTscan)
        {
            UpdateLineRendererBedScanner();
        }
        else if (Type == RendererType.Allia)
        {
            // Do NOT draw rectangle for Allia; use Azurion-style (circular) rendering
            UpdateLineRendererBedScannerAllia();
        }
    }

    // Computes additional offset for ArmAssembly based on presence of specific child selectables
    private float GetArmAssemblyExtraOffset()
    {
        if (Type != RendererType.ArmAssembly || _highestSelectable == null) return 0f;

        var root = GetMountStack();
        var childSelectables = root.GetComponentsInChildren<Selectable>(true);
        float offset = 0f;

        foreach (var sel in childSelectables)
        {
            string n = sel.gameObject.name;
            // Top horizontal powered XL => +150mm

            if (n.IndexOf("BoomSegment_1", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                offset = Mathf.Max(offset, TOP_HORIZONTAL_EXTRA);
            }
            if (n.IndexOf("BoomSegment_2PoweredXL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                offset = Mathf.Max(offset, BOTTOM_MOTORIZED_EXTRA);
            }
            if (n.IndexOf("BoomSegment_2Powered", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                offset = Mathf.Max(offset, BOTTOM_MOTORIZED_EXTRA);
            }
            // Bottom spring XXL => +212.16mm
            if (n.IndexOf("1000SH_XXL_SpringBottom", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                offset = Mathf.Max(offset, BOTTOM_SPRING_EXTRA);
            }
        }

        return offset;
    }


    private static bool IsClearanceHelper(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return name.IndexOf("Sphere", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Measur", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Clearance", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// The nearest part that is allowed to turn. A child that cannot turn does not hide its parent.
    /// </summary>
    private Selectable NearestTurningPart(Transform t)
    {
        while (t != null)
        {
            if (t.TryGetComponent(out Selectable selectable)
                && selectable != _highestSelectable
                && selectable.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z))
                return selectable;
            t = t.parent;
        }
        return null;
    }

    /// <summary>
    /// Full turn around that part's own pivot, then distance from this hole.
    /// Same result as spinning the point, without a 361-step loop.
    /// A part that cannot turn keeps the distance of where it sits.
    /// </summary>
    private float ReachFromHole(Vector3 hole, Vector3 point, Selectable turningPart)
    {
        if (turningPart == null)
            return HorizontalDistance(hole, point);

        Vector3 partPivot = turningPart.transform.position;
        return HorizontalDistance(hole, partPivot) + HorizontalDistance(partPivot, point);
    }

    /// <summary>
    /// A farther point starts the height over. A point at the same reach widens it,
    /// so the ring sits in the middle of the points that set the size.
    /// </summary>
    private bool NoteReach(float reach, float y, string meshName, Selectable owner, ref string farthestName, ref string farthestOwner)
    {
        if (reach + epsilon < _farthestDistance)
            return false;

        if (reach > _farthestDistance + epsilon)
        {
            _farthestDistance = reach;
            _highestY = y;
            _lowestY = y;
            farthestName = meshName;
            farthestOwner = owner != null ? owner.name : "";
        }
        else
        {
            if (reach > _farthestDistance)
                _farthestDistance = reach;
            if (y > _highestY)
                _highestY = y;
            if (y < _lowestY)
                _lowestY = y;
        }
        return true;
    }

    /// <summary>
    /// One circle per hole. Radius is the full turn of whatever is on that arm, including the head.
    /// </summary>
    private bool MeasureVisibleReach(Vector3 pivot)
    {
        Transform stack = GetMountStack();
        Renderer[] renderers = stack.GetComponentsInChildren<Renderer>(true);
        int counted = 0;
        string farthestName = "";
        string farthestOwner = "";

        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            if (renderer is LineRenderer || renderer is ParticleSystemRenderer)
                continue;
            string n = renderer.gameObject.name ?? "";
            if (IsClearanceHelper(n))
                continue;

            Selectable owner = NearestTurningPart(renderer.transform);
            bool used = false;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null && mesh.isReadable)
            {
                Vector3[] verts;
                try { verts = mesh.vertices; }
                catch (Exception) { verts = null; }
                if (verts != null && verts.Length > 0)
                {
                    used = true;
                    Transform tr = renderer.transform;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 v = tr.TransformPoint(verts[i]);
                        float reach = ReachFromHole(pivot, v, owner);
                        if (!NoteReach(reach, v.y, n, owner, ref farthestName, ref farthestOwner))
                            continue;
                    }
                }
            }

            if (used)
            {
                counted++;
                continue;
            }

            Bounds b = renderer.bounds;
            Vector3 c = b.center;
            Vector3 e = b.extents;
            bool any = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                float reach = ReachFromHole(pivot, corner, owner);
                if (!NoteReach(reach, corner.y, n, owner, ref farthestName, ref farthestOwner))
                    continue;
                any = true;
            }
            if (any)
                counted++;
        }

        float midY = (_highestY + _lowestY) * 0.5f;
        _lastMeasureLog = $"pivot='{stack.name}' farthest='{farthestName}' owner='{farthestOwner}' reach={_farthestDistance:F2}m height={midY:F2}m";
        return counted > 0 && _farthestDistance > 0f;
    }

    private void UpdateLineRendererArmAssembly()
    {
        if (!IsPrimaryArmAssemblyClearance())
        {
            if (_lineRenderer != null)
                _lineRenderer.gameObject.SetActive(false);
            _hasClearanceRadius = false;
            _needsUpdate = false;
            _taskRunning = false;
            _cancelTask = false;
            return;
        }

        _taskRunning = true;
        _cancelTask = false;
        _highestY = float.MinValue;
        _lowestY = float.MaxValue;
        _farthestDistance = 0f;

        if (_lineRenderer == null || !MeasureVisibleReach(GetMountStack().position))
        {
            _hasClearanceRadius = false;
            _needsUpdate = false;
            _taskRunning = false;
            return;
        }

        _taskRunning = false;
        if (_cancelTask)
        {
            _cancelTask = false;
            _needsUpdate = true;
            return;
        }

        float specialOffset = GetArmAssemblyExtraOffset();
        _lastSpecialOffset = specialOffset;
        _farthestDistance += specialOffset + BufferSize;
        string clearanceLog = $"[Clearance] {_lastMeasureLog} drawn={_farthestDistance:F2}m extra={specialOffset:F2}m";
        if (clearanceLog != _lastLoggedClearance)
        {
            _lastLoggedClearance = clearanceLog;
            Debug.Log(clearanceLog, this);
        }

        Vector3 pivot = GetMountStack().position;
        float midY = (_highestY + _lowestY) * 0.5f;
        _clearanceYOffset = midY - pivot.y;
        _hasClearanceRadius = true;

        if (UI_ToggleClearanceLines.IsActive)
            _lineRenderer.gameObject.SetActive(true);
        PlaceArmCircle();
        _needsUpdate = false;
    }

    /// <summary>
    /// Radius comes from the mesh sweep. The center is this mount's live pivot,
    /// so dragging the boom does not leave the circle where the sweep last ran.
    /// </summary>
    private void PlaceArmCircle()
    {
        if (_lineRenderer == null || !_hasClearanceRadius)
            return;

        Vector3 pivot = GetMountStack().position;
        _placedPivot = pivot;
        _lineRenderer.useWorldSpace = true;
        if (_positions == null || _positions.Count != _circlePositions.Count)
            _positions = new List<Vector3>(_circlePositions);

        float y = pivot.y + _clearanceYOffset;
        for (int i = 0; i < _circlePositions.Count; i++)
        {
            Vector3 dir = _circlePositions[i];
            _positions[i] = new Vector3(
                pivot.x + dir.x * _farthestDistance,
                y,
                pivot.z + dir.z * _farthestDistance);
        }

        _lineRenderer.positionCount = _positions.Count;
        _lineRenderer.SetPositions(_positions.ToArray());
    }

    private void UpdateLineRendererDoor()
    {
        _positions.Clear();
        // draw a vector from hinge to strike
        float doorLength = (DoorStrike.transform.position - DoorHinge.transform.position).magnitude;
        _lineRenderer.transform.position = DoorHinge.transform.position;
        DoorHinge.LookAt(DoorStrike.transform.position, Vector3.up);
        _lineRenderer.transform.rotation = Quaternion.identity;
        _positions.Add(Vector3.zero);
        for (int i = 0; i <= Math.Abs(DoorSwingAngle); i++)
        {
            DoorHinge.RotateAround(DoorHinge.transform.position, Vector3.up, 1f * Mathf.Sign(DoorSwingAngle));
            _positions.Add(DoorHinge.transform.forward * doorLength);
        }
        _positions.Add(Vector3.zero);

        _lineRenderer.positionCount = _positions.Count;
        _lineRenderer.SetPositions(_positions.ToArray());

        _needsUpdate = false;
    }



    private static bool IsBakedClearanceOverlay(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return name.IndexOf("Bezier", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Bézier", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Nurbs", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Clearance", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Omni Legend PIM Fig.14 — table travel with 2 m scan-range option.</summary>
    private const float PetCtTableTravelM = 2.188f;

    /// <summary>Omni Legend PIM Fig.38 **** — 2 m extender past the gantry rear (350 mm is the short cradle extender).</summary>
    private const float PetCtCradleExtenderM = 0.650f;

    /// <summary>Omni Legend PIM Fig.38 — patient table width.</summary>
    private const float PetCtTableWidthM = 0.914f;

    private void UpdateLineRendererBedScanner()
    {
        if (_lineRenderer == null)
            return;

        if (_lineRenderer.transform.parent != transform)
            _lineRenderer.transform.SetParent(transform, false);
        _lineRenderer.useWorldSpace = false;
        _lineRenderer.transform.localPosition = Vector3.zero;
        _lineRenderer.transform.localRotation = Quaternion.identity;
        _lineRenderer.transform.localScale = Vector3.one;

        // Floor place uses LookRotation(hit.normal): local Z = world up, local Y = table/bore on XZ.
        // Mesh AABBs through prefab TRS then Rx(-90): height is world Y (0.13..2.27),
        // table/bore is ProjectOnPlane(transform.up) (prefab Y -2.25..+2.65), width is transform.right.
        Vector3 origin = transform.position;
        float floorY = origin.y + 0.05f;
        Vector3 travel = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        Vector3 lateral = Vector3.ProjectOnPlane(transform.right, Vector3.up);
        if (travel.sqrMagnitude < 1e-4f)
            travel = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (travel.sqrMagnitude < 1e-4f)
            return;
        travel.Normalize();
        if (lateral.sqrMagnitude < 1e-4f)
            lateral = Vector3.Cross(Vector3.up, travel);
        lateral.Normalize();

        float allMinT = float.PositiveInfinity, allMaxT = float.NegativeInfinity;
        float tableMinT = float.PositiveInfinity, tableMaxT = float.NegativeInfinity;
        float tableMinL = float.PositiveInfinity, tableMaxL = float.NegativeInfinity;
        float gantryMinT = float.PositiveInfinity, gantryMaxT = float.NegativeInfinity;
        bool anyTable = false;
        bool anyGantry = false;

        var filters = GetComponentsInChildren<MeshFilter>(true);
        for (int f = 0; f < filters.Length; f++)
        {
            var mf = filters[f];
            if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy)
                continue;
            if (IsBakedClearanceOverlay(mf.gameObject.name))
                continue;
            if (mf.GetComponent<LineRenderer>() != null)
                continue;

            Bounds b = mf.sharedMesh.bounds;
            Vector3 c = b.center;
            Vector3 e = b.extents;
            float minT = float.PositiveInfinity, maxT = float.NegativeInfinity;
            float minL = float.PositiveInfinity, maxL = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 meshLocal = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 world = mf.transform.TransformPoint(meshLocal);
                Vector3 d = world - origin;
                float tt = Vector3.Dot(d, travel);
                float ll = Vector3.Dot(d, lateral);
                if (tt < minT) minT = tt;
                if (tt > maxT) maxT = tt;
                if (ll < minL) minL = ll;
                if (ll > maxL) maxL = ll;
            }

            if (minT < allMinT) allMinT = minT;
            if (maxT > allMaxT) allMaxT = maxT;

            float travelSpan = maxT - minT;
            float lateralSpan = maxL - minL;
            if (travelSpan > 1.5f && lateralSpan < 1.2f)
            {
                anyTable = true;
                if (minT < tableMinT) tableMinT = minT;
                if (maxT > tableMaxT) tableMaxT = maxT;
                if (minL < tableMinL) tableMinL = minL;
                if (maxL > tableMaxL) tableMaxL = maxL;
            }
            else if (lateralSpan >= 1.2f)
            {
                anyGantry = true;
                if (minT < gantryMinT) gantryMinT = minT;
                if (maxT > gantryMaxT) gantryMaxT = maxT;
            }
        }

        if (float.IsInfinity(allMinT))
            return;

        float startT;
        float endT;
        float centerL;
        if (anyTable)
        {
            startT = tableMinT;
            endT = tableMaxT;
            centerL = (tableMinL + tableMaxL) * 0.5f;
            // Fig.14 dashed pallet + Fig.38 item 3: when fully in, the cradle exits the gantry rear.
            if (anyGantry)
            {
                float tableMid = (tableMinT + tableMaxT) * 0.5f;
                float gantryMid = (gantryMinT + gantryMaxT) * 0.5f;
                float farGantryT = tableMid > gantryMid ? gantryMinT : gantryMaxT;
                float extenderT = tableMid > gantryMid
                    ? farGantryT - PetCtCradleExtenderM
                    : farGantryT + PetCtCradleExtenderM;
                if (extenderT < startT) startT = extenderT;
                if (extenderT > endT) endT = extenderT;
            }
        }
        else
        {
            bool tablePositive = Mathf.Abs(allMaxT) >= Mathf.Abs(allMinT);
            startT = 0f;
            endT = tablePositive
                ? Mathf.Max(allMaxT, PetCtTableTravelM)
                : Mathf.Min(allMinT, -PetCtTableTravelM);
            centerL = 0f;
        }

        float halfW = PetCtTableWidthM * 0.5f + BufferSize;

        Vector3 At(float tt, float ll)
        {
            Vector3 p = origin + travel * tt + lateral * ll;
            p.y = floorY;
            return p;
        }

        _positions.Clear();
        _positions.Add(transform.InverseTransformPoint(At(startT, centerL - halfW)));
        _positions.Add(transform.InverseTransformPoint(At(startT, centerL + halfW)));
        _positions.Add(transform.InverseTransformPoint(At(endT, centerL + halfW)));
        _positions.Add(transform.InverseTransformPoint(At(endT, centerL - halfW)));
        _positions.Add(transform.InverseTransformPoint(At(startT, centerL - halfW)));

        _lineRenderer.positionCount = _positions.Count;
        _lineRenderer.SetPositions(_positions.ToArray());
        _lineRenderer.textureMode = LineTextureMode.Tile;
        if (renderMaterialColor != null)
            _lineRenderer.material = renderMaterialColor;

        if ((origin - _petCtLogPos).sqrMagnitude > 0.01f
            || (transform.eulerAngles - _petCtLogEuler).sqrMagnitude > 1f)
        {
            _petCtLogPos = origin;
            _petCtLogEuler = transform.eulerAngles;
            Debug.Log(
                $"[PetCT-CLR] pos={origin} euler={transform.eulerAngles} floorY={floorY:F3} " +
                $"fwd={transform.forward} up={transform.up} right={transform.right} " +
                $"travel={travel} table={anyTable} gantry={anyGantry} startT={startT:F2} endT={endT:F2} " +
                $"allT=[{allMinT:F2},{allMaxT:F2}] p0={_positions[0]} p2={_positions[2]}");
        }

        _needsUpdate = false;
    }


    private void UpdateLineRendererBedScannerAllia()
    {
        _positions.Clear();

        // Rectangle size
        float length = 5f; // Total length (opposite to X)
        float width = 0.5f; // Width (Z-wise, centered)
        float yOffset = 0.2f;

        // Base position at object's center + upward offset
        Vector3 baseCenter = transform.position + Vector3.up * yOffset;

        // Directional vectors
        Vector3 left = transform.right * length;               // Full length toward X
        Vector3 forward = new Vector3(0, width * 0.5f, 0);     // Half width on each side (Z)

        // Define corners
        Vector3 corner1 = baseCenter - forward;                 // Front-left (near center)
        Vector3 corner2 = baseCenter + forward;                 // Front-right (near center)
        Vector3 corner3 = baseCenter + left + forward;          // Back-right
        Vector3 corner4 = baseCenter + left - forward;          // Back-left

        _positions.Add(corner1);
        _positions.Add(corner2);
        _positions.Add(corner3);
        _positions.Add(corner4);
        _positions.Add(corner1); // Close the loop

        _lineRenderer.positionCount = _positions.Count;
        _lineRenderer.SetPositions(_positions.ToArray());

        _lineRenderer.widthMultiplier = 0.01f;
        _lineRenderer.textureMode = LineTextureMode.Tile;

        if (renderMaterialColor != null)
        {
            _lineRenderer.material = renderMaterialColor;
        }

        _needsUpdate = false;
    }

    private void UpdateLineRendererAzurion()
    {
        if (_lineRenderer == null) return;

        _positions.Clear();

        Bounds bounds = GetComponentInChildren<Renderer>().bounds;

        Vector3 center = transform.position;

        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + BufferSize;

        Quaternion rotation = Quaternion.Euler(-90f, 0f, 0f);

        foreach (var pos in _circlePositions)
        {
            Vector3 localPoint = new Vector3(pos.x * radius, bounds.center.y, pos.z * radius);

            Vector3 rotatedPoint = rotation * localPoint;

            Vector3 worldPoint = center + rotatedPoint;

            _positions.Add(worldPoint);
        }

        _positions.Add(_positions[0]);

        _lineRenderer.positionCount = _positions.Count;
        _lineRenderer.SetPositions(_positions.ToArray());

        if (!UI_ToggleClearanceLines.IsActive) 
        {
            _lineRenderer.transform.localPosition = new Vector3(0,0,2f);
        }
        _needsUpdate = false;
    }
    #endregion
}