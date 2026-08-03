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
        public MeshVertsData(MeshFilter meshFilter, ClearanceLinesRenderer clearanceLinesRenderer)
        {
            MeshFilter = meshFilter;
            _clearanceLinesRenderer = clearanceLinesRenderer;
        }

        public MeshFilter MeshFilter { get; }
        public Quaternion Rotation { get; set; }
        public Vector3 GlobalPosition { get; set; }
        public Vector3[] Vertices { get; set; }
        public int Rotations { get; set; } = 1;
        public Vector3 LossyScale { get; set; }
        private int? _hierarchyNestedLevel;
        public int HierarchyNestedLevel
        { 
            get 
            { 
                _hierarchyNestedLevel ??= GetHierarchyNestedLevel();
                return (int)_hierarchyNestedLevel;
            }
        }
        private ClearanceLinesRenderer _clearanceLinesRenderer;

        private int GetHierarchyNestedLevel()
        {
            int level = 0;
            Transform parent = MeshFilter.transform.parent;
            while (parent != null)
            {
                parent = parent.parent;
                level++;
            }
            return level;
        }
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
    /// <summary>Snapshot held for the in-flight parallel sweep (must not be nulled mid-task).</summary>
    private List<MeshVertsData> _recordingMeshVertsDatas;
    private Vector2 _originPointXZ;
    private List<Vector3> _positions = new();
    private float _highestY;
    private float _lowestY;
    private float _farthestDistance;
    private bool _rotateMeshWhenFindingFarthestVert;
    private bool _needsUpdate = true;
    private bool _taskRunning = false;
    private bool _cancelTask = false;

    [SerializeField] float epsilon = 0.00001f;
    private float MedianY
    {
        get
        {
            if (_highestSelectable == null || _highestY < _lowestY)
                return 0f;
            return ((_highestY + _lowestY) / 2f) - _highestSelectable.transform.position.y;
        }
    }
    private object _lockObj = new();
   public Material renderMaterialColor;

    // Special offsets (in meters) derived from manufacturer guidance
    private const float TOP_HORIZONTAL_EXTRA = 0.150f;      // 150mm
    private const float BOTTOM_SPRING_EXTRA = 0.21216f;     // 212.16mm
    private const float BOTTOM_MOTORIZED_EXTRA = 0.260f; // 260mm

    // Cache last computed extra offset to trigger regen when parts change
    private float _lastSpecialOffset = 0f;
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

            _rotateMeshWhenFindingFarthestVert = _selectable != null
                && _selectable.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z);
        }
        CheckStatus();
    }

    private void Update()
    {
        //if (!UI_ToggleClearanceLines.IsActive) return;

        if (_needsUpdate && !_taskRunning)
        {
            UpdateLineRenderer();
        }

        if (_lineRenderer == null)
            return;

        if (FreeLookCam.IsActive)
        {
            float distanceToCamera = Vector3.Distance(gameObject.transform.position, Camera.main.transform.position);
            _lineRenderer.startWidth = _sizeScalar * distanceToCamera;
            _lineRenderer.endWidth = _sizeScalar * distanceToCamera;
        }
        else
        {
            float size = Mathf.Min(_sizeScalarOrthoMax, _sizeScalarOrtho * Camera.main.orthographicSize);
            _lineRenderer.startWidth = size;
            _lineRenderer.endWidth = size;
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
    /// Parent-chain ScaleUpdated misses sibling arms under a tandem mount.
    /// Subscribe every selectable under the shared root so length/scale changes
    /// on any arm rebuild the ring.
    /// </summary>
    private void RefreshAssemblyScaleSubscriptions()
    {
        ClearAssemblyScaleSubscriptions();
        if (_highestSelectable == null)
            return;

        foreach (var sel in _highestSelectable.GetComponentsInChildren<Selectable>(true))
        {
            if (sel == null)
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
            var prefab = Resources.Load<GameObject>("Prefabs/ClearanceLinesRenderer");
            if (prefab == null)
                return;
            Transform parent = Type == RendererType.ArmAssembly
                ? _highestSelectable.transform
                : transform.root;
            var newObj = Instantiate(prefab, parent);
            newObj.name = gameObject.name;
            newObj.transform.rotation = Quaternion.identity;
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

    public void RecordData(int rotationAmount, Vector3 forwardVector)
    {
        var meshes = _recordingMeshVertsDatas;
        if (meshes == null || meshes.Count == 0)
            return;

        float farthest = 0f;
        float localHighestY = float.MinValue;
        float localLowestY = float.MaxValue;

        Vector3 origin = new Vector3(_originPointXZ.x, 0f, _originPointXZ.y);
        Quaternion yaw = Quaternion.AngleAxis(rotationAmount, forwardVector);

        for (int j = 0; j < meshes.Count; j++)
        {
            MeshVertsData vertData = meshes[j];
            if (vertData?.Vertices == null)
                continue;

            for (int i = 0; i < vertData.Vertices.Length; i++)
            {
                Vector3 vert = vertData.Vertices[i];
                vert.x *= vertData.LossyScale.x;
                vert.y *= vertData.LossyScale.y;
                vert.z *= vertData.LossyScale.z;
                // World-space vertex at the current pose, then yaw around the mount XZ origin
                // so every arm under a tandem/multi mount contributes to one sweep radius.
                Vector3 worldVert = vertData.GlobalPosition + (vertData.Rotation * vert);
                Vector3 relativeXZ = new Vector3(worldVert.x - origin.x, 0f, worldVert.z - origin.z);
                Vector3 spunXZ = yaw * relativeXZ;
                Vector3 transformedPoint = new Vector3(
                    origin.x + spunXZ.x,
                    worldVert.y,
                    origin.z + spunXZ.z);

                float distance = Vector2.Distance(_originPointXZ, new Vector2(transformedPoint.x, transformedPoint.z));
                if (distance >= farthest)
                {
                    farthest = distance;

                    if (transformedPoint.y > localHighestY)
                        localHighestY = transformedPoint.y;

                    if (transformedPoint.y < localLowestY)
                        localLowestY = transformedPoint.y;
                }

                if (_cancelTask)
                    return;
            }
        }

        if (farthest > _farthestDistance)
        {
            lock (_lockObj)
            {
                if (farthest > _farthestDistance)
                {
                    _farthestDistance = farthest;
                    _highestY = localHighestY;
                    _lowestY = localLowestY;
                }
            }
        }
    }

    private void ResetMeshVertsData()
    {
        if (_meshVertsDatas == null)
        {
            _meshVertsDatas = new();
            MeshFilter[] meshFilters = CollectMeshFiltersForClearance();
            // Yawing a fixed pose around the mount does not change XZ radius — one pass
            // over world verts is enough. (Legacy 361-spin was for the old origin math.)
            int rotations = _rotateMeshWhenFindingFarthestVert && Type != RendererType.ArmAssembly
                ? 361
                : 1;
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

                _meshVertsDatas.Add(new MeshVertsData(filter, this)
                {
                    Rotation = filter.transform.rotation,
                    GlobalPosition = filter.transform.position,
                    Vertices = verts,
                    Rotations = rotations,
                    LossyScale = filter.transform.lossyScale,
                });
            }
        }
        else
        {
            foreach (var meshVertsData in _meshVertsDatas)
            {
                if (meshVertsData.MeshFilter == null)
                    continue;
                meshVertsData.GlobalPosition = meshVertsData.MeshFilter.transform.position;
                meshVertsData.Rotation = meshVertsData.MeshFilter.transform.rotation;
                meshVertsData.LossyScale = meshVertsData.MeshFilter.transform.lossyScale;
            }
        }
    }

    /// <summary>
    /// Arm assemblies: every active mesh under the shared mount root (all arms),
    /// so tandem / multi-arm clearance grows to the farthest reach.
    /// Other types: keep the authored IncludeChildren / self MeshFilter behavior.
    /// </summary>
    private MeshFilter[] CollectMeshFiltersForClearance()
    {
        if (Type == RendererType.ArmAssembly && _highestSelectable != null)
        {
            return _highestSelectable
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
    /// One ring per mount: only the lowest-instance-id ArmAssembly CLR under the
    /// root does the heavy sweep; siblings hide their duplicate line.
    /// </summary>
    private bool IsPrimaryArmAssemblyClearance()
    {
        if (Type != RendererType.ArmAssembly || _highestSelectable == null)
            return true;

        ClearanceLinesRenderer primary = null;
        int bestId = int.MaxValue;
        foreach (var clr in _highestSelectable.GetComponentsInChildren<ClearanceLinesRenderer>(true))
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

    private void ResetVariables()
    {
        _taskRunning = true;
        _highestY = float.MinValue;
        _lowestY = float.MaxValue;
        _farthestDistance = 0f;
        _positions = new List<Vector3>(_circlePositions);
        _farthestDistance = 0f;
        _originPointXZ = (_highestSelectable == null) ?
            new Vector2(transform.position.x, transform.position.z)
            : new Vector2(_highestSelectable.transform.position.x, _highestSelectable.transform.position.z);
    }

    private void SetNeedsUpdate()
    {
        _needsUpdate = true;
        // Never null the list a worker thread may still be reading — rebuild on the
        // next main-thread UpdateLineRendererArmAssembly instead.
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

        var root = _highestSelectable.transform;
        // Look through all children under this assembly root (active and inactive)
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


    private async void UpdateLineRendererArmAssembly()
    {
        if (!IsPrimaryArmAssemblyClearance())
        {
            if (_lineRenderer != null)
                _lineRenderer.gameObject.SetActive(false);
            _needsUpdate = false;
            _taskRunning = false;
            _cancelTask = false;
            return;
        }

        ResetVariables();
        _meshVertsDatas = null;
        ResetMeshVertsData();

        if (_meshVertsDatas == null || _meshVertsDatas.Count == 0)
        {
            _needsUpdate = false;
            _taskRunning = false;
            _cancelTask = false;
            return;
        }

        _recordingMeshVertsDatas = _meshVertsDatas;

        if (_lineRenderer != null && UI_ToggleClearanceLines.IsActive)
            _lineRenderer.gameObject.SetActive(true);

        int rotationCount = Mathf.Max(1, _meshVertsDatas[0].Rotations);

#if UNITY_WEBGL && !UNITY_EDITOR
        for (int i = 0; i < rotationCount; i++)
        {
            RecordData(i, Vector3.down);
            if (i % 6 == 0)
            {
                await Task.Yield();
            }         
        }
#else
        Task task = Task.Run(() =>
        {
            Parallel.For(0, rotationCount,
                parallelOptions: new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount >= 4 ? Mathf.Max(Environment.ProcessorCount / 2, 1) : Environment.ProcessorCount }, 
                body: j =>
                {
                    RecordData(j, Vector3.down);
                });
        });

        await task;
#endif

        if (_lineRenderer == null)
        {
            _needsUpdate = false;
            _taskRunning = false;
            _cancelTask = false;
            _recordingMeshVertsDatas = null;
            return;
        }

        // arm hierarchy scale was updated while task was running
        if (_cancelTask)
        {
            _taskRunning = false;
            _cancelTask = false;
            _recordingMeshVertsDatas = null;
            _meshVertsDatas = null;
            // _needsUpdate still true → Update/CheckStatus rebuilds
            return;
        }

        // Apply special manufacturer offsets when specific arm parts are present
        float specialOffset = GetArmAssemblyExtraOffset();
        _lastSpecialOffset = specialOffset;
        _farthestDistance += specialOffset;

        _farthestDistance += BufferSize;
        for (int i = 0; i < _positions.Count; i++)
        {
            Vector3 newPos = _positions[i] * _farthestDistance;
            newPos.y = MedianY;
            _positions[i] = newPos;
        }

        _lineRenderer.positionCount = _positions.Count;
        _lineRenderer.SetPositions(_positions.ToArray());

        _taskRunning = false;
        _cancelTask = false;
        _recordingMeshVertsDatas = null;
        _needsUpdate = false;
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



    private void UpdateLineRendererBedScanner()
    {
        _positions.Clear();

        // Rectangle size
        float length = 5f; // Total length (opposite to X)
        float width = 0.5f; // Width (Z-wise, centered)
        float yOffset = 0.2f;

        // Base position at object's center + upward offset
        Vector3 baseCenter = transform.position + Vector3.up * yOffset;

        // Directional vectors
        Vector3 left = -transform.right * length;               // Full length toward -X
        Vector3 forward = new Vector3(0, width * 0.5f,0);     // Half width on each side (Z)

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