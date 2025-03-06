using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Mathematics;
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
        Door
    }

    private static readonly float _sizeScalar = 0.0035f;
    private static readonly float _sizeScalarOrtho = 0.005f;
    private static readonly float _sizeScalarOrthoMax = 0.05f;

    private static List<Vector3> _circlePositions = new();

    private LineRenderer _lineRenderer;

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
    private RendererType Type { get; set; }

    private Selectable _highestSelectable;

    /// <summary> Can be null, be sure to check</summary>
    private Selectable _selectable;
    private List<Selectable> _trackedParentSelectables = new();
    private List<MeshVertsData> _meshVertsDatas;
    private Vector2 _originPointXZ;
    private List<Vector3> _positions = new();
    private float _highestY;
    private float _lowestY;
    private float _farthestDistance;
    private bool _rotateMeshWhenFindingFarthestVert;
    private bool _needsUpdate = true;
    private bool _taskRunning = false;
    private bool _cancelTask = false;
    private float MedianY => ((_highestY + _lowestY) / 2f) - _highestSelectable.transform.position.y;
    private object _lockObj = new();
    #endregion

    #region Monobehaviour
    private void Awake()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        _selectable = GetComponent<Selectable>();
    }

    private void OnDestroy()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        Unsubscribe();
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
            if (_trackedParentSelectables[0].TryGetArmAssemblyRoot(out GameObject armAssemblyRoot))
            {
                _highestSelectable = armAssemblyRoot.GetComponent<Selectable>();
            }
            else throw new Exception("Could not get clearance lines - no higher z-rotation in the arm assembly found");

            _rotateMeshWhenFindingFarthestVert = _selectable != null && _selectable.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z);
        }
       
        CheckStatus();
    }

    private void Update()
    {
        if (!UI_ToggleClearanceLines.IsActive) return;

        if (_needsUpdate && !_taskRunning)
        {
            UpdateLineRenderer();
        }

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
        UI_ToggleClearanceLines.ClearanceLinesToggled.RemoveListener(CheckStatus);
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
        if (_lineRenderer == null)
        {
            var prefab = Resources.Load<GameObject>("Prefabs/ClearanceLinesRenderer");
            var newObj = Instantiate(prefab, Type == RendererType.ArmAssembly ? _highestSelectable.transform : transform.root);
            newObj.transform.rotation = Quaternion.identity;
            _lineRenderer = newObj.GetComponent<LineRenderer>();
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
        float farthest = 0f;
        float localHighestY = float.MinValue;
        float localLowestY = float.MaxValue;

        for (int j = 0; j < _meshVertsDatas.Count; j++)
        {
            MeshVertsData vertData = _meshVertsDatas[j];
            for (int i = 0; i < vertData.Vertices.Length; i++)
            {
                Vector3 vert = vertData.Vertices[i];
                vert.x *= vertData.LossyScale.x;
                vert.y *= vertData.LossyScale.y;
                vert.z *= vertData.LossyScale.z;
                vert = vertData.Rotation * vert;

                if (j > 0)
                {
                    vert += vertData.GlobalPosition - _meshVertsDatas[0].GlobalPosition;
                }

                vert = Quaternion.AngleAxis(rotationAmount, forwardVector) * vert;
                Vector3 transformedPoint = vert + _meshVertsDatas[0].GlobalPosition;

                float distance = Vector2.Distance(_originPointXZ, new Vector2(transformedPoint.x, transformedPoint.z));
                if (distance >= farthest)
                {
                    farthest = distance;

                    if (transformedPoint.y > localHighestY)
                    {
                        localHighestY = transformedPoint.y;
                    }

                    if (transformedPoint.y < localLowestY)
                    {
                        localLowestY = transformedPoint.y;
                    }
                }

                if (_cancelTask)
                {
                    return;
                }
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
            MeshFilter[] meshFilters = IncludeChildrenInMeasurement ? GetComponentsInChildren<MeshFilter>() : new[] { GetComponent<MeshFilter>() };
            for (int j = 0; j < meshFilters.Length; j++)
            {
                var filter = meshFilters[j];
                _meshVertsDatas.Add(new MeshVertsData(filter, this)
                {
                    Rotation = filter.transform.rotation,
                    GlobalPosition = filter.transform.position,
                    Vertices = filter.sharedMesh.vertices,
                    Rotations = _rotateMeshWhenFindingFarthestVert ? 361 : 1,
                    LossyScale = filter.transform.lossyScale,
                });
            }
        }
        else
        {
            foreach (var meshVertsData in _meshVertsDatas)
            {
                meshVertsData.GlobalPosition = meshVertsData.MeshFilter.transform.position;
                meshVertsData.Rotation = meshVertsData.MeshFilter.transform.rotation;
                meshVertsData.LossyScale = meshVertsData.MeshFilter.transform.lossyScale;
            }
        }
    }

    private void ResetVariables()
    {
        _taskRunning = true;
        _highestY = float.MinValue;
        _lowestY = float.MaxValue;
        _farthestDistance = 0f;
        _positions = new List<Vector3>(_circlePositions);
        _farthestDistance = 0f;
        _originPointXZ = new Vector2(_highestSelectable.transform.position.x, _highestSelectable.transform.position.z);
    }

    private void SetNeedsUpdate()
    {
        _needsUpdate = true;
        if (_taskRunning)
        {
            _cancelTask = true;
        }
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
    }

    private async void UpdateLineRendererArmAssembly()
    {
        ResetVariables();
        ResetMeshVertsData();

#if UNITY_WEBGL && !UNITY_EDITOR
        for (int i = 0; i < _meshVertsDatas[0].Rotations; i++)
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
            Parallel.For(0, _meshVertsDatas[0].Rotations,
                parallelOptions: new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount >= 4 ? Mathf.Max(Environment.ProcessorCount / 2, 1) : Environment.ProcessorCount }, 
                body: j =>
                {
                    RecordData(j, Vector3.down);
                });
        });

        await task;
#endif

        // object was destroyed while task was running
        if (_lineRenderer == null)
        {
            _needsUpdate = false;
            _taskRunning = false;
            _cancelTask = false;
            return;
        }

        // arm hierarchy scale was updated while task was running
        if (_cancelTask)
        {
            _taskRunning = false;
            _cancelTask = false;
            return;
        }

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

        if (!_cancelTask)
        {
            _needsUpdate = false;
        }
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
    #endregion
}