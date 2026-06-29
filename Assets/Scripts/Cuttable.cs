using Parabox.CSG;
using SplenSoft.UnityUtilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Can be attached to wall objects to define that they can be
/// cut but <see cref="WallCutter"/> objects. Uses 
/// <see cref="Parabox.CSG"/> library for mesh subtraction
/// </summary>
public class Cuttable : MonoBehaviour
{
#if UNITY_EDITOR
    [CustomEditor(typeof(Cuttable))]
    private class Cuttable_Inspector : Editor
    {
        private Cuttable _instance;

        public override void OnInspectorGUI()
        {
            if (_instance == null)
            {
                _instance = target as Cuttable;
            }

            DrawDefaultInspector();

            if (Application.isPlaying)
            {
                if (GUILayout.Button("Update Cuts"))
                {
                    _instance.Cut();
                }
            }
        }
    }
#endif

    public UnityEvent OnCutComplete { get; } = new();
    private static List<Cuttable> ActiveCuttables { get; } = new();
    private static readonly List<WallCutter> CachedWallCutters = new();
    private static bool _wallCuttersCacheDirty = true;
    private static readonly ThrottledInvoker DragCutThrottle = new ThrottledInvoker(0.125f);

    private MeshFilter _filter;
    private MeshRenderer _meshRenderer;
    private GizmoHandler _gizmoHandler;
    private Selectable _selectable;
    private Collider _collider;
    private MatchScale _matchScale;

    [field: SerializeField]
    private MeshInstanceManager MeshInstanceManager { get; set; }

    private UnityEventManager _eventManager = new();

    private void Awake()
    {
        ActiveCuttables.Add(this);

        _collider = GetComponentInChildren<Collider>();
        _filter = GetComponentInChildren<MeshFilter>();
        _meshRenderer = GetComponentInChildren<MeshRenderer>();
        GetMatchScale();
        MeshInstanceManager.RegisterMesh(_filter.sharedMesh);

        if (!TryGetComponent(out _selectable))
            _selectable = GetComponentInParent<Selectable>();

        if (!TryGetComponent(out _gizmoHandler))
            _gizmoHandler = GetComponentInParent<GizmoHandler>();

        if (_matchScale != null)
        {
            _eventManager.RegisterEvents
                ((_matchScale.OnScaleUpdated, Cut));
        }

        if (_gizmoHandler != null)
        {
            _eventManager.RegisterEvents
                ((_gizmoHandler.GizmoDragEnded, CutImmediate),
                (_gizmoHandler.GizmoDragPostUpdate, CutThrottled));
        }

        Selectable.ActiveSelectablesInSceneChanged.AddListener(MarkWallCuttersCacheDirty);

        if (_selectable != null)
        {
            _eventManager.RegisterEvents
                ((_selectable.OnPlaced, Cut));
        }

        _eventManager.AddListeners();
    }

    private void GetMatchScale()
    {
        if (!TryGetComponent(out _matchScale))
            _matchScale = GetComponentInParent<MatchScale>();
    }

    private void OnDestroy()
    {
        ActiveCuttables.Remove(this);
        MarkWallCuttersCacheDirty();

        _eventManager.RemoveListeners();
        Selectable.ActiveSelectablesInSceneChanged.RemoveListener(MarkWallCuttersCacheDirty);
    }

    private static void MarkWallCuttersCacheDirty() => _wallCuttersCacheDirty = true;

    private static void RebuildWallCuttersCacheIfNeeded()
    {
        if (!_wallCuttersCacheDirty) return;

        CachedWallCutters.Clear();
        var seen = new HashSet<WallCutter>();
        foreach (var selectable in Selectable.ActiveSelectables)
        {
            int before = CachedWallCutters.Count;
            selectable.GetComponentsInChildren(true, CachedWallCutters);
            for (int i = before; i < CachedWallCutters.Count; i++)
            {
                if (!seen.Add(CachedWallCutters[i]))
                {
                    CachedWallCutters.RemoveAt(i);
                    i--;
                }
            }
        }

        _wallCuttersCacheDirty = false;
    }

    private void CutThrottled() => DragCutThrottle.Invoke(Cut);

    private void CutImmediate() => DragCutThrottle.InvokeImmediate(Cut);

    private void Start()
    {
        Cut();
    }

    public static void UpdateCuts()
    {
        ActiveCuttables.ForEach(x => x.Cut());
    }

    private void UpdateMaterials()
    {
        var materials = _meshRenderer.sharedMaterials.ToList();

        Material firstMaterial = materials[0];

        while (materials.Count < _filter.sharedMesh.subMeshCount)
        {
            materials.Add(firstMaterial);
        }

        _meshRenderer.sharedMaterials = materials.ToArray();
    }

    public void Cut()
    {
        // Set back to original mesh
        _filter.sharedMesh = MeshInstanceManager.OriginalMesh;
        ((MeshCollider)_collider).sharedMesh = _filter.sharedMesh;
        MeshInstanceManager.ResetMesh();

        MeshCollider meshCollider = null;
        if (_collider is MeshCollider mc && !mc.convex)
        {
            meshCollider = (MeshCollider)_collider;
            meshCollider.convex = true;
        }

        RebuildWallCuttersCacheIfNeeded();

        foreach (var wallCutter in CachedWallCutters)
        {
            // Not sure if this saves any performance, but its
            // probably the most cost-effective way to determine
            // if the wallCutter and target mesh intersect.
            // If we don't do this, we're going to generate and
            // dump more meshes than we need to, so for now I
            // think it's worth it
            if (wallCutter.Collider == null || 
            wallCutter.Selectable == null || 
            wallCutter.Selectable.IsDestroyed) 
                continue;

            bool collides = Physics.ComputePenetration(
                _collider,
                _collider.gameObject.transform.position,
                _collider.gameObject.transform.rotation,
                wallCutter.Collider,
                wallCutter.Collider.gameObject.transform.position,
                wallCutter.Collider.gameObject.transform.rotation,
                out _,
                out _);

            if (!collides) continue;

            var result = CSG.Subtract(_filter.gameObject, wallCutter.CutArea);

            var mesh = ((Mesh)result);
            Vector3[] verts = mesh.vertices;

            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = _filter.transform.InverseTransformPoint(verts[i]);
            }

            mesh.SetVertices(verts);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            MeshInstanceManager.UpdateMesh(mesh);

            _filter.sharedMesh = mesh;
        }

        ((MeshCollider)_collider).sharedMesh = _filter.sharedMesh;
        UpdateMaterials();
        if (meshCollider != null)
        {
            meshCollider.convex = false;
        }

        OnCutComplete?.Invoke();
    }
}