using HighlightPlus;
using SplenSoft.AssetBundles;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;
using Unity.VisualScripting;
using UnityEngine.UI;
using SplenSoft.UnityUtilities;
using System.IO;


#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Add basic selectable - <see href="https://youtu.be/qEaRrGC_MX8?si=kCXNSVa11KxLKRNG"/> 
/// </summary>
[RequireComponent(typeof(GizmoHandler), typeof(HighlightEffect)), Serializable]
public partial class Selectable : MonoBehaviour, IPreprocessAssetBundle
{
    [Serializable]
    public struct Metadata
    {
        // AFTER (fixed):
        [field: SerializeField] public string key { get; set; }
        [field: SerializeField] public string value { get; set; }



        public Metadata(string k = "", string v = "")
        {
            key = k;
            value = v;
        }
    }

    #region Fields and Properties
    [field: SerializeField] private bool _initialOffsetApplied = false;
    public static List<Selectable> ActiveSelectables { get; } = new List<Selectable>();

    public static Action SelectionChanged;
    public static List<Selectable> SelectedSelectables { get; private set; } = new();
    public static bool IsInElevationPhotoMode { get; private set; }
    public static UnityEvent ActiveSelectablesInSceneChanged { get; } = new();

    public static void NotifyActiveSelectablesInSceneChanged()
        => ActiveSelectablesInSceneChanged?.Invoke();

    public Dictionary<GizmoType, Dictionary<Axis, GizmoSetting>>
        GizmoSettings
    { get; } = new();

    public EventHandler MouseOverStateChanged;
    public UnityEvent SelectableDestroyed { get; } = new();
    public UnityEvent ScaleUpdated { get; } = new();
    public UnityEvent Deselected { get; } = new();
    public UnityEvent OnPlaced { get; } = new();
    public UnityEvent OnRaycastPositionUpdated { get; } = new();
    public Selectable ParentSelectable { get; private set; }

    public Vector3 OriginalLocalPosition { get; set; }

    public string guid { get; set; }

    public bool IsMouseOver { get; private set; }
    public bool IsDestroyed { get; private set; }

    [field: SerializeField]
    public string GUID { get; private set; }

    public AttachmentPoint ParentAttachmentPoint { get; set; }

    [field: SerializeField, MetaDataHandler]
    public SelectableMetaData MetaData { get; set; }

    [field: SerializeField]
    public List<AttachmentPointData>
        AttachmentPointDatas
    { get; set; } = new();

    [field: SerializeField,
    FormerlySerializedAs("<Types>k__BackingField")]
    public List<SpecialSelectableType>
    SpecialTypes
    { get; set; } = new();

    [field: SerializeField]
    public List<RoomBoundaryType> WallRestrictions { get; set; } = new();

    [field: SerializeField]
    public List<GizmoSetting> GizmoSettingsList { get; set; } = new();

    [field: SerializeField]
    private Vector3 InitialLocalPositionOffset { get; set; }

    [field: SerializeField]
    public bool IsDestructible { get; private set; } = true;

    [field: SerializeField]
    public bool AllowInverseControl { get; private set; } = false;

    [field: SerializeField]
    public List<ScaleLevel> ScaleLevels { get; set; } = new();

    [field: SerializeField, FormerlySerializedAs("<useLossyScale>k__BackingField")]
    public bool UseLossyScale { get; private set; }

    [field: SerializeField,
    Tooltip("True if this object will rotate " +
    "along its y-axis automatically to make its " +
    "z-axis (forward) parallel to the world " +
    "y-axis (up-down)")]
    private bool ZAlwaysFacesGround { get; set; }

    [field: SerializeField,
    Tooltip("True if this object will rotate " +
    "along its y-axis automatically to make its " +
    "z-axis (forward) parallel to the world " +
    "y-axis (up-down), but only when taking " +
    "elevation photos for PDF output")]
    private bool ZAlwaysFacesGroundElevationOnly { get; set; }

    [field: SerializeField]
    private bool ZAlignUpIsParentForward { get; set; }

    [field: SerializeField]
    public List<Measurable> Measurables { get; private set; }
    public List<Measurer> Measurers = new List<Measurer>();
    public List<Measurable.Measurement> measurements;

    [field: SerializeField,
    Tooltip("True if this object will rotate to its " +
    "default rotation when taking an elevation " +
    "photo for the PDF")]
    private bool AlignForElevationPhoto { get; set; }

    [field: SerializeField,
    Tooltip("True if this object will rotate along " +
    "its Y-axis (vertical rotation) to its lowest " +
    "and highest possible positions for an " +
    "elevation photo.")]
    private bool ChangeHeightForElevationPhoto { get; set; }

    [field: SerializeField,
    Tooltip("Meant for decals. This means you can " +
    "place it on any collider. Yes, including other " +
    "decals. Could get messy.")]
    private bool CanPlaceAnywhere { get; set; }

    /// <summary>
    /// A forced parent with no attachment point. Only used
    /// by "decal" type attachments
    /// </summary>
    [field: SerializeField]
    public Selectable AttachedTo { get; set; }

    /// <summary>
    /// Selectables that are part of the same prefab as this one. 
    /// Used to highlight multiple gizmos instead of just one
    /// </summary>
    [field: SerializeField, ReadOnly]
    public List<Selectable> RelatedSelectables { get; set; }

    public List<Selectable> _assemblySelectables = new();
    private Dictionary<Selectable, Quaternion> _originalRotations = new();
    private Dictionary<Measurable, bool> _measurableActiveStates = new();
    private List<Vector3> _childScales = new();
    private Quaternion _originalRotation;
    private Quaternion _originalLocalRotation;
    private Transform _virtualParent;
    private HighlightEffect _highlightEffect;
    private HighlightProfile _highlightProfileSelected;
    private GizmoHandler _gizmoHandler;
    //private Quaternion _originalRotation2;
    private Camera _cameraRenderTextureElevation;
    public static Camera ActiveCameraRenderTextureElevation { get; private set; }
    private Collider[] RaycastingColliders { get; set; }

    /// <summary>
    /// If true, this is probably a ceiling mount
    /// </summary>
    private bool IsAssemblyRoot => SpecialTypes.Contains(SpecialSelectableType.Mount);

    public string UIButtonName;
    public bool IsArmAssembly => transform.root.TryGetComponent(out Selectable rootSelectable) &&
        rootSelectable.IsAssemblyRoot;

    public bool canBeDuplicated;

    public bool IsSelected => SelectedSelectables.Contains(this);

    [field: SerializeField, ReadOnly] public ScaleLevel CurrentScaleLevel { get; private set; }
    [field: SerializeField, ReadOnly] public ScaleLevel CurrentPreviewScaleLevel { get; private set; }
    public UnityEvent<ScaleLevel> OnScaleChange { get; } = new();

    private bool _isRaycastPlacementMode;
    private bool _hasBeenPlaced;
    public bool Started { get; private set; }
    private bool _isRaycastingOnSelectable;

    public bool ScaleLevelsRestoredFromSave { get; set; } = false;
    private bool _deferInitUntilLoadComplete;
    private static HighlightProfile _cachedHighlightProfileSelected;
    #endregion

    #region Monobehaviour
    private void Awake()
    {
        ActiveSelectables.Add(this);
        CacheParentReferences();

        if (ConfigurationManager.IsLoading)
        {
            _deferInitUntilLoadComplete = true;
            return;
        }

        InitializeComponents();
    }

    private void CacheParentReferences()
    {
        Transform parent = transform.parent;

        while (parent != null)
        {
            if (parent.TryGetComponent<AttachmentPoint>(out var attachmentPoint))
            {
                if (attachmentPoint != ParentAttachmentPoint)
                    ParentAttachmentPoint = attachmentPoint;
                break;
            }

            if (parent.TryGetComponent<Selectable>(out var selectable))
            {
                ParentSelectable = selectable;
                break;
            }

            parent = parent.parent;
        }
    }

    private void InitializeComponents()
    {
        if (AllowInverseControl && GetComponent<CCDIK>() == null)
            gameObject.AddComponent<CCDIK>();

        _cameraRenderTextureElevation = GetComponentInChildren<Camera>();
        if (_cameraRenderTextureElevation != null)
            _cameraRenderTextureElevation.enabled = false;

        _originalRotation = transform.rotation;
        _originalLocalRotation = transform.localRotation;

        _highlightEffect = GetComponent<HighlightEffect>();
        _highlightProfileSelected = _cachedHighlightProfileSelected ??=
            Resources.Load<HighlightProfile>("HighlightProfile_SelectableSelected");

        if (_highlightEffect != null &&
            _highlightProfileSelected != null &&
            _highlightEffect.profile != _highlightProfileSelected)
        {
            _highlightEffect.ProfileLoad(_highlightProfileSelected);
        }

        _gizmoHandler = GetComponent<GizmoHandler>();
        InputHandler.KeyStateChanged += InputHandler_KeyStateChanged;

        GizmoSettingsList.ForEach(item =>
        {
            if (item.OnlyIfRoot && ParentAttachmentPoint != null)
                return;

            if (!GizmoSettings.ContainsKey(item.GizmoType))
                GizmoSettings[item.GizmoType] = new();

            GizmoSettings[item.GizmoType][item.Axis] = item;
        });

        if (!ConfigurationManager.IsLoading)
            NotifyActiveSelectablesInSceneChanged();
    }

    /// <summary>
    /// Completes Awake/Start work deferred during bulk room load.
    /// </summary>
    public void CompleteDeferredLoadInitialization()
    {
        if (!_deferInitUntilLoadComplete)
            return;

        _deferInitUntilLoadComplete = false;
        // Awake ran before pass-2 parenting, so re-resolve attachment/selectable parents now.
        CacheParentReferences();
        if (ParentAttachmentPoint != null)
            ParentAttachmentPoint.SetAttachedSelectable(this);
        InitializeComponents();
        InitializeAfterStart();
        _gizmoHandler?.RefreshGizmoCapabilities();
    }

    private void OnDestroy()
    {
        if (IsDestroyed) return;

        IsDestroyed = true;

        ActiveSelectables.Remove(this);

        if (SelectedSelectables.Contains(this))
        {
            Deselect();
        }

        InputHandler.KeyStateChanged -= InputHandler_KeyStateChanged;
        if (ParentAttachmentPoint != null)
        {
            ParentAttachmentPoint.DetachSelectable(this);
        }

        if (ParentSelectable != null)
        {
            Destroy(ParentSelectable.gameObject);
        }

        SelectableDestroyed?.Invoke();
        ActiveSelectablesInSceneChanged?.Invoke();
    }

    public void OnMouseUpAsButton()
    {
        bool overUi = InputHandler.IsPointerOverUIElement();
        SelectionDiagnostics.LogMouseUp(this, overUi);

        if (overUi)
            return;

        Select();
    }

    private void OnMouseEnter()
    {
        //  Debug.Log($"OnMouse Enter detected over {gameObject.name}");

        if (GizmoHandler.GizmoBeingUsed) return;
        IsMouseOver = true;
        MouseOverStateChanged?.Invoke(this, null);
    }

    private void OnMouseExit()
    {
        if (GizmoHandler.GizmoBeingUsed) return;
        IsMouseOver = false;
        MouseOverStateChanged?.Invoke(this, null);
    }

    private async void GenerateGuidName()
    {
        while (ConfigurationManager.IsLoading)
        {
            await Task.Yield();
            if (!Application.isPlaying)
                throw new AppQuitInTaskException();
        }
        guid = Guid.NewGuid().ToString();
        gameObject.name = guid.ToString();
    }
    public bool isDuplicated;
    private void Start()
    {
        if (Started || ConfigurationManager.IsLoading)
            return;

        InitializeAfterStart();
    }

    private void InitializeAfterStart()
    {
        if (Started)
            return;

        EnsureMeasurablesLinked();

        if (!ConfigurationManager.IsLoading &&
            GUID != "" &&
            !ConfigurationManager.IsRoomBoundary(GUID) &&
            !ConfigurationManager.IsBaseboard(GUID) &&
            !ConfigurationManager.IsWallProtector(GUID) &&
            transform.parent == null)
        {
            // Match clearance by prefab/catalog name BEFORE renaming the GO to a GUID.
            ImagingClearanceBootstrap.EnsureFor(this);
            GenerateGuidName();
        }

        // If scale levels were restored from save, skip recalculation logic.
        // Also skip when TrackedObject has stored transform data: root selectables
        // (e.g. ArmSegment_1(Clone)) often save with empty scaleLevels while the
        // actual mesh scale lives on child transforms — rerunning prefab SetScaleLevel
        // here overwrites RestoreTransform and causes the post-load arm stretch bug.
        bool loadedTransformsFromSave = TryGetComponent(out TrackedObject trackedForLoad)
            && trackedForLoad.HasStoredValues;
        if (ScaleLevelsRestoredFromSave || loadedTransformsFromSave)
        {
            //_originalRotation2 = transform.localRotation;
            OriginalLocalPosition = transform.localPosition;
            Started = true;
            // Measurement tags follow UI_MeasurementButton — do not force on at init.
            ToggleMeasurableActiveStates(false);
            Measurers.AddRange(Measurables
                    .SelectMany(m => m.Measurements)
                    .Where(measurement => measurement.Measurer != null)
                    .Select(measurement => measurement.Measurer)
            );

            // Always set up gizmo event listeners for scale changes, even for restored objects
            if (IsGizmoSettingAllowed(GizmoType.Scale, Axis.Z))
            {
                _gizmoHandler.GizmoDragEnded.AddListener(() =>
                {
                    if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
                    {
                        UpdateZScaling(true);
                    }
                });

                _gizmoHandler.GizmoDragPostUpdate.AddListener(() =>
                {
                    if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
                    {
                        UpdateZScaling(false);
                    }
                });
            }
            ImagingClearanceBootstrap.EnsureFor(this);
            return;
        }

        if (ScaleLevels.Count > 0)
        {
            // Duplicate Instantiate already copied the live hierarchy (tube Z + attach-chain
            // inverses). Do not re-bake ScaleZ / SetScaleLevel from ModelDefault on duplicates.
            if (isDuplicated)
            {
                ScaleAuditLog.Event("Sel.Init.dupSkipSetScale",
                    $"name={name} local={transform.localScale} " +
                    $"selectedScaleZ={(ScaleLevels.FirstOrDefault(s => s.Selected)?.ScaleZ.ToString("G6") ?? "none")}");

                CurrentScaleLevel = ScaleLevels.FirstOrDefault(item => item.Selected)
                    ?? ScaleLevels.First(item => item.ModelDefault);
                CurrentPreviewScaleLevel = CurrentScaleLevel;
                StoreChildScales();

                OriginalLocalPosition = transform.localPosition;
                Started = true;
                ToggleMeasurableActiveStates(false);
                Measurers.AddRange(Measurables
                        .SelectMany(m => m.Measurements)
                        .Where(measurement => measurement.Measurer != null)
                        .Select(measurement => measurement.Measurer)
                );

                if (IsGizmoSettingAllowed(GizmoType.Scale, Axis.Z))
                {
                    _gizmoHandler.GizmoDragEnded.AddListener(() =>
                    {
                        if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
                            UpdateZScaling(true);
                    });

                    _gizmoHandler.GizmoDragPostUpdate.AddListener(() =>
                    {
                        if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
                            UpdateZScaling(false);
                    });
                }
                ImagingClearanceBootstrap.EnsureFor(this);
                return;
            }

            CurrentScaleLevel = ScaleLevels.First(item => item.ModelDefault);
            CurrentPreviewScaleLevel = CurrentScaleLevel;

            StoreChildScales();

            // Catalog Size is meters of real length. Bake ScaleZ so visual length at each
            // level equals Size — do not force ModelDefault to ScaleZ=1 when the authored
            // mesh/tip at identity is a different length (that made "300 mm" longer than
            // a calibrated "500 mm").
            BakeScaleZFromAuthoredLength();

            var defaultSelected = ScaleLevels.First(item => item.Selected);
            SetScaleLevel(defaultSelected, true);

            if (IsGizmoSettingAllowed(GizmoType.Scale, Axis.Z))
            {
                _gizmoHandler.GizmoDragEnded.AddListener(() =>
                {
                    if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
                    {
                        UpdateZScaling(true);
                    }
                });

                _gizmoHandler.GizmoDragPostUpdate.AddListener(() =>
                {
                    if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
                    {
                        UpdateZScaling(false);
                    }
                });
            }
        }

        //_originalRotation2 = transform.localRotation;
        OriginalLocalPosition = transform.localPosition;

        //OriginalLocalRotation = transform.localEulerAngles;

        if (!_initialOffsetApplied && !isDuplicated)
        {
            Vector3 adjustedOffsetVector = new Vector3(
                InitialLocalPositionOffset.x * transform.localScale.x,
                InitialLocalPositionOffset.y * transform.localScale.y,
                InitialLocalPositionOffset.z * transform.localScale.z
            );
            transform.localPosition += adjustedOffsetVector;
            _initialOffsetApplied = true;    // survives into duplicates
        }
            Started = true;
        ToggleMeasurableActiveStates(false);
        //Storing Reference for the Measurers
        Measurers.AddRange(Measurables
                .SelectMany(m => m.Measurements)
                .Where(measurement => measurement.Measurer != null)
                .Select(measurement => measurement.Measurer)
        );

        ImagingClearanceBootstrap.EnsureFor(this);
    }

    private void Update()
    {
        UpdateRaycastPlacementMode();
        FaceZTowardGround();
    }
    #endregion

    #region Static

    public static float RoundToNearestHalfInch(float value)
    {
        float halfInch = 0.0127f;
        float modulo = value % halfInch;
        if (modulo <= halfInch / 2)
        {
            value -= modulo;
        }
        else
        {
            value += halfInch - modulo;
        }

        return value;
    }

    public static void DeselectAll()
    {
        if (SelectedSelectables.Count > 0)
        {
            foreach (var selectable in SelectedSelectables)
            {
                if (selectable == null)
                    continue;
                var gizmo = selectable.GetComponent<GizmoHandler>();
                if (gizmo != null && gizmo.GizmoUsedLastFrame)
                    return;
            }

            SelectedSelectables[0].Deselect();
        }
    }

    /// <summary>
    /// Clears selection highlights/gizmos for photo/PDF capture.
    /// Unlike <see cref="DeselectAll"/>, does not bail when a gizmo was used last frame.
    /// Also force-clears HighlightPlus on every active selectable — service-head outlet
    /// panels can stay highlighted after Deselect and paint green boxes into the RT.
    /// </summary>
    public static void ClearSelectionForCapture()
    {
        while (SelectedSelectables.Count > 0)
        {
            var sel = SelectedSelectables[0];
            if (sel == null)
            {
                SelectedSelectables.RemoveAt(0);
                continue;
            }
            sel.Deselect(fireEvent: false);
            break;
        }
        SelectedSelectables.Clear();

        foreach (var s in ActiveSelectables)
        {
            if (s == null || s._highlightEffect == null)
                continue;
            s._highlightEffect.highlighted = false;
        }
    }

    /// <summary>
    /// Force HighlightPlus off and hide AttachPoint placeholder meshes for elevation capture.
    /// AP Sphere renderers otherwise stay visible while the mouse is over a parent selectable.
    /// </summary>
    public static void SuppressHighlightsForCapture(IList<Selectable> assembly)
    {
        ClearSelectionForCapture();
        if (assembly == null)
            return;
        foreach (var s in assembly)
        {
            if (s == null)
                continue;
            foreach (var h in s.GetComponentsInChildren<HighlightEffect>(true))
            {
                if (h != null)
                    h.highlighted = false;
            }
            foreach (var ap in s.GetComponentsInChildren<AttachmentPoint>(true))
            {
                if (ap != null)
                    ap.RefreshStatusForLoad();
            }
        }
    }

    /// <summary>
    /// Destroys all <see cref="IsDestructible"/> objects 
    /// in the scene
    /// </summary>
    public static void DestroyAll()
    {
        DeselectAll();

        ActiveSelectables
            .Where(x => x.IsDestructible)
            .ToList()
            .ForEach(x =>
            {
                if (x != null && !x.IsDestroyed)
                    Destroy(x.gameObject);
            });
    }

    #endregion

    public Bounds GetBounds()
    {
        MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>();

        if (meshRenderers.Length == 0)
        {
            throw new Exception($"Selectable {gameObject.name} had 0 mesh renderers.");
        }

        Bounds bounds = new Bounds(
            meshRenderers[0].bounds.center,
            meshRenderers[0].bounds.size);

        for (int i = 1; i < meshRenderers.Length; i++)
        {
            bounds.Encapsulate(meshRenderers[i].bounds);
        }

        return bounds;
    }

    public bool TryGetArmAssemblyRoot(out GameObject rootObj)
    {
        rootObj = null;
        if (transform.root.TryGetComponent<Selectable>(out var rootSelectable))
        {
            rootObj = rootSelectable.gameObject;
            return rootSelectable.IsAssemblyRoot;
        }
        return false;
    }

    private void FaceZTowardGround()
    {
        if (ZAlwaysFacesGround || (ZAlwaysFacesGroundElevationOnly && IsInElevationPhotoMode))
        {
            float oldX = transform.localEulerAngles.x;

            transform.LookAt(
                transform.position + Vector3.down,
                ZAlignUpIsParentForward ? transform.parent.forward : transform.parent.right);


            transform.localEulerAngles = new Vector3(oldX, transform.localEulerAngles.y, 0);
        }
    }

    /// <summary>
    /// Gets most recent metadata from database (via Object 
    /// menu) or seed data from selectable prefab
    /// </summary>
    /// <returns></returns>
    public SelectableMetaData GetMetadata()
    {
        var data = RelatedSelectables[0].MetaData;

        var matchingItem = ObjectMenu.Instance.ObjectMenuItems
                .FirstOrDefault(x => x.SelectableData != null &&
                    RelatedSelectables[0].GUID == x.SelectableData.AssetBundleName);

        if (matchingItem != null)
        {
            data = matchingItem.SelectableMetaData;
        }

        return data;
    }

    private void Deselect(bool fireEvent = true)
    {
        //Debug.Log($"Attempting to deselect {gameObject.name}");
        if (!IsSelected)
        {
            //Debug.Log($"Could not deselect {gameObject.name} because it was not selected");
            return;
        }

        List<Selectable> previous = new List<Selectable>(SelectedSelectables);
        SelectedSelectables.Clear();






        previous.ForEach(x =>
        {
            //Debug.Log($"Firing deselect event for {x.gameObject.name}");
            x._highlightEffect.highlighted = false;
            x.Deselected?.Invoke();
        });

        if (fireEvent)
        {
            SelectionChanged?.Invoke();
        }

    }

    public void Select()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
            return;

        // Clicking the already-selected object deselects it.
        if (IsSelected)
        {
            Deselect();
            return;
        }

        if (_isRaycastPlacementMode)
        {
            SelectionDiagnostics.LogSelectBlocked(this, "raycast_placement_mode");
            return;
        }

        if (GizmoHandler.GizmoBeingUsed)
        {
            SelectionDiagnostics.LogSelectBlocked(this, "gizmo_in_use");
            return;
        }

        if (SelectedSelectables.Count > 0)
        {
            SelectedSelectables[0].Deselect(false);
        }

        SelectedSelectables = new List<Selectable>(RelatedSelectables);

        if (!SelectedSelectables.Contains(this))
        {
            SelectedSelectables.Add(this);
        }

        SelectedSelectables.ForEach(x =>
        {
            x._highlightEffect.highlighted = true;
            x._gizmoHandler.SelectableSelected();
        });

        SelectionDiagnostics.LogSelectOk(this);
        SelectionChanged?.Invoke();
    }

    #region Gizmos

    private bool CheckConstraints(float currentVal, float originalVal,
        float maxVal, float minVal, out float excess)
    {
        float diff = currentVal - originalVal;

        excess = diff > maxVal ? diff - maxVal :
            diff < minVal ? diff - minVal : 0f;

        return excess != 0;
    }

    public bool ExeedsMaxTranslation(out Vector3 totalExcess)
    {
        Vector3 adjustedTransform = transform.localRotation * transform.localPosition;

        float ignoreScale = GetGizmoSettingTranslateIgnoreBool() ? 1 : transform.localScale.z;

        float maxTranslationX = GetGizmoSettingMaxValue(GizmoType.Move, Axis.X) * transform.localScale.x;
        float maxTranslationY = GetGizmoSettingMaxValue(GizmoType.Move, Axis.Y) * transform.localScale.y;
        float maxTranslationZ = GetGizmoSettingMaxValue(GizmoType.Move, Axis.Z) * ignoreScale;

        float minTranslationX = GetGizmoSettingMinValue(GizmoType.Move, Axis.X) * transform.localScale.x;
        float minTranslationY = GetGizmoSettingMinValue(GizmoType.Move, Axis.Y) * transform.localScale.y;
        float minTranslationZ = GetGizmoSettingMinValue(GizmoType.Move, Axis.Z) * ignoreScale;

        Vector3 adjustedMaxTranslation = new Vector3(maxTranslationX, maxTranslationY, maxTranslationZ);
        Vector3 adjustedMinTranslation = new Vector3(minTranslationX, minTranslationY, minTranslationZ);
        totalExcess = default;
        bool exceedsX = IsGizmoSettingAllowed(GizmoType.Move, Axis.X)
                        && CheckConstraints(adjustedTransform.x,
                                            OriginalLocalPosition.x,
                                            adjustedMaxTranslation.x,
                                            adjustedMinTranslation.x,
                        out totalExcess.x);
        bool exceedsY = IsGizmoSettingAllowed(GizmoType.Move, Axis.Y)
                        && CheckConstraints(adjustedTransform.y,
                                            OriginalLocalPosition.y,
                                            adjustedMaxTranslation.y,
                                            adjustedMinTranslation.y,
                        out totalExcess.y);
        bool exceedsZ = IsGizmoSettingAllowed(GizmoType.Move, Axis.Z)
                        && CheckConstraints(adjustedTransform.z,
                                            OriginalLocalPosition.z,
                                            adjustedMaxTranslation.z,
                                            adjustedMinTranslation.z,
                        out totalExcess.z);
        return exceedsX || exceedsY || exceedsZ;
    }

    /// <returns>True if any rotation happened</returns>
    public bool TryRotateTowardVector(Vector3 directionVector)
    {
        if (IsGizmoSettingAllowed(GizmoType.Rotate, Axis.X) ||
        IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Y) ||
        IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z))
        {
            Quaternion oldRotation = transform.localRotation;
            transform.LookAt(transform.position + directionVector);
            if (ExceedsMaxRotation(out Vector3 totalExcess))
            {
                transform.localRotation *= Quaternion.Euler(-totalExcess.x, -totalExcess.y, -totalExcess.z);
            }

            if (oldRotation != transform.localRotation) return true;
        }

        return false;
    }

    public bool ExceedsMaxRotation(out Vector3 totalExcess)
    {
        float angleX = transform.localEulerAngles.x > 180 ? transform.localEulerAngles.x - 360f : transform.localEulerAngles.x;
        float angleY = transform.localEulerAngles.y > 180 ? transform.localEulerAngles.y - 360f : transform.localEulerAngles.y;
        float angleZ = transform.localEulerAngles.z > 180 ? transform.localEulerAngles.z - 360f : transform.localEulerAngles.z;
        totalExcess = default;
        bool exceedsX = IsGizmoSettingAllowed(GizmoType.Rotate, Axis.X) && CheckConstraints(angleX, 0, GetGizmoSettingMaxValue(GizmoType.Rotate, Axis.X), GetGizmoSettingMinValue(GizmoType.Rotate, Axis.X), out totalExcess.x);
        bool exceedsY = TryGetGizmoSetting(GizmoType.Rotate, Axis.Y, out _) && CheckConstraints(angleY, 0, GetGizmoSettingMaxValue(GizmoType.Rotate, Axis.Y), GetGizmoSettingMinValue(GizmoType.Rotate, Axis.Y), out totalExcess.y);
        bool exceedsZ = TryGetGizmoSetting(GizmoType.Rotate, Axis.Z, out _) && CheckConstraints(angleZ, 0, GetGizmoSettingMaxValue(GizmoType.Rotate, Axis.Z), GetGizmoSettingMinValue(GizmoType.Rotate, Axis.Z), out totalExcess.z);
        return exceedsX || exceedsY || exceedsZ;
    }

    /// <summary>
    /// True when live tube/mesh length must not be rewritten (load, duplicate, or saved transforms).
    /// Callers may sync ScaleLevel metadata only — never SetScaleLevel from a filter/prefab baseline.
    /// </summary>
    public bool ShouldPreserveLiveLengthScale =>
        isDuplicated
        || ScaleLevelsRestoredFromSave
        || ConfigurationManager.IsLoading
        || (TryGetComponent(out TrackedObject tracked) && tracked.HasStoredValues);

    /// <summary>
    /// Sync selected ScaleLevel metadata without touching transforms or rewriting ScaleZ.
    /// Do not align ScaleZ to live localScale here — during load this runs before
    /// RestoreTransform, when the tube is still at prefab Z=1 and would wipe saved ScaleZ.
    /// Callers that already trust live Z (e.g. GetAttachedObjects preserve) must set ScaleZ first.
    /// </summary>
    public void RestoreScaleLevelFromSave(ScaleLevel scaleLevel)
    {
        if (scaleLevel == null)
            return;

        if (ScaleLevels != null)
        {
            for (int i = 0; i < ScaleLevels.Count; i++)
            {
                ScaleLevel level = ScaleLevels[i];
                if (level == null) continue;
                level.Selected = level == scaleLevel;
            }
        }

        scaleLevel.Selected = true;
        CurrentScaleLevel = scaleLevel;
        CurrentPreviewScaleLevel = scaleLevel;
        // Baseline for the stored-child path in SetScaleLevel. Without this, scrubbing
        // back to the restored level indexes an empty _childScales list and throws.
        StoreChildScales();
    }

    /// <summary>
    /// Elevation / export: ensure CurrentScaleLevel points at a catalog entry with Size
    /// without touching transforms. Fixes dual-selectables where CurrentScaleLevel was cleared.
    /// </summary>
    public void EnsureCurrentScaleLevelFromCatalog()
    {
        // Prefab serializes an orphan CurrentScaleLevel with Size=0 — treat that as unbound.
        if (CurrentScaleLevel != null && CurrentScaleLevel.Size > 0f
            && ScaleLevels != null && ScaleLevels.Contains(CurrentScaleLevel))
            return;
        if (ScaleLevels == null || ScaleLevels.Count == 0)
            return;

        float liveZ = transform.localScale.z;
        float beforeSize = CurrentScaleLevel != null ? CurrentScaleLevel.Size : 0f;
        bool beforeInList = CurrentScaleLevel != null
            && ScaleLevels.Contains(CurrentScaleLevel);

        ScaleLevel pick = ScaleLevels.FirstOrDefault(s => s != null && s.Selected && s.Size > 0f)
            ?? ScaleLevels.FirstOrDefault(s =>
                s != null && s.Size > 0f && s.ScaleZ > 0.01f && Mathf.Abs(s.ScaleZ - liveZ) < 0.02f)
            ?? ScaleLevels.FirstOrDefault(s => s != null && s.Size > 0f);
        if (pick == null)
        {
            if (Selectable.IsInElevationPhotoMode)
            {
                Debug.LogWarning(
                    $"[ElevDim] ScaleLevel rebind FAILED {ElevationLengthFormat.DiagnoseSizeBinding(this)} " +
                    "— ScaleLevels present but no Size>0 entry");
            }
            return;
        }

        RestoreScaleLevelFromSave(pick);

        if (Selectable.IsInElevationPhotoMode
            && (beforeSize <= 0f || !beforeInList || !Mathf.Approximately(beforeSize, pick.Size)))
        {
            Debug.Log(
                $"[ElevDim] ScaleLevel rebound name={name} beforeSize={beforeSize:F3} beforeInList={beforeInList} " +
                $"→ size={pick.Size:F3} z={pick.ScaleZ:F3} liveZ={liveZ:F3}");
        }
    }

    /// <summary>
    /// Keep direct AttachmentPoints at world scale (1,1,1) under this length owner.
    /// Same intent as the old local (1,1,1/z) contract; uses world-scale restore so
    /// rotated APs stay correct. Discrete length still goes through <see cref="SetScaleLevel"/>.
    /// </summary>
    public void EnsureAttachChainScaleCompensation(bool reapplyMeshIsolation = false)
    {
        // Room geometry uses localScale as dimensions — never invent AP inverses from that.
        if (!string.IsNullOrEmpty(GUID)
            && (ConfigurationManager.IsRoomBoundary(GUID)
                || ConfigurationManager.IsBaseboard(GUID)
                || ConfigurationManager.IsWallProtector(GUID)))
        {
            return;
        }

        float targetZ = ResolveAttachChainTargetZ();
        if (targetZ <= 0.0001f || Mathf.Abs(targetZ - 1f) < 0.0001f)
        {
            ScaleAuditLog.Event("Sel.EnsureAttachChain",
                $"skip near-1 path={name} local={transform.localScale} currentScaleZ={(CurrentScaleLevel != null ? CurrentScaleLevel.ScaleZ.ToString("G6") : "null")}");
            return;
        }

        // Never rewrite tube Z here — SetScaleLevel / RestoreTransform / gizmo own that.
        float inv = 1f / targetZ;
        ScaleAuditLog.Event("Sel.EnsureAttachChain",
            $"begin path={name} targetZ={targetZ:G6} inv={inv:G6} " +
            $"tubeLocal={transform.localScale} tubeLossy={transform.lossyScale} childCount={transform.childCount}");

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            bool isAttach = child.GetComponent<AttachmentPoint>() != null
                || child.name.Equals("AttachmentPoint", StringComparison.OrdinalIgnoreCase)
                || child.name.Equals("AttachPoint", StringComparison.OrdinalIgnoreCase);
            if (!isAttach)
                continue;

            Vector3 cls = child.localScale;
            Vector3 lossy = child.lossyScale;
            bool worldOk = Mathf.Abs(Mathf.Abs(lossy.x) - 1f) < 0.05f
                && Mathf.Abs(Mathf.Abs(lossy.y) - 1f) < 0.05f
                && Mathf.Abs(Mathf.Abs(lossy.z) - 1f) < 0.05f;
            if (worldOk)
            {
                ScaleAuditLog.Event("Sel.EnsureAttachChain.child",
                    $"skip-ok child={child.name} local={cls} lossy={lossy}");
                StripAbsorbedAttachInverseFromChildren(child, targetZ, inv);
                continue;
            }

            AttachmentPoint.SetWorldScale(child, Vector3.one);
            StripAbsorbedAttachInverseFromChildren(child, targetZ, inv);
            ScaleAuditLog.Warn("Sel.EnsureAttachChain.child",
                $"WRITE child={child.name} before={cls} after={child.localScale} " +
                $"lossyAfter={child.lossyScale}");
        }

        // Callers that process many selectables should run a second pass of
        // ReapplyLengthScaleIsolation after every AP is cleaned (see FixLoaded / save).
        if (reapplyMeshIsolation)
            ReapplyLengthScaleIsolation();
    }

    /// <summary>
    /// Prefer an intentional ScaleLevel Z. Fall back to local Z only for free-scale objects
    /// (no positive discrete levels). Never treat a parenting bake (e.g. arm local Z=1.25 under
    /// drop-tube 0.8) as a discrete length when ScaleLevels exist but CurrentScaleLevel is unset.
    /// </summary>
    private float ResolveAttachChainTargetZ()
    {
        float localZ = transform.localScale.z;
        float levelZ = CurrentScaleLevel != null ? CurrentScaleLevel.ScaleZ : 0f;

        bool hasPositiveLevels = false;
        if (ScaleLevels != null)
        {
            for (int i = 0; i < ScaleLevels.Count; i++)
            {
                ScaleLevel level = ScaleLevels[i];
                if (level != null && level.ScaleZ > 0.0001f)
                {
                    hasPositiveLevels = true;
                    break;
                }
            }
        }

        if (levelZ > 0.0001f)
            return levelZ;

        if (!hasPositiveLevels)
        {
            // Free-scale length tubes only (drop/cover ~0.25–0.7). Room walls use localScale
            // as dimensions (often >3) and must not invent AP inverses.
            if (localZ < 0.05f || localZ > 2.5f)
                return 0f;
            return localZ;
        }

        // Discrete-scale object with unset CurrentScaleLevel: only trust local Z if it matches a level.
        for (int i = 0; i < ScaleLevels.Count; i++)
        {
            ScaleLevel level = ScaleLevels[i];
            if (level != null && level.ScaleZ > 0.0001f && Mathf.Abs(level.ScaleZ - localZ) < 0.05f)
                return level.ScaleZ;
        }

        return 0f;
    }

    /// <summary>
    /// Untracked mesh children (ArmSegment_1.002, BoomSegment mesh pieces) are not serialized.
    /// After load they sit at prefab (1,1,1) under a length-scaled parent. Rebuild the same
    /// full inverse SetScaleLevel applies when scaling from identity → targetZ.
    /// </summary>
    public void RepairUntrackedChildInversesAfterLoad()
    {
        ReapplyLengthScaleIsolation();
    }

    /// <summary>
    /// Rebuild length isolation after load (untracked mesh wrappers are not serialized).
    /// Resets to unit-length baseline then runs <see cref="SetScaleLevel"/> so load matches
    /// the interactive world-scale preserve path.
    /// </summary>
    public void ReapplyLengthScaleIsolation()
    {
        ScaleLevel target = CurrentScaleLevel;
        float targetZ = target != null ? target.ScaleZ : 0f;
        if (target == null || targetZ <= 0.0001f)
            return;
        if (Mathf.Abs(targetZ - 1f) < 0.001f)
            return;

        // Reset to unit-length baseline so SetScaleLevel rebuilds isolation from identity.
        Vector3 ls = transform.localScale;
        transform.localScale = new Vector3(ls.x, ls.y, 1f);

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null) continue;

            bool isAttach = child.GetComponent<AttachmentPoint>() != null
                || child.name.Equals("AttachmentPoint", StringComparison.OrdinalIgnoreCase)
                || child.name.Equals("AttachPoint", StringComparison.OrdinalIgnoreCase);
            if (isAttach)
            {
                child.localScale = Vector3.one;
                continue;
            }

            if (child.GetComponent<TrackedObject>() != null)
                continue;
            if (child.GetComponent<Selectable>() != null)
                continue;

            child.localScale = Vector3.one;
        }

        // Force the world-preserve path (not restored _childScales).
        CurrentScaleLevel = null;
        SetScaleLevel(target, setSelected: true, fireEvent: false);

        // SetScaleLevel snaps to _originalRotation under non-uniform ancestors; that can
        // drift child world poses. Re-assert saved local pos/rot (scale stays).
        if (TryGetComponent(out TrackedObject selfTracked))
            selfTracked.RestoreLocalPoseKeepingScale();
        TrackedObject[] trackedKids = GetComponentsInChildren<TrackedObject>(true);
        for (int t = 0; t < trackedKids.Length; t++)
        {
            TrackedObject tracked = trackedKids[t];
            if (tracked == null || tracked == selfTracked) continue;
            tracked.RestoreLocalPoseKeepingScale();
        }

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null) continue;
            if (child.GetComponent<TrackedObject>() != null) continue;
            if (child.GetComponent<Selectable>() != null) continue;
            ScaleAuditLog.Warn("Sel.ReapplyLengthIsolation",
                $"parent={name} child={child.name} after={child.localScale} " +
                $"targetZ={targetZ:G6} lossyAfter={child.lossyScale}");
        }
    }

    /// <summary>
    /// True when this selectable owns length ScaleLevels (vs empty outer assembly wrapper).
    /// </summary>
    private bool OwnsLengthScale()
    {
        if (CurrentScaleLevel != null && CurrentScaleLevel.ScaleZ > 0.0001f)
            return true;
        if (ScaleLevels == null || ScaleLevels.Count == 0)
            return false;
        for (int i = 0; i < ScaleLevels.Count; i++)
        {
            ScaleLevel level = ScaleLevels[i];
            if (level != null && (level.ScaleZ > 0.0001f || level.Size > 0.0001f))
                return true;
        }
        return IsGizmoSettingAllowed(GizmoType.Scale, Axis.Z);
    }

    /// <summary>
    /// Before parenting a new object under an attach point, ensure the owning selectable's
    /// AP has inverse Z. Does not re-derive mesh isolation (live attach must not Reset→SetScaleLevel).
    /// </summary>
    public static void EnsureAttachChainForAttachmentPoint(AttachmentPoint ap)
    {
        if (ap == null) return;
        Transform p = ap.transform != null ? ap.transform.parent : null;
        Selectable fallback = null;
        while (p != null)
        {
            if (p.TryGetComponent(out Selectable sel))
            {
                if (sel.OwnsLengthScale())
                {
                    sel.EnsureAttachChainScaleCompensation(reapplyMeshIsolation: false);
                    return;
                }
                if (fallback == null)
                    fallback = sel;
            }
            p = p.parent;
        }
        fallback?.EnsureAttachChainScaleCompensation(reapplyMeshIsolation: false);
    }

    /// <summary>
    /// If an attached object carries a canceling inverse that belongs on the AP instead
    /// (Z≈1/parentZ, or equal-XY shear like 0.89,0.89,1 under a polluted AP XY), reset it.
    /// </summary>
    private static void StripAbsorbedAttachInverseFromChildren(Transform ap, float parentTargetZ, float inv)
    {
        if (ap == null) return;

        for (int c = 0; c < ap.childCount; c++)
        {
            Transform attached = ap.GetChild(c);
            if (attached == null) continue;

            Vector3 als = attached.localScale;

            // Never rewrite intentional service-head shelf SKU scales (length-only X).
            if (attached.name != null
                && attached.name.StartsWith("SH_Shelf", StringComparison.Ordinal))
                continue;

            // Equal-XY shear from parenting under polluted AP XY (1.12,1.12,*) → child (0.89,0.89,1).
            bool equalXyShear =
                Mathf.Abs(als.x - als.y) < 0.02f
                && Mathf.Abs(als.x - 1f) > 0.05f
                && Mathf.Abs(als.x) > 0.2f && Mathf.Abs(als.x) < 5f
                && Mathf.Abs(als.z - 1f) < 0.05f;

            bool absorbedZ = Mathf.Abs(als.z - 1f) > 0.05f
                && Mathf.Abs(als.z * parentTargetZ - 1f) < 0.05f
                && Mathf.Abs(als.z - inv) < 0.05f;

            if (!equalXyShear && !absorbedZ)
                continue;

            // Don't strip a selectable whose active ScaleLevel intentionally is this Z.
            if (absorbedZ && attached.TryGetComponent(out Selectable sel) && sel.CurrentScaleLevel != null)
            {
                float sz = sel.CurrentScaleLevel.ScaleZ;
                float size = sel.CurrentScaleLevel.Size;
                if (sz > 0.0001f && size > 0.0001f && Mathf.Abs(sz - als.z) < 0.05f
                    && Mathf.Abs(sz - inv) > 0.05f)
                    continue;
            }

            Vector3 fixedScale = new Vector3(1f, 1f, equalXyShear ? als.z : 1f);
            if (absorbedZ) fixedScale.z = 1f;
            attached.localScale = fixedScale;
            ScaleAuditLog.Warn("Sel.EnsureAttachChain.stripAbsorbed",
                $"ap={ap.name} child={attached.name} before={als} after={fixedScale} " +
                $"parentTargetZ={parentTargetZ:G6} inv={inv:G6}");
        }
    }

    /// <summary>
    /// World meters of this part's authored mesh length at localScale.z = 1
    /// (own renderers only). Catalog <see cref="ScaleLevel.Size"/> is also meters —
    /// ScaleZ should be Size / authored length.
    /// </summary>
    public float GetAuthoredLengthMeters()
    {
        Vector3 saved = transform.localScale;
        bool restored = false;
        if (Mathf.Abs(saved.z - 1f) > 1e-4f)
        {
            transform.localScale = new Vector3(saved.x, saved.y, 1f);
            Physics.SyncTransforms();
            restored = true;
        }

        var probe = ScaleAuditLog.CaptureLengthProbe(transform);
        // SelfLength stops at child Selectables — never use TipDistance here; that walks
        // the whole assembly and can inflate authored length after attach (wrong ScaleZ).
        float meshLen = probe.SelfLength;

        if (restored)
        {
            transform.localScale = saved;
            Physics.SyncTransforms();
        }

        if (meshLen > 0.05f)
            return meshLen;

        var md = ScaleLevels?.FirstOrDefault(l => l != null && l.ModelDefault);
        return md != null && md.Size > 0.05f ? md.Size : 1f;
    }

    /// <summary>
    /// Service heads / SH rails carry row-count metadata on ScaleLevels. Those still use
    /// normal <see cref="SetScaleLevel"/> (git: OnScaleChange → ReassembleRows) but must
    /// bake ScaleZ from ModelDefault Size ratios — not mesh length (Size is a tier proxy).
    /// </summary>
    public bool UsesScaleLevelsAsRowConfig()
    {
        if (GetComponent<BoomHeadScaleHandler>() != null)
            return true;
        if (ScaleLevels == null || ScaleLevels.Count == 0)
            return false;
        for (int i = 0; i < ScaleLevels.Count; i++)
        {
            var level = ScaleLevels[i];
            if (level != null && level.TryGetValue("rows", out _))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Write ScaleZ on every level. Arms/tubes: Size / authored mesh meters.
    /// Row-config heads/rails: Size / ModelDefault.Size with MD ScaleZ=1 (historic init).
    /// </summary>
    public void BakeScaleZFromAuthoredLength()
    {
        if (ScaleLevels == null || ScaleLevels.Count == 0)
            return;

        // Historic InitializeAfterStart for boom heads: ModelDefault.ScaleZ = 1,
        // others ScaleZ = Size / ModelDefault.Size. Do not use mesh AABB — Size tiers
        // track rows (and legacy "Service Head Lengths"), not authored mesh meters.
        if (UsesScaleLevelsAsRowConfig())
        {
            var md = ScaleLevels.FirstOrDefault(l => l != null && l.ModelDefault)
                ?? ScaleLevels.FirstOrDefault(l => l != null && l.Size > 0f);
            float refSize = md != null && md.Size > 1e-4f ? md.Size : 1f;
            for (int i = 0; i < ScaleLevels.Count; i++)
            {
                var item = ScaleLevels[i];
                if (item == null)
                    continue;
                if (item.ModelDefault || item.Size <= 0f)
                    item.ScaleZ = 1f;
                else
                    item.ScaleZ = item.Size / refSize;
            }
            if (md != null)
                md.ScaleZ = 1f;
            ScaleAuditLog.Event("Sel.BakeScaleZ.rowConfig",
                $"name={name} mdRef={refSize:G4} " +
                $"levels={string.Join(",", ScaleLevels.Where(l => l != null).Select(l => $"{l.Size:G4}→{l.ScaleZ:G4}"))}");
            return;
        }

        float authored = GetAuthoredLengthMeters();
        if (authored < 1e-4f)
            authored = 1f;

        for (int i = 0; i < ScaleLevels.Count; i++)
        {
            var item = ScaleLevels[i];
            if (item == null || item.Size <= 0f)
                continue;
            item.ScaleZ = item.Size / authored;
        }

        ScaleAuditLog.Event("Sel.BakeScaleZ",
            $"name={name} authored={authored:G6} " +
            $"levels={string.Join(",", ScaleLevels.Where(l => l != null).Select(l => $"{l.Size:G4}→{l.ScaleZ:G4}"))}");
    }

    public void SetScaleLevel(ScaleLevel scaleLevel, bool setSelected, bool fireEvent = true)
    {
        if (scaleLevel == null)
        {
            Debug.LogWarning($"SetScaleLevel called with null on {name}", this);
            return;
        }

        // Prefab ScaleZ is 0 until InitializeAfterStart bakes Size ratios — never collapse the tube.
        if (scaleLevel.ScaleZ <= 0.0001f)
        {
            ScaleAuditLog.Warn("Sel.SetScaleLevel.skipNonPositiveZ",
                $"name={name} size={scaleLevel.Size} scaleZ={scaleLevel.ScaleZ:G6}");
            if (setSelected)
            {
                ScaleLevels.ForEach((item) => item.Selected = false);
                scaleLevel.Selected = true;
                CurrentScaleLevel = scaleLevel;
                CurrentPreviewScaleLevel = scaleLevel;
            }
            else
            {
                CurrentPreviewScaleLevel = scaleLevel;
            }
            return;
        }

        ScaleAuditLog.LengthProbe lengthBefore = ScaleAuditLog.CaptureLengthProbe(transform);
        ScaleAuditLog.Event("Sel.SetScaleLevel.begin",
            $"name={name} setSelected={setSelected} fireEvent={fireEvent} " +
            $"requestedScaleZ={scaleLevel.ScaleZ:G6} size={scaleLevel.Size} " +
            $"beforeLocal={transform.localScale} beforeLossy={transform.lossyScale} " +
            $"currentScaleZ={(CurrentScaleLevel != null ? CurrentScaleLevel.ScaleZ.ToString("G6") : "null")} " +
            $"selfLen={lengthBefore.SelfLength:G6} selfDiam={lengthBefore.SelfDiameter:G6} " +
            $"tipDist={lengthBefore.TipDistance:G6} " +
            $"child0={lengthBefore.FirstChildName ?? "none"} child0Lossy={lengthBefore.FirstChildLossy} " +
            $"down={lengthBefore.DownstreamName ?? "none"} downLossy={lengthBefore.DownstreamLossy}");

        CurrentPreviewScaleLevel = scaleLevel;

        if (fireEvent)
        {
            OnScaleChange?.Invoke(CurrentPreviewScaleLevel);
        }

        // Client intent (unchanged): lengthen THIS selectable only; keep direct children
        // at the world size they had so nothing attached downstream inherits the stretch.
        // Old path used InverseTransformVector (breaks on cardanic/45° joints). Same intent,
        // rotation-safe: capture child world scale → write tube Z → SetWorldScale restore.
        bool usedStoredChildScales = false;
        bool usedCalculateInverse = false;

        bool canUseStored = scaleLevel == CurrentScaleLevel
            && _childScales != null
            && _childScales.Count == transform.childCount;

        if (scaleLevel == CurrentScaleLevel && !canUseStored)
        {
            ScaleAuditLog.Warn("Sel.SetScaleLevel.storedMismatch",
                $"name={name} childCount={transform.childCount} stored={(_childScales != null ? _childScales.Count : 0)} — using worldPreserve");
        }

        Vector3 newScale = new Vector3(transform.localScale.x, transform.localScale.y, scaleLevel.ScaleZ);

        if (canUseStored)
        {
            usedStoredChildScales = true;
            transform.localScale = newScale;
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).localScale = _childScales[i];
        }
        else
        {
            usedCalculateInverse = true;
            IsolateDirectChildrenPreservingWorldScale(newScale);
        }

        if (setSelected)
        {
            ScaleLevels.ForEach((item) => item.Selected = false);
            scaleLevel.Selected = true;
            CurrentScaleLevel = scaleLevel;

            if (fireEvent)
            {
                OnScaleChange?.Invoke(CurrentScaleLevel);
            }

            //if (TryGetComponent(out ScaleGroup group))
            //{
            //    ScaleGroupManager.OnScaleLevelChanged?.Invoke(group.id, CurrentScaleLevel);
            //}

            StoreChildScales();
        }

        ScaleAuditLog.LogLengthIsolation(
            "Sel.SetScaleLevel.length",
            transform,
            lengthBefore,
            scaleLevel.ScaleZ,
            scaleLevel.Size,
            setSelected,
            usedStoredChildScales,
            usedCalculateInverse);

        if (ScaleAuditLog.VerboseHierarchy)
        {
            ScaleAuditLog.Hierarchy("Sel.SetScaleLevel.after", transform,
                $"name={name} scaleZ={(scaleLevel != null ? scaleLevel.ScaleZ.ToString("G6") : "null")} setSelected={setSelected}");
        }

        //if (TryGetComponent(out ScaleGroup _))
        //{
        //    transform.SetParent(oldParent);
        //}
    }

    /// <summary>
    /// After changing this tube's local Z, restore each isolatable direct child's world
    /// scale so the next arm / light head does not inherit the stretch. Attach points and
    /// untracked mesh wrappers are forced to world (1,1,1); length-capable Selectable
    /// children keep the world size they had before the tube change.
    /// </summary>
    private void IsolateDirectChildrenPreservingWorldScale(Vector3 newParentLocalScale)
    {
        int n = transform.childCount;
        if (n == 0)
        {
            transform.localScale = newParentLocalScale;
            return;
        }

        var targetWorld = new Vector3[n];
        var mode = new byte[n]; // 0=skip, 1=worldOne, 2=preserveWorld
        bool boomHead = GetComponent<BoomHeadScaleHandler>() != null;

        for (int i = 0; i < n; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null)
            {
                mode[i] = 0;
                continue;
            }

            if (child.TryGetComponent(out IgnoreInverseScaling ignore)
                && ignore.IgnoreX && ignore.IgnoreY && ignore.IgnoreZ)
            {
                mode[i] = 0;
                continue;
            }

            bool isAttach = child.GetComponent<AttachmentPoint>() != null
                || child.name.Equals("AttachmentPoint", StringComparison.OrdinalIgnoreCase)
                || child.name.Equals("AttachPoint", StringComparison.OrdinalIgnoreCase);

            if (isAttach)
            {
                mode[i] = 1; // world (1,1,1)
                targetWorld[i] = Vector3.one;
                continue;
            }

            if (child.GetComponent<Selectable>() != null)
            {
                // Keep whatever world size this selectable already had (length or not).
                mode[i] = 2;
                targetWorld[i] = child.lossyScale;
                continue;
            }

            // Untracked mesh wrapper (.002, boom mesh pieces): world (1,1,1) so tip APs
            // and attached gear do not inherit tube stretch. Boom-head children preserve
            // their current world size (rails/shelves may not be unit).
            mode[i] = boomHead ? (byte)2 : (byte)1;
            targetWorld[i] = boomHead ? child.lossyScale : Vector3.one;
        }

        transform.localScale = newParentLocalScale;

        for (int i = 0; i < n; i++)
        {
            if (mode[i] == 0) continue;
            Transform child = transform.GetChild(i);
            if (child == null) continue;
            AttachmentPoint.SetWorldScale(child, targetWorld[i]);
        }
    }

    public void UpdateZScaling(bool setSelected)
    {
        if (ScaleLevels == null || ScaleLevels.Count == 0) return;

        // Broken / free-scale lists (all ScaleZ ≈ 0): do not snap to a zero level.
        bool anyPositive = false;
        for (int i = 0; i < ScaleLevels.Count; i++)
        {
            if (ScaleLevels[i] != null && ScaleLevels[i].ScaleZ > 0.0001f)
            {
                anyPositive = true;
                break;
            }
        }
        if (!anyPositive) return;

        //get closest scale in list
        ScaleLevel closest = ScaleLevels.OrderBy(item => Math.Abs(_gizmoHandler.CurrentScaleDrag.z - item.ScaleZ)).First();

        if (closest == CurrentPreviewScaleLevel && !setSelected)
            return;

        SetScaleLevel(closest, setSelected);
        ScaleUpdated?.Invoke();
    }

    public void StoreChildScales()
    {
        _childScales.Clear();

        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            _childScales.Add(child.transform.localScale);
            //Debug.Log($"Storing child scales");
        }
    }

    public bool TryGetGizmoSetting(GizmoType gizmoType, Axis axis, out GizmoSetting gizmoSetting)
    {
        gizmoSetting = default;
        if (!GizmoSettings.ContainsKey(gizmoType)) return false;
        if (!GizmoSettings[gizmoType].ContainsKey(axis)) return false;
        gizmoSetting = GizmoSettings[gizmoType][axis];
        return true;
    }

    public bool IsGizmoSettingAllowed(GizmoType gizmoType, Axis axis) => TryGetGizmoSetting(gizmoType, axis, out _);

    public float GetGizmoSettingMaxValue(GizmoType gizmoType, Axis axis)
    {
        if (TryGetGizmoSetting(gizmoType, axis, out GizmoSetting gizmoSetting))
        {
            return gizmoSetting.GetMaxValue();
        }
        else return 0;
    }

    public float GetGizmoSettingMinValue(GizmoType gizmoType, Axis axis)
    {
        if (TryGetGizmoSetting(gizmoType, axis, out GizmoSetting gizmoSetting))
        {
            return gizmoSetting.GetMinValue();
        }
        else return 0;
    }

    public bool GetGizmoSettingTranslateIgnoreBool()
    {
        if (TryGetGizmoSetting(GizmoType.Move, Axis.Z, out GizmoSetting gizmoSetting))
        {
            return gizmoSetting.IgnoreScale;
        }
        else return false;
    }

    #endregion

    #region PDF

    private bool IsHittingCeiling()
    {
        //RoomBoundary ceiling = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling);

        float width = RoomSize.Instance.CurrentDimensions.Width.ToMeters();
        float depth = RoomSize.Instance.CurrentDimensions.Depth.ToMeters();
        float height = RoomSize.Instance.CurrentDimensions.Height.ToMeters();
        var bounds = new Bounds(RoomSize.Bounds.center, new Vector3(width, height, depth));

        if (!TryGetComponent<MeshFilter>(out var meshFilter))
            return false;

        var verts = meshFilter.sharedMesh.vertices;

        foreach (var vert in verts)
        {
            var transformedVert = transform.TransformPoint(vert);

            if (transformedVert.y > bounds.max.y)
                return true;
        }

        return false;
    }
    public IEnumerator CapturePdfDataForExport(
    string title,
    string subtitle,
    List<AssemblyData> assemblyDatas,
    Action<List<PdfExporterLocal.PdfImageData>, List<Selectable>> onComplete)
    {
        if (!TryGetArmAssemblyRoot(out GameObject rootObj))
        {
            Debug.LogWarning($"[ElevDim] CapturePdfDataForExport: no arm assembly root on {name}");
            onComplete?.Invoke(new List<PdfExporterLocal.PdfImageData>(), null);
            yield break;
        }

        if (rootObj != gameObject)
        {
            var rootSelectable = rootObj.GetComponent<Selectable>();
            yield return rootSelectable.CapturePdfDataForExport(title, subtitle, assemblyDatas, onComplete);
            yield break;
        }

        IsInElevationPhotoMode = true;
        UI_ToggleProximityAlerts.BeginCaptureSuppress();
        bool floorWasActive = true;
        RoomBoundary floorBoundary = null;
        List<(Selectable selectable, bool wasActive)> visibilitySnapshot = null;
        List<PdfExporterLocal.PdfImageData> imageData = new();
        try
        {
            var camera = GetComponentInChildren<Camera>();
            ActiveCameraRenderTextureElevation = camera;
            SuppressHighlightsForCapture(_assemblySelectables);

            // Hide the 3D floor mesh during capture — the PDF ground graphic is the floor.
            floorBoundary = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor);
            if (floorBoundary != null)
            {
                floorWasActive = floorBoundary.gameObject.activeSelf;
                floorBoundary.gameObject.SetActive(false);
            }

            SetAssemblyToDefaultRotations();
            _measurableActiveStates.Clear();
            ToggleMeasurableActiveStates(true);

            visibilitySnapshot = ActiveSelectables
                .Where(x => x != null)
                .Select(x => (x, x.gameObject.activeSelf))
                .ToList();

            if (camera == null)
                throw new Exception($"Elevation capture camera missing on assembly root {name}");

            ActiveSelectables
                .Where(x => x != null && !_assemblySelectables.Contains(x))
                .ToList()
                .ForEach(x => x.gameObject.SetActive(false));

            // Single capture pass — front and back share one union-bounds frame.
            var captured = GetAssemblyPDFImageData(camera);
            if (captured != null)
            {
                foreach (var img in captured)
                {
                    if (img == null || string.IsNullOrEmpty(img.Path))
                        continue;
                    imageData.Add(new PdfExporterLocal.PdfImageData
                    {
                        Path = img.Path,
                        Width = img.Width > 0 ? img.Width : 1000,
                        Height = img.Height > 0 ? img.Height : 1000
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ElevDim] Capture failed for {name}: {ex}");
        }
        finally
        {
            if (visibilitySnapshot != null)
            {
                foreach (var (selectable, wasActive) in visibilitySnapshot)
                {
                    if (selectable != null && selectable.gameObject != null)
                        selectable.gameObject.SetActive(wasActive);
                }
            }

            RestoreArmAssemblyRotations();
            if (_assemblySelectables != null)
                _assemblySelectables.ForEach(x => { if (x != null) x.FaceZTowardGround(); });
            if (floorBoundary != null)
                floorBoundary.gameObject.SetActive(floorWasActive);
            // Tear down overlays while still in elev mode (Toggle uses that flag),
            // then hard-clear anything left so dims never stick in the live scene.
            ToggleMeasurableActiveStates(false);
            IsInElevationPhotoMode = false;
            ElevationCutsheetPass.EndCaptureCleanup();
            // Final wipe after elev flag is off — OnEnable/CheckActiveState must not
            // revive measurement labels in the live room.
            ElevationCutsheetPass.SuppressAllOverlays();
            ActiveCameraRenderTextureElevation = null;
            UI_ToggleProximityAlerts.EndCaptureSuppress();

            // Always complete — PdfBatchExporter waits on this callback; skipping it hangs
            // the multipage export so later assemblies never get a page.
            onComplete?.Invoke(imageData, _assemblySelectables);
        }
    }

    public List<PdfExporter.PdfImageData> GetAssemblyPDFImageData(Camera camera)
    {
        var imageDatas = new List<PdfExporter.PdfImageData>();

        // Local helper to apply orientation for index (0=front,1=back) including ceiling avoidance
        void ApplyOrientationForIndex(int i)
        {
            void FaceAllTowardGround()
            {
                _assemblySelectables
                    .Where(x => x.ZAlwaysFacesGround || x.ZAlwaysFacesGroundElevationOnly)
                    .ToList()
                    .ForEach(item => item.FaceZTowardGround());
            }

            foreach (Selectable selectable in _assemblySelectables.Where(x => x.ChangeHeightForElevationPhoto))
            {
                var newAngles = selectable.transform.localEulerAngles;
                var gizmoSetting = selectable.GizmoSettings[GizmoType.Rotate][Axis.Y];

                if (i == 0)
                {
                    newAngles.y = gizmoSetting.Invert
                        ? gizmoSetting.GetMaxValue()
                        : gizmoSetting.GetMinValue();
                }
                else
                {
                    newAngles.y = gizmoSetting.Invert
                        ? gizmoSetting.GetMinValue()
                        : gizmoSetting.GetMaxValue();
                }

                selectable.transform.localEulerAngles = newAngles;
                var childList = selectable.GetComponentsInChildren<Selectable>().ToList();

                FaceAllTowardGround();
                while (childList.Any(x => x.IsHittingCeiling()))
                {
                    float abs = Mathf.Abs(newAngles.y) - 0.1f;
                    if (abs < 0f) break;
                    newAngles.y = abs * Mathf.Sign(newAngles.y);
                    selectable.transform.localEulerAngles = newAngles;
                    FaceAllTowardGround();
                }
                selectable.transform.localEulerAngles = newAngles;
            }

            FaceAllTowardGround();
            // Colliders lag transforms after articulation — floor underside casts must
            // see the pose that renderers already show (else ticks float under raised arms).
            Physics.SyncTransforms();

            // After pitch snaps: yaw any plan-end-on arms so reach reads in profile.
            ApplyElevationProfileYaw(invertDirection: i == 1);
            FaceAllTowardGround();
            Physics.SyncTransforms();
        }

        // First pass: compute unified bounds that fit both orientations including measurement overlays
        // Proposal preview only embeds the front elevation — skip the back pass entirely.
        int viewCount = ProposalPDFGenerator.FastPreviewCapture ? 1 : 2;
        Bounds? unionBoundsNullable = null;
        for (int i = 0; i < viewCount; i++)
        {
            ApplyOrientationForIndex(i);
            var baseBounds = GetAssemblyBounds();
            var expanded = ComputeExpandedBoundsForOrientation(camera, baseBounds, invertDirection: (i == 1));
            if (unionBoundsNullable == null)
            {
                unionBoundsNullable = expanded;
            }
            else
            {
                var ub = unionBoundsNullable.Value;
                ub.Encapsulate(expanded);
                unionBoundsNullable = ub;
            }
        }
        var unionBounds = unionBoundsNullable ?? GetAssemblyBounds();
        // Floor bar / ceiling height on the PDF are gospel — never fit Y to the boom AABB
        // (boom-only used to zoom in and park arms on the floor graphic).
        unionBounds = LockElevationVerticalToRoom(unionBounds);

        // Second pass: capture (front only in fast preview, front+back for real exports)
        for (int i = 0; i < viewCount; i++)
        {
            ApplyOrientationForIndex(i);

            string path = CaptureElevationWithFixedBounds(
                camera,
                unionBounds,
                out var imageWidth,
                out var imageHeight,
                fileIndex: i,
                invertDirection: (i == 1)
            );

            imageDatas.Add(new PdfExporter.PdfImageData
            {
                Path = path,
                Width = imageWidth,
                Height = imageHeight
            });
        }

        // Ensure both images share the same dimensions by padding the smaller to the larger
        if (imageDatas.Count == 2)
        {
            int targetWidth = Mathf.Max(imageDatas[0].Width, imageDatas[1].Width);
            int targetHeight = Mathf.Max(imageDatas[0].Height, imageDatas[1].Height);

            for (int idx = 0; idx < imageDatas.Count; idx++)
            {
                var img = imageDatas[idx];
                if (img.Width != targetWidth || img.Height != targetHeight)
                {
                    PadImageToSize(img.Path, img.Width, img.Height, targetWidth, targetHeight, Color.white);
                    img.Width = targetWidth;
                    img.Height = targetHeight;
                    imageDatas[idx] = img;
                }
            }
        }

        return imageDatas;
    }


    public void ExportElevationPdf(string title, string subtitle, List<AssemblyData> assemblyDatas)
    {
        if (TryGetArmAssemblyRoot(out GameObject rootObj))
        {
            if (rootObj != gameObject)
            {
                rootObj.GetComponent<Selectable>().ExportElevationPdf(title, subtitle, assemblyDatas);
                Debug.Log($"going to root object! root {rootObj.name} and current {gameObject.name}");
                return;
            }

            // this obj is the ceiling mount
            IsInElevationPhotoMode = true;
            UI_ToggleProximityAlerts.BeginCaptureSuppress();
            bool floorWasActive = true;
            RoomBoundary floorBoundary = null;
            List<(Selectable selectable, bool wasActive)> visibilitySnapshot = null;

            try
            {
                var camera = GetComponentInChildren<Camera>();
                ActiveCameraRenderTextureElevation = camera;
                SuppressHighlightsForCapture(_assemblySelectables);

                floorBoundary = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor);
                if (floorBoundary != null)
                {
                    floorWasActive = floorBoundary.gameObject.activeSelf;
                    floorBoundary.gameObject.SetActive(false);
                }

                SetAssemblyToDefaultRotations();
                _measurableActiveStates.Clear();
                ToggleMeasurableActiveStates(true);

                visibilitySnapshot = ActiveSelectables
                    .Where(x => x != null)
                    .Select(x => (x, x.gameObject.activeSelf))
                    .ToList();

                //shut off all selectables in the scene except for the ones in this arm assembly
                ActiveSelectables
                    .Where(x => x != null && !_assemblySelectables.Contains(x))
                    .ToList()
                    .ForEach(x => x.gameObject.SetActive(false));

                var captured = GetAssemblyPDFImageData(camera);
                var images = new List<PdfExporterLocal.PdfImageData>();
                if (captured != null)
                {
                    foreach (var img in captured)
                    {
                        if (img == null || string.IsNullOrEmpty(img.Path))
                            continue;
                        images.Add(new PdfExporterLocal.PdfImageData
                        {
                            Path = img.Path,
                            Width = img.Width > 0 ? img.Width : 1000,
                            Height = img.Height > 0 ? img.Height : 1000
                        });
                    }
                }

                var datas = assemblyDatas != null && assemblyDatas.Count > 0
                    ? assemblyDatas
                    : UI_PdfExportOptions.GenerateAssemblyDataWithTitles(this);
                var allAssemblyJson = PdfExporterLocal.ConvertToAssemblyJsonFull(
                    datas,
                    UI_PdfExportOptions.GetAdditionalData());
                PdfExporterLocal.ExportElevationPdfLocal(
                    images,
                    title,
                    subtitle,
                    allAssemblyJson,
                    UI_PdfExportOptions.GetProjectMetaData());
            }
            finally
            {
                if (visibilitySnapshot != null)
                {
                    foreach (var (selectable, wasActive) in visibilitySnapshot)
                    {
                        if (selectable != null && selectable.gameObject != null)
                            selectable.gameObject.SetActive(wasActive);
                    }
                }

                    RestoreArmAssemblyRotations();
                if (_assemblySelectables != null)
                    _assemblySelectables.ForEach(x => { if (x != null) x.FaceZTowardGround(); });
                if (floorBoundary != null)
                    floorBoundary.gameObject.SetActive(floorWasActive);
                ToggleMeasurableActiveStates(false);
                IsInElevationPhotoMode = false;
                ElevationCutsheetPass.EndCaptureCleanup();
                ElevationCutsheetPass.SuppressAllOverlays();
                ActiveCameraRenderTextureElevation = null;
                UI_ToggleProximityAlerts.EndCaptureSuppress();
            }
        }
    }



    public List<PdfExporter.PdfImageData> ExportElevationPdf()
    {
        if (TryGetArmAssemblyRoot(out GameObject rootObj))
        {
            if (rootObj != gameObject)
            {
                Debug.Log($"going to root object! root {rootObj.name} and current {gameObject.name}");
                return rootObj.GetComponent<Selectable>().ExportElevationPdf();
            }

            // this obj is the ceiling mount
            IsInElevationPhotoMode = true;
            UI_ToggleProximityAlerts.BeginCaptureSuppress();
            bool floorWasActive = true;
            RoomBoundary floorBoundary = null;
            List<(Selectable selectable, bool wasActive)> visibilitySnapshot = null;

            try
            {
                var camera = GetComponentInChildren<Camera>();
                ActiveCameraRenderTextureElevation = camera;
                SuppressHighlightsForCapture(_assemblySelectables);

                floorBoundary = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor);
                if (floorBoundary != null)
                {
                    floorWasActive = floorBoundary.gameObject.activeSelf;
                    floorBoundary.gameObject.SetActive(false);
                }

                SetAssemblyToDefaultRotations();
                _measurableActiveStates.Clear();
                ToggleMeasurableActiveStates(true);

                visibilitySnapshot = ActiveSelectables
                    .Where(x => x != null)
                    .Select(x => (x, x.gameObject.activeSelf))
                    .ToList();

                //shut off all selectables in the scene except for the ones in this arm assembly
                ActiveSelectables
                    .Where(x => x != null && !_assemblySelectables.Contains(x))
                    .ToList()
                    .ForEach(x => x.gameObject.SetActive(false));

                return GetAssemblyPDFImageData(camera);
            }
            finally
            {
                if (visibilitySnapshot != null)
                {
                    foreach (var (selectable, wasActive) in visibilitySnapshot)
                    {
                        if (selectable != null && selectable.gameObject != null)
                            selectable.gameObject.SetActive(wasActive);
                    }
                }

                    RestoreArmAssemblyRotations();
                if (_assemblySelectables != null)
                {
                    _assemblySelectables.ForEach(x =>
                    {
                        if (x != null) x.FaceZTowardGround();
                    });
                }
                if (floorBoundary != null)
                    floorBoundary.gameObject.SetActive(floorWasActive);
                ToggleMeasurableActiveStates(false);
                IsInElevationPhotoMode = false;
                ElevationCutsheetPass.EndCaptureCleanup();
                ElevationCutsheetPass.SuppressAllOverlays();
                ActiveCameraRenderTextureElevation = null;
                UI_ToggleProximityAlerts.EndCaptureSuppress();
            }
        }

        return null;

    }

    private struct CameraState
    {
        public Vector3 Position;
        public bool Orthographic;
        public float OrthographicSize;
        public bool Enabled;
        public RenderTexture TargetTexture;
    }

    /// <summary>
    /// Orthographic elevation look direction: maximize on-page horizontal arm reach.
    /// Looks along the shorter plan-axis so the longer span (arm length) fills the sheet —
    /// avoids end-on views when arms align with the authored ±Z camera.
    /// </summary>
    private Vector3 GetElevationViewOutwardDirection(Bounds poseBounds, Vector3 authoredOffset, bool invertDirection)
    {
        Vector3 authored = authoredOffset;
        authored.y = 0f;
        if (authored.sqrMagnitude < 1e-8f)
            authored = Vector3.forward;
        authored.Normalize();

        float spanX = poseBounds.size.x; // visible when looking along ±Z
        float spanZ = poseBounds.size.z; // visible when looking along ±X

        Vector3 outward;
        if (spanX + 1e-4f >= spanZ)
        {
            // Arms / footprint span X more — look along Z (classic front elevation).
            outward = Vector3.Dot(authored, Vector3.forward) >= 0f ? Vector3.forward : Vector3.back;
        }
        else
        {
            // Arms span Z more — look along X so reach reads as width on the page.
            outward = Vector3.Dot(authored, Vector3.right) >= 0f ? Vector3.right : Vector3.left;
        }

        if (invertDirection)
            outward = -outward;

        return outward;
    }

    /// <summary>
    /// Elev-only plan yaw: when dual arms sit at right angles, the elev camera's shorter
    /// plan axis makes one arm end-on (no length sense). Rotate horizontal arm roots so
    /// their reach shares one profile direction (existing profile arms, else ±45° to view).
    /// Restored via <see cref="_originalRotations"/> after capture.
    /// </summary>
    void ApplyElevationProfileYaw(bool invertDirection)
    {
        if (_assemblySelectables == null || _assemblySelectables.Count == 0)
            return;

        Bounds poseBounds = GetAssemblyBounds();
        Vector3 authored = Vector3.forward;
        Vector3 viewFlat = GetElevationViewOutwardDirection(poseBounds, authored, invertDirection);
        viewFlat.y = 0f;
        if (viewFlat.sqrMagnitude < 1e-8f)
            return;
        viewFlat.Normalize();

        Vector3 profileDir = Vector3.Cross(Vector3.up, viewFlat);
        if (profileDir.sqrMagnitude < 1e-8f)
            profileDir = Vector3.right;
        profileDir.Normalize();

        var arms = new List<(Selectable sel, Vector3 reach, float span)>();
        foreach (var sel in _assemblySelectables)
        {
            if (!TryGetElevationPlanReach(sel, out Vector3 reach, out float span))
                continue;
            arms.Add((sel, reach, span));
        }
        if (arms.Count == 0)
            return;

        // Prefer the mean reach of arms already readable in profile; else 45° to the view.
        Vector3 target = Vector3.zero;
        int profileCount = 0;
        foreach (var a in arms)
        {
            float endOn = Mathf.Abs(Vector3.Dot(a.reach, viewFlat));
            if (endOn > 0.72f)
                continue;
            Vector3 r = a.reach;
            if (Vector3.Dot(r, profileDir) < 0f)
                r = -r;
            target += r;
            profileCount++;
        }

        if (profileCount > 0)
            target.Normalize();
        else
        {
            // Everything end-on or ambiguous — fan to 45° so length reads on the sheet.
            target = (profileDir + viewFlat).normalized;
            if (target.sqrMagnitude < 1e-6f)
                target = profileDir;
        }

        int yawed = 0;
        foreach (var a in arms)
        {
            float aligned = Mathf.Abs(Vector3.Dot(a.reach, target));
            if (aligned > 0.97f)
                continue;

            float ang = Vector3.SignedAngle(a.reach, target, Vector3.up);
            float angNeg = Vector3.SignedAngle(a.reach, -target, Vector3.up);
            if (Mathf.Abs(angNeg) < Mathf.Abs(ang))
                ang = angNeg;
            if (Mathf.Abs(ang) < 4f)
                continue;

            if (!_originalRotations.ContainsKey(a.sel))
                _originalRotations[a.sel] = a.sel.transform.localRotation;

            a.sel.transform.Rotate(Vector3.up, ang, Space.World);
            yawed++;
        }

        if (yawed > 0)
        {
            Debug.Log(
                $"[ElevDim] Profile yaw applied count={yawed} target=({target.x:F2},{target.z:F2}) " +
                $"view=({viewFlat.x:F2},{viewFlat.z:F2}) arms={arms.Count}");
        }
    }

    /// <summary>
    /// Horizontal boom/light arm length owners with real plan reach — not drop tubes.
    /// </summary>
    static bool TryGetElevationPlanReach(Selectable sel, out Vector3 reachDir, out float span)
    {
        reachDir = Vector3.zero;
        span = 0f;
        if (sel == null)
            return false;

        string n = sel.name ?? string.Empty;
        // Drop tubes / hang columns — same silhouette every elev angle; never profile-yaw.
        if (Measurable.IsDropTubeName(n) || Measurable.IsVerticalHangLengthName(n))
            return false;
        if (n.IndexOf("Ceiling Tube", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Deckentr", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("TurningCover", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("CeilingCover", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        // Prefer catalog length owners (arm segments / Sim.FLEX).
        float catalogM = ElevationLengthFormat.ResolveOwnSizeMeters(sel);

        bool nameLooksHorizontal =
            n.IndexOf("BoomSegment_1", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("TopArm", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("ArmSegment_1", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Sim.FLEX", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("SimFLEX", System.StringComparison.OrdinalIgnoreCase) >= 0;

        // Distal pitch segments / heads follow the proximal arm — do not yaw them alone.
        if (n.IndexOf("BoomSegment_2", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("BoomSegment_3", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("ArmSegment_2", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("ArmSegment_3", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        if (catalogM < 0.2f && !nameLooksHorizontal)
            return false;

        // Mesh plan axis: longest horizontal AABB edge of own renderers.
        if (!Measurable.TryGetOwnRendererBounds(sel, out Bounds rb) || rb.size.sqrMagnitude < 1e-6f)
            return false;

        float sx = rb.size.x;
        float sy = rb.size.y;
        float sz = rb.size.z;
        float horizMax = Mathf.Max(sx, sz);
        if (horizMax < 0.18f)
            return false;
        // Mostly vertical column — skip.
        if (sy > horizMax * 1.35f && horizMax < 0.35f)
            return false;

        // Length axis: boom arms extend along local forward; fall back to long AABB plan axis.
        Vector3 fwd = sel.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 1e-4f)
            reachDir = fwd.normalized;
        else
            reachDir = sx >= sz ? Vector3.right : Vector3.forward;

        span = Mathf.Max(catalogM, horizMax);
        return span >= 0.2f;
    }

    /// <summary>
    /// Captures an elevation photo of the assembly from either the “front” or “back”
    /// depending on the invertDirection flag.
    /// </summary>
    private string GetElevationPhoto(
        Camera camera,
        Bounds bounds,
        out int imageWidth,
        out int imageHeight,
        int fileIndex,
        bool invertDirection = false)
    {
        camera.enabled = true;
        camera.orthographic = true;

        Vector3 cameraOriginalPos = camera.transform.position;
        Vector3 outwardDirection = GetElevationViewOutwardDirection(
            bounds, cameraOriginalPos - transform.position, invertDirection);

        camera.transform.position = bounds.center + (outwardDirection.normalized * bounds.extents.magnitude);
        camera.transform.LookAt(bounds.center, Vector3.up);
        camera.orthographicSize = bounds.extents.y;

        // Cutsheet overlays only — never activate Walls / imperial then erase.
        // Re-clear highlights immediately before RT — outlet panels can stay green.
        SuppressHighlightsForCapture(_assemblySelectables);
        ElevationCutsheetPass.Apply(_assemblySelectables, camera);

        if (_assemblySelectables != null)
        {
            foreach (var item in _assemblySelectables)
            {
                if (item?.Measurables == null) continue;
                foreach (var measurable in item.Measurables)
                {
                    if (measurable?.Measurements == null) continue;
                    foreach (var measurement in measurable.Measurements)
                    {
                        if (measurement?.Measurer == null || !measurement.Measurer.gameObject.activeSelf)
                            continue;
                        if (measurement.Measurer.Renderer != null)
                            bounds.Encapsulate(measurement.Measurer.Renderer.bounds);
                        if (measurement.Measurer.MeasurementText != null
                            && measurement.Measurer.MeasurementText.gameObject.activeSelf)
                        {
                            bounds.Encapsulate(new Bounds(
                                measurement.Measurer.MeasurementText.transform.position,
                                Vector3.one * 1f));
                        }
                    }
                }
            }
        }

        camera.transform.position = bounds.center + (outwardDirection.normalized * bounds.extents.magnitude);
        camera.transform.LookAt(bounds.center, Vector3.up);
        FitOrthoCameraToBounds(camera, bounds, margin: 1.06f);
        if (!TryGetBoundsScreenRect(camera, bounds, out Vector2 screenMin, out Vector2 screenMax))
            throw new Exception("Could not project elev bounds to screen");

        RenderTexture rt = camera.targetTexture;
        int safetyCounter = 64;
        while (--safetyCounter > 0
               && (screenMin.x < 1f || screenMin.y < 1f
                   || screenMax.x > rt.width - 2f || screenMax.y > rt.height - 2f))
        {
            camera.orthographicSize *= 1.08f;
            if (!TryGetBoundsScreenRect(camera, bounds, out screenMin, out screenMax))
                break;
        }

        imageWidth = Mathf.Max(1, Mathf.CeilToInt(screenMax.x - screenMin.x));
        imageHeight = Mathf.Max(1, Mathf.CeilToInt(screenMax.y - screenMin.y));

        ElevationCutsheetPass.SuppressNonCutsheetTexts();
        Canvas.ForceUpdateCanvases();
        InGameLight.ToggleLights(false);
        var camLight = camera.GetComponentInChildren<Light>(true);
        if (camLight != null)
            camLight.gameObject.SetActive(true);

        ElevationOutletCaptureDiagnostics.LogBeforeRender(_assemblySelectables, camera);
        RenderElevationCamera(camera, bounds);
        camera.enabled = false;

        if (camLight != null)
            camLight.gameObject.SetActive(false);
        InGameLight.ToggleLights(true);

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(imageWidth, imageHeight, TextureFormat.RGBA32, false);
        float minX = screenMin.x;
        float minY = screenMin.y;
        tex.ReadPixels(new Rect(minX, minY, imageWidth, imageHeight), 0, 0);
        RenderTexture.active = null;

        byte[] pngData = tex.EncodeToPNG();
        string filenameImage = Path.Combine(
            Application.persistentDataPath,
            $"ExportedArmAssemblyElevationShot{fileIndex}{(invertDirection ? "_back" : "_front")}.png");
        File.WriteAllBytes(filenameImage, pngData);

        camera.transform.position = cameraOriginalPos;

        return filenameImage;
    }

    /// <summary>
    /// Elev <see cref="Camera.Render"/> with depth-safe RT/clip settings only.
    /// Does not change look direction or live geometry.
    /// Detaches the elev cam from non-uniform assembly parents for the render —
    /// scale_audit showed Camera_RenderTexture_ElevationView lossy z=0.7 under
    /// ceiling covers, which skews ortho depth vs the unscaled game camera.
    /// Single-pass only — a URP second pass was clearing the color RT and wiping
    /// equipment. Dims stay on top via <see cref="ElevOverlayDrawOrder"/> (ZTest Always).
    /// </summary>
    private static void RenderElevationCamera(Camera camera, Bounds framedBounds)
    {
        if (camera == null)
            return;

        Transform t = camera.transform;
        Transform prevParent = t.parent;
        Vector3 prevLocalScale = t.localScale;
        Vector3 worldPos = t.position;
        Quaternion worldRot = t.rotation;
        bool reparented = false;
        if ((t.lossyScale - Vector3.one).sqrMagnitude > 1e-4f)
        {
            t.SetParent(null, worldPositionStays: true);
            t.SetPositionAndRotation(worldPos, worldRot);
            t.localScale = Vector3.one;
            reparented = true;
        }

        RenderTexture rt = camera.targetTexture;
        int prevAa = rt != null ? rt.antiAliasing : 0;
        if (rt != null && rt.antiAliasing > 2)
            rt.antiAliasing = 2;

        float prevNear = camera.nearClipPlane;
        float prevFar = camera.farClipPlane;
        float camDist = Vector3.Distance(t.position, framedBounds.center);
        float radius = Mathf.Max(0.5f, framedBounds.extents.magnitude);
        camera.nearClipPlane = Mathf.Max(0.01f, camDist - radius - 0.5f);
        camera.farClipPlane = camDist + radius + 0.5f;

        ElevOverlayDrawOrder.BeginCanvasForElevation(
            camera, out Canvas elevCanvas, out Camera prevWorldCam,
            out bool prevOverride, out int prevOrder);

        try
        {
            camera.Render();
        }
        finally
        {
            ElevOverlayDrawOrder.EndCanvasForElevation(
                elevCanvas, prevWorldCam, prevOverride, prevOrder);
        }

        camera.nearClipPlane = prevNear;
        camera.farClipPlane = prevFar;
        if (rt != null)
            rt.antiAliasing = prevAa;

        if (reparented)
        {
            t.SetParent(prevParent, worldPositionStays: true);
            t.localScale = prevLocalScale;
            t.SetPositionAndRotation(worldPos, worldRot);
        }
    }

    // Computes the expanded bounds for the current orientation (front/back) including measurement overlays without rendering
    private Bounds ComputeExpandedBoundsForOrientation(Camera camera, Bounds bounds, bool invertDirection)
    {
        camera.enabled = true;
        camera.orthographic = true;

        Vector3 cameraOriginalPos = camera.transform.position;
        Vector3 outwardDirection = GetElevationViewOutwardDirection(
            bounds, cameraOriginalPos - transform.position, invertDirection);

        camera.transform.position = bounds.center + (outwardDirection.normalized * bounds.extents.magnitude);
        camera.transform.LookAt(bounds.center, Vector3.up);
        camera.orthographicSize = bounds.extents.y;

        SuppressHighlightsForCapture(_assemblySelectables);
        ElevationCutsheetPass.Apply(_assemblySelectables, camera);
        // Expand bounds from active cutsheet measurers only.
        if (_assemblySelectables != null)
        {
            foreach (var item in _assemblySelectables)
            {
                if (item?.Measurables == null) continue;
                foreach (var measurable in item.Measurables)
                {
                    if (measurable?.Measurements == null) continue;
                    foreach (var measurement in measurable.Measurements)
                    {
                        if (measurement?.Measurer == null || !measurement.Measurer.gameObject.activeSelf)
                            continue;
                        if (measurement.Measurer.Renderer != null)
                            bounds.Encapsulate(measurement.Measurer.Renderer.bounds);
                        if (measurement.Measurer.MeasurementText != null
                            && measurement.Measurer.MeasurementText.gameObject.activeSelf)
                        {
                            bounds.Encapsulate(new Bounds(
                                measurement.Measurer.MeasurementText.transform.position,
                                Vector3.one * 1f));
                        }
                    }
                }
            }
        }

        // Re-aim with expanded bounds
        camera.transform.position = bounds.center + (outwardDirection.normalized * bounds.extents.magnitude);
        camera.transform.LookAt(bounds.center, Vector3.up);
        camera.orthographicSize = bounds.extents.y;

        // Do not render. Restore camera position
        camera.transform.position = cameraOriginalPos;

        return bounds;
    }

    /// <summary>
    /// Widen bounds to include active cutsheet dim lines / labels (after Apply).
    /// Grow the frame — do not shove dims back into a tight crop.
    /// Does not change the room-locked Y span by itself — caller re-locks Y after.
    /// </summary>
    private Bounds ExpandElevationBoundsForCutsheetOverlays(Bounds bounds)
    {
        const float labelPad = 0.55f;

        void EncapsulateMeasurer(Measurer measurer)
        {
            if (measurer == null || !measurer.gameObject.activeSelf)
                return;
            if (!measurer.ShouldDrawInElevationPhoto())
                return;
            var measurement = measurer.Measurement;
            if (measurement == null)
                return;

            bounds.Encapsulate(measurement.Origin);
            bounds.Encapsulate(measurement.HitPoint);
            if (measurer.ElevationLeadersValid)
            {
                bounds.Encapsulate(measurer.ElevationLeaderFeatureA);
                bounds.Encapsulate(measurer.ElevationLeaderFeatureB);
            }

            var text = measurer.MeasurementText;
            if (text != null && text.gameObject.activeSelf)
            {
                bounds.Encapsulate(new Bounds(
                    text.transform.position,
                    Vector3.one * labelPad * 2f));
                if (text.Text != null)
                {
                    text.Text.ForceMeshUpdate();
                    Bounds gb = text.Text.textBounds;
                    var rt = text.Text.rectTransform;
                    Vector3 e = gb.extents;
                    Vector3 c = gb.center;
                    bounds.Encapsulate(rt.TransformPoint(c + new Vector3(-e.x, -e.y, 0f)));
                    bounds.Encapsulate(rt.TransformPoint(c + new Vector3(-e.x,  e.y, 0f)));
                    bounds.Encapsulate(rt.TransformPoint(c + new Vector3( e.x, -e.y, 0f)));
                    bounds.Encapsulate(rt.TransformPoint(c + new Vector3( e.x,  e.y, 0f)));
                }
            }
        }

        // All live elev measurers (not only ones still linked on assembly lists).
        foreach (var measurer in UnityEngine.Object.FindObjectsByType<Measurer>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            EncapsulateMeasurer(measurer);

        return bounds;
    }

    /// <summary>
    /// Ortho size from camera-local extents of all 8 AABB corners (with aspect).
    /// </summary>
    static void FitOrthoCameraToBounds(Camera camera, Bounds bounds, float margin = 1.05f)
    {
        if (camera == null)
            return;
        float maxRight = 0.01f;
        float maxUp = 0.01f;
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        for (int ix = -1; ix <= 1; ix += 2)
        for (int iy = -1; iy <= 1; iy += 2)
        for (int iz = -1; iz <= 1; iz += 2)
        {
            Vector3 world = c + new Vector3(e.x * ix, e.y * iy, e.z * iz);
            Vector3 local = camera.transform.InverseTransformPoint(world);
            maxRight = Mathf.Max(maxRight, Mathf.Abs(local.x));
            maxUp = Mathf.Max(maxUp, Mathf.Abs(local.y));
        }
        float aspect = Mathf.Max(0.01f, camera.aspect);
        float size = Mathf.Max(maxUp, maxRight / aspect) * Mathf.Max(1f, margin);
        camera.orthographicSize = Mathf.Max(0.01f, size);
    }

    /// <summary>
    /// Screen-space AABB of a world bounds — must use all 8 corners, not min/max only.
    /// </summary>
    static bool TryGetBoundsScreenRect(
        Camera camera, Bounds bounds, out Vector2 screenMin, out Vector2 screenMax)
    {
        screenMin = new Vector2(float.MaxValue, float.MaxValue);
        screenMax = new Vector2(float.MinValue, float.MinValue);
        if (camera == null)
            return false;

        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        bool any = false;
        for (int ix = -1; ix <= 1; ix += 2)
        for (int iy = -1; iy <= 1; iy += 2)
        for (int iz = -1; iz <= 1; iz += 2)
        {
            Vector3 world = c + new Vector3(e.x * ix, e.y * iy, e.z * iz);
            Vector3 sp = camera.WorldToScreenPoint(world);
            if (sp.z < 0f)
                continue;
            any = true;
            screenMin.x = Mathf.Min(screenMin.x, sp.x);
            screenMin.y = Mathf.Min(screenMin.y, sp.y);
            screenMax.x = Mathf.Max(screenMax.x, sp.x);
            screenMax.y = Mathf.Max(screenMax.y, sp.y);
        }
        return any && screenMax.x > screenMin.x && screenMax.y > screenMin.y;
    }

    /// <summary>
    /// Elevation PDFs stamp a ground graphic under the photos and list ceiling height —
    /// those are the vertical scale. Lock photo Y to room floor top → ceiling underside
    /// so boom-only and boom+light share the same floor-to-ceiling framing. Keep X/Z
    /// from the assembly (and dim overlays) for horizontal fit.
    /// </summary>
    private static Bounds LockElevationVerticalToRoom(Bounds bounds)
    {
        float floorY = 0f;
        var floor = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor);
        if (floor != null)
            floorY = floor.transform.position.y + (floor.transform.localScale.y * 0.5f);

        float ceilingY = floorY + 3f;
        var ceiling = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling);
        if (ceiling != null)
        {
            float underside = ceiling.transform.position.y - (ceiling.transform.localScale.y * 0.5f);
            if (underside > floorY + 0.1f)
                ceilingY = underside;
            else if (ceiling.Height > 0.1f)
                ceilingY = floorY + ceiling.Height;
        }

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        min.y = floorY;
        max.y = ceilingY;
        if (max.y < min.y + 0.01f)
            max.y = min.y + 0.01f;
        bounds.SetMinMax(min, max);
        return bounds;
    }

    // Captures using a fixed bounds so both front/back share identical camera framing
    private string CaptureElevationWithFixedBounds(
        Camera camera,
        Bounds fixedBounds,
        out int imageWidth,
        out int imageHeight,
        int fileIndex,
        bool invertDirection = false)
    {
        camera.enabled = true;
        camera.orthographic = true;

        Vector3 cameraOriginalPos = camera.transform.position;
        // Aim from the posed assembly footprint (not union alone) so dual-arm / long-Z
        // stacks get a true side elevation instead of an end-on stub view.
        Bounds poseBounds = GetAssemblyBounds();
        Vector3 outwardDirection = GetElevationViewOutwardDirection(
            poseBounds, cameraOriginalPos - transform.position, invertDirection);

        // Cutsheet overlays only — never activate Walls/Ceiling / interactive imperial dims.
        // Aim first so layout uses a sensible camera, then widen XZ for labels (Y stays room-locked).
        camera.transform.position = fixedBounds.center + (outwardDirection.normalized * fixedBounds.extents.magnitude);
        camera.transform.LookAt(fixedBounds.center, Vector3.up);
        camera.orthographicSize = Mathf.Max(0.01f, fixedBounds.extents.y);

        SuppressHighlightsForCapture(_assemblySelectables);
        ElevationCutsheetPass.Apply(_assemblySelectables, camera);
        fixedBounds = ExpandElevationBoundsForCutsheetOverlays(fixedBounds);
        {
            Vector3 bMin = fixedBounds.min;
            Vector3 bMax = fixedBounds.max;
            const float sidePad = 0.35f;
            bMin.x -= sidePad;
            bMin.z -= sidePad;
            bMax.x += sidePad;
            bMax.z += sidePad;
            fixedBounds.SetMinMax(bMin, bMax);
        }
        fixedBounds = LockElevationVerticalToRoom(fixedBounds);

        camera.transform.position = fixedBounds.center + (outwardDirection.normalized * fixedBounds.extents.magnitude);
        camera.transform.LookAt(fixedBounds.center, Vector3.up);

        // Fit using ALL 8 AABB corners in camera space — WorldToScreen(min)/max alone
        // misses labels sticking out sideways (cropping callouts off the PDF).
        FitOrthoCameraToBounds(camera, fixedBounds, margin: 1.06f);
        if (!TryGetBoundsScreenRect(camera, fixedBounds, out Vector2 screenMin, out Vector2 screenMax))
            throw new Exception("Could not project elev bounds to screen (fixed)");

        RenderTexture rt = camera.targetTexture;
        // Keep crop inside the RT; grow ortho again if any corner still clips.
        int safetyCounter = 64;
        while (--safetyCounter > 0
               && (screenMin.x < 1f || screenMin.y < 1f
                   || screenMax.x > rt.width - 2f || screenMax.y > rt.height - 2f))
        {
            camera.orthographicSize *= 1.08f;
            if (!TryGetBoundsScreenRect(camera, fixedBounds, out screenMin, out screenMax))
                break;
        }

        imageWidth = Mathf.Max(1, Mathf.CeilToInt(screenMax.x - screenMin.x));
        imageHeight = Mathf.Max(1, Mathf.CeilToInt(screenMax.y - screenMin.y));
        Debug.Log(
            $"[ElevDim] FRAME ortho={camera.orthographicSize:F3} " +
            $"crop={imageWidth}x{imageHeight} screen=({screenMin.x:F0},{screenMin.y:F0})-" +
            $"({screenMax.x:F0},{screenMax.y:F0}) rt={rt.width}x{rt.height} " +
            $"boundsXZ=({fixedBounds.size.x:F2},{fixedBounds.size.z:F2})");

        ElevationCutsheetPass.SuppressNonCutsheetTexts();
        Canvas.ForceUpdateCanvases();
        InGameLight.ToggleLights(false);
        var camLight = camera.GetComponentInChildren<Light>(true);
        if (camLight != null)
            camLight.gameObject.SetActive(true);

        ElevationOutletCaptureDiagnostics.LogBeforeRender(_assemblySelectables, camera);
        RenderElevationCamera(camera, fixedBounds);
        camera.enabled = false;

        if (camLight != null)
            camLight.gameObject.SetActive(false);
        InGameLight.ToggleLights(true);

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(imageWidth, imageHeight, TextureFormat.RGBA32, false);
        float minX = screenMin.x;
        float minY = screenMin.y;
        tex.ReadPixels(new Rect(minX, minY, imageWidth, imageHeight), 0, 0);
        RenderTexture.active = null;

        // save — JPEG for proposal preview (much faster), PNG for real exports
        bool fastPreview = ProposalPDFGenerator.FastPreviewCapture;
        byte[] encoded = fastPreview ? tex.EncodeToJPG(72) : tex.EncodeToPNG();
        string ext = fastPreview ? ".jpg" : ".png";
        string filenameImage = Path.Combine(
            Application.persistentDataPath,
            $"ExportedArmAssemblyElevationShot{fileIndex}{(invertDirection ? "_back" : "_front")}{ext}");
        File.WriteAllBytes(filenameImage, encoded);

        UnityEngine.Object.Destroy(tex);

        camera.transform.position = cameraOriginalPos;
        return filenameImage;
    }

    public void RestoreArmAssemblyRotations()
    {
        if (TryGetArmAssemblyRoot(out GameObject rootObj))
        {
            if (rootObj == gameObject)
            {
                foreach (var kv in _originalRotations)
                {
                    if (kv.Key != null)
                        kv.Key.transform.localRotation = kv.Value;
                }
            }
            else
            {
                rootObj.GetComponent<Selectable>().RestoreArmAssemblyRotations();
                return;
            }
        }
    }

    public void SetAssemblyToDefaultRotations()
    {
        if (TryGetArmAssemblyRoot(out GameObject rootObj))
        {
            if (rootObj == gameObject)
            {
                // includeInactive: dual-selectable length owners (.001) can be inactive
                // while their mesh still renders under a related wrapper.
                _assemblySelectables = GetComponentsInChildren<Selectable>(true).ToList();
                ExpandAssemblySelectablesWithRelated();
                // Dual-selectable boom/light prefabs often leave Measurables [] while the
                // ToOrigin Measurable lives on a related child — link before elevation dims.
                _assemblySelectables.ForEach(s =>
                {
                    if (s == null) return;
                    s.EnsureCurrentScaleLevelFromCatalog();
                    s.EnsureMeasurablesLinked();
                    if (s.Measurables == null) return;
                    foreach (var m in s.Measurables)
                        m?.EnsureInitializedForElevation();
                });
                Debug.Log(
                    $"[ElevDim] Assembly roster root={name} count={_assemblySelectables.Count} " +
                    $"withMeas={_assemblySelectables.Count(s => s != null && s.Measurables != null && s.Measurables.Count > 0)} " +
                    $"names={string.Join(",", _assemblySelectables.Where(s => s != null).Select(s => s.name))}",
                    this);
                //_assemblySelectables.Add(this);
                _originalRotations.Clear();
                Array.ForEach(_assemblySelectables.OrderBy(x => x.GetParentCount()).ToArray(), item =>
                {
                    if (item.AlignForElevationPhoto || item.ChangeHeightForElevationPhoto || item.ZAlwaysFacesGroundElevationOnly)
                    {
                        _originalRotations[item] = item.transform.localRotation;
                        item.transform.localRotation = item._originalLocalRotation;
                    }
                });
            }
            else
            {
                rootObj.GetComponent<Selectable>().SetAssemblyToDefaultRotations();
                return;
            }
        }
    }

    /// <summary>
    /// RelatedSelectables may hold the ScaleLevels / Measurable owner when it is not
    /// already under this root's transform children list.
    /// </summary>
    private void ExpandAssemblySelectablesWithRelated()
    {
        if (_assemblySelectables == null)
            return;

        var seen = new HashSet<Selectable>(_assemblySelectables.Where(s => s != null));
        var extras = new List<Selectable>();
        foreach (var s in _assemblySelectables)
        {
            if (s?.RelatedSelectables == null)
                continue;
            foreach (var rel in s.RelatedSelectables)
            {
                if (rel == null || !seen.Add(rel))
                    continue;
                extras.Add(rel);
            }
        }
        if (extras.Count > 0)
            _assemblySelectables.AddRange(extras);
    }
    /// <summary>
    /// Boom/light prefabs often ship a ToOrigin Measurable while the ScaleLevels
    /// Selectable still has Measurables []. Recover self refs; pull at most ONE
    /// ToOrigin from the dual-select group so cutsheets never stack duplicate lengths.
    /// </summary>
    public void EnsureMeasurablesLinked()
    {
        if (Measurables == null)
            Measurables = new List<Measurable>();

        // Drop null slots left by missing nested prefab refs.
        Measurables.RemoveAll(m => m == null);

        // FBX typo BoomSegement_* breaks dual-select stem matching vs BoomSegment_*(Clone).
        if (name != null && name.IndexOf("Segement", StringComparison.Ordinal) >= 0)
            gameObject.name = name.Replace("Segement", "Segment");

        foreach (var m in GetComponents<Measurable>())
            TryAddCutsheetMeasurable(m);

        bool isLengthOwner = ScaleLevels != null && ScaleLevels.Count > 0;
        if (!isLengthOwner)
            return;

        // Service-head ScaleLevels are row tiers, not catalog length.
        // Must be on THIS selectable — GetComponentInChildren would hit a head hanging
        // under BoomSegment_3 and skip installing the neck's ToOrigin.
        if (GetComponent<BoomHeadScaleHandler>() != null)
            return;

        // Length owners: unitize non-selectable FBX parent shells (load/init only, never export).
        EnsureLengthOwnerParentShellNormalized();

        bool alreadyHasToOrigin = Measurables.Any(m =>
            m != null && MeasurableHasType(m, MeasurementType.ToArmAssemblyOrigin));
        if (alreadyHasToOrigin)
        {
            foreach (var m in Measurables)
            {
                if (m != null && MeasurableHasType(m, MeasurementType.ToArmAssemblyOrigin))
                    m.EnsureConfiguredAsCatalogLength();
            }
            return;
        }

        // Only when this length owner has no ToOrigin yet — claim one from dual-select
        // related group or unowned children. Never steal a descendant Size-owner's ToOrigin
        // (that dropped the child's catalog length on cutsheets via measurableClaimed).
        Measurable claim = null;
        foreach (var m in GetComponentsInChildren<Measurable>(true))
        {
            if (m == null || !MeasurableHasType(m, MeasurementType.ToArmAssemblyOrigin))
                continue;
            if (IsOwnedByOtherLengthSelectable(m))
                continue;
            if (IsToOriginClaimedElsewhere(m))
                continue;
            claim = m;
            break;
        }

        if (claim == null && RelatedSelectables != null)
        {
            foreach (var rel in RelatedSelectables)
            {
                if (rel == null || rel == this)
                    continue;
                foreach (var m in rel.GetComponentsInChildren<Measurable>(true))
                {
                    if (m == null || !MeasurableHasType(m, MeasurementType.ToArmAssemblyOrigin))
                        continue;
                    if (IsOwnedByOtherLengthSelectable(m))
                        continue;
                    if (IsToOriginClaimedElsewhere(m))
                        continue;
                    claim = m;
                    break;
                }
                if (claim != null)
                    break;
            }
        }

        if (claim != null && !Measurables.Contains(claim))
            Measurables.Add(claim);

        // Invariant: every length ScaleLevels owner has a ToOrigin Measurable.
        // Prefab/bundle omissions (BoomSegment_3 shipped with Measurables []) are fixed
        // here once — not re-synthesized per cutsheet pass.
        if (!Measurables.Any(m => m != null && MeasurableHasType(m, MeasurementType.ToArmAssemblyOrigin)))
        {
            var installed = GetComponent<Measurable>();
            if (installed == null)
                installed = gameObject.AddComponent<Measurable>();
            installed.EnsureConfiguredAsCatalogLength();
            TryAddCutsheetMeasurable(installed);
            Debug.Log(
                $"[ElevDim] installed missing ToOrigin on Size owner={name} " +
                $"mm={Mathf.RoundToInt(ElevationLengthFormat.ResolveOwnSizeMeters(this) * 1000f)} " +
                $"(prefab/bundle had none)",
                this);
        }

        if (Selectable.IsInElevationPhotoMode
            && !Measurables.Any(m => m != null && MeasurableHasType(m, MeasurementType.ToArmAssemblyOrigin)))
        {
            Debug.LogWarning(
                $"[ElevDim] length owner has NO ToOrigin measurable after link " +
                $"name={name} levels={ScaleLevels.Count} measCount={Measurables.Count} " +
                $"bind={{ {ElevationLengthFormat.DiagnoseSizeBinding(this)} }}",
                this);
        }
    }

    static bool MeasurableHasType(Measurable m, MeasurementType type)
    {
        if (m == null)
            return false;
        if (m.MeasurementTypes != null && m.MeasurementTypes.Contains(type))
            return true;
        if (m.Measurements != null && m.Measurements.Any(x => x != null && x.MeasurementType == type))
            return true;
        return false;
    }

    bool IsToOriginClaimedElsewhere(Measurable m)
    {
        if (RelatedSelectables == null)
            return false;
        foreach (var rel in RelatedSelectables)
        {
            if (rel == null || rel == this)
                continue;
            // Only block when the other twin actually owns a catalog Size — otherwise the
            // Size half of a dual-select pair can never recover the shared ToOrigin.
            if (rel.Measurables != null && rel.Measurables.Contains(m)
                && ElevationLengthFormat.ResolveOwnSizeMeters(rel) > 0f)
                return true;
        }
        return false;
    }

    bool IsDualSelectTwin(Selectable other)
    {
        if (other == null || RelatedSelectables == null)
            return false;
        return RelatedSelectables.Contains(other);
    }

    /// <summary>
    /// True when <paramref name="m"/> sits under a different Selectable that has its own
    /// ScaleLevels — that descendant owns the catalog length, not this parent.
    /// Dual-select twins are not "other" owners (Measurable often lives on the non-Size half).
    /// </summary>
    bool IsOwnedByOtherLengthSelectable(Measurable m)
    {
        if (m == null)
            return false;
        var nearest = m.GetComponentInParent<Selectable>(true);
        if (nearest == null || nearest == this)
            return false;
        if (IsDualSelectTwin(nearest))
            return false;
        return nearest.ScaleLevels != null && nearest.ScaleLevels.Count > 0;
    }

    void TryAddCutsheetMeasurable(Measurable m)
    {
        if (m == null || Measurables.Contains(m))
            return;
        if (!MeasurableHasType(m, MeasurementType.ToArmAssemblyOrigin)
            && !MeasurableHasType(m, MeasurementType.Floor))
            return;
        Measurables.Add(m);
    }

    /// <summary>
    /// Place/init: vertical length owners only — unitize non-selectable FBX parent shell
    /// so Size→world length is exact. Never touches horizontal arms. Skipped during
    /// elevation / config load (load uses <see cref="FixLengthOwnerParentShellAfterLoad"/>).
    /// </summary>
    public void EnsureLengthOwnerParentShellNormalized()
    {
        if (IsInElevationPhotoMode || ConfigurationManager.IsLoading)
            return;
        NormalizeVerticalLengthOwnerParentShell(rebakeAndApply: true);
    }

    /// <summary>Legacy name — same as <see cref="EnsureLengthOwnerParentShellNormalized"/>.</summary>
    public void EnsureDropTubeLengthMeshOwnership() => EnsureLengthOwnerParentShellNormalized();

    /// <summary>
    /// After config restore: vertical length owners under non-selectable FBX shells
    /// (BoomDropTube z=0.8, BoomSegment_3 parent 1.25) must unitize the shell and
    /// re-bake ScaleZ. Horizontal arms are never mutated here — a prior AABB.y check
    /// wrongly re-scaled BoomSegment_1-2 / MCP and broke the live scene.
    /// </summary>
    public void FixLengthOwnerParentShellAfterLoad()
    {
        NormalizeVerticalLengthOwnerParentShell(rebakeAndApply: true);
    }

    /// <summary>Legacy name — same as <see cref="FixLengthOwnerParentShellAfterLoad"/>.</summary>
    public void FixDropTubeShellAfterLoad() => FixLengthOwnerParentShellAfterLoad();

    /// <summary>
    /// True for catalog parts whose length axis is world-vertical (drop tube / neck).
    /// Horizontal boom arms must not enter parent-shell repair.
    /// </summary>
    bool IsVerticalCatalogLengthOwner()
    {
        if (Measurable.IsVerticalHangLengthName(name))
            return true;
        if (name != null && name.IndexOf("BoomSegment_3", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (UsesScaleLevelsAsRowConfig())
            return false;
        Vector3 axis = transform.TransformDirection(Vector3.forward);
        if (axis.sqrMagnitude < 1e-8f)
            return false;
        return Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) >= 0.75f;
    }

    /// <summary>
    /// Vertical length owners only: non-selectable parent shell → identity; drop-tube dual
    /// mesh hides shell renderer; then BakeScaleZ + SetScaleLevel when shell/mesh disagree
    /// with Size. Returns true when shell scale changed.
    /// </summary>
    bool NormalizeVerticalLengthOwnerParentShell(bool rebakeAndApply)
    {
        if (ScaleLevels == null || ScaleLevels.Count == 0)
            return false;
        if (UsesScaleLevelsAsRowConfig())
            return false;
        if (!IsVerticalCatalogLengthOwner())
            return false;

        Transform shell = transform.parent;
        if (shell == null || shell.GetComponent<Selectable>() != null)
            return false;

        // Drop-tube FBX = shell mesh (no Size) + child Size owner. Prefab usually disables
        // the shell; re-assert so a restored enable does not draw a second column.
        if (Measurable.IsDropTubeName(name) || Measurable.IsVerticalHangLengthName(name))
        {
            foreach (var r in shell.GetComponents<Renderer>())
            {
                if (r != null && r.enabled)
                    r.enabled = false;
            }
        }

        Vector3 ps = shell.localScale;
        bool shellWasWrong =
            Mathf.Abs(ps.x - 1f) > 0.01f
            || Mathf.Abs(ps.y - 1f) > 0.01f
            || Mathf.Abs(ps.z - 1f) > 0.01f;
        if (shellWasWrong)
        {
            shell.localScale = Vector3.one;
            Physics.SyncTransforms();
        }

        if (!rebakeAndApply)
            return shellWasWrong;

        float sizeM = ElevationLengthFormat.ResolveOwnSizeMeters(this);
        bool meshWrong = false;
        if (sizeM > 0.05f
            && Measurable.TryGetStrictOwnRendererBounds(this, out Bounds rb)
            && rb.size.y > 0.02f)
        {
            meshWrong = Mathf.Abs(rb.size.y - sizeM) > 0.02f;
        }

        if (!shellWasWrong && !meshWrong)
            return false;

        BakeScaleZFromAuthoredLength();
        var level = CurrentScaleLevel
            ?? ScaleLevels.FirstOrDefault(s => s != null && s.Selected && s.Size > 0f)
            ?? ScaleLevels.FirstOrDefault(s => s != null && s.Size > 0f);
        if (level == null || level.ScaleZ <= 0.0001f)
            return shellWasWrong;

        SetScaleLevel(level, setSelected: false, fireEvent: false);
        EnsureAttachChainScaleCompensation(reapplyMeshIsolation: false);
        Debug.Log(
            $"[ElevDim] vertical length-owner shell repaired name={name} " +
            $"shellWasWrong={shellWasWrong} meshWrong={meshWrong} " +
            $"shellWas={ps} sizeMm={Mathf.RoundToInt(sizeM * 1000f)} scaleZ={level.ScaleZ:F4}",
            this);
        return shellWasWrong || meshWrong;
    }

   public void ToggleMeasurableActiveStatesWhilePlacing(bool enable)
    {
      
        //Debug.LogError("ToggleMeasurableActiveStatesWhilePlacing");
        if (Measurables != null && Measurables.Count > 0)
        {
            //  Debug.LogError("Measurables.Count > 0");
            Measurables.ForEach(measurable =>
            {
                if (measurable.ArmAssemblyActiveInElevationPhotoMode)
                {
                    measurable.ArmAssemblyActiveInElevationPhotoMode = enable;
                }
                _measurableActiveStates[measurable] = enable;
                measurable.SetActive(enable);
            });
        }
    }

    /// <summary>
    /// Elevation capture is synchronous — MeasurementText.Update never runs before Render.
    /// Hard-hide any label that is not a cutsheet whitelist dim.
    /// </summary>
    private static void HideNonWhitelistedElevationMeasurementTexts()
    {
        ElevationCutsheetPass.SuppressNonCutsheetTexts();
    }

    private void ToggleMeasurableActiveStates(bool active)
    {
        // Init / interactive placement also calls this. Only the elevation branch may
        // suppress the shared canvas — otherwise every InitializeAfterStart would wipe
        // all MeasurementTexts in the scene.
        if (!IsInElevationPhotoMode)
        {
            if (Measurables == null || Measurables.Count == 0)
                return;
            foreach (var measurable in Measurables)
            {
                if (measurable == null)
                    continue;
                measurable.SetActive(active);
            }
            return;
        }

        if (active)
        {
            // Elevation: suppress everything. Cutsheet dims are built explicitly in
            // ElevationCutsheetPass.Apply during capture — never SetActive(true) on
            // measurables (that enables Walls and flashes imperial canvas labels).
            ElevationCutsheetPass.SuppressAllOverlays();

            if (_assemblySelectables == null)
                return;

            _assemblySelectables.ForEach(item =>
            {
                if (item == null) return;
                item.EnsureCurrentScaleLevelFromCatalog();
                item.EnsureMeasurablesLinked();
                if (item.Measurables == null || item.Measurables.Count == 0)
                    return;

                item.Measurables.ForEach(measurable =>
                {
                    if (measurable == null) return;
                    measurable.EnsureInitializedForElevation();
                    _measurableActiveStates[measurable] = measurable.IsActive;
                    // Soft flag only — do not enable wall measurers / fire ActiveMeasurablesChanged.
                    measurable.ArmAssemblyActiveInElevationPhotoMode = true;
                });
            });
        }
        else
        {
            ElevationCutsheetPass.SuppressAllOverlays();
            _measurableActiveStates.Keys.ToList().ForEach(item =>
            {
                if (item == null) return;
                item.ArmAssemblyActiveInElevationPhotoMode = false;
                item.ClearCutsheetElevationState();
                // Do not UpdateMeasurements here — that re-drew cutsheet dims into the live scene.
                item.SetActive(false);
            });
            _measurableActiveStates.Clear();
            ElevationCutsheetPass.EndCaptureCleanup();
        }
    }

    private Bounds GetAssemblyBounds()
    {
        if (!IsAssemblyRoot)
        {
            if (TryGetArmAssemblyRoot(out GameObject rootObj))
            {
                return rootObj.GetComponent<Selectable>().GetAssemblyBounds();
            }
            else
            {
                throw new Exception("Could not get assembly root");
            }
        }

        List<MeshRenderer> renderers = GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToList();

        if (renderers.Count == 0)
        {
            throw new Exception("Arm assembly has no renderers!");
        }

        Bounds bounds = renderers[0].bounds;
        renderers.ForEach(renderer =>
        {
            bounds.Encapsulate(renderer.bounds);
        });

        return bounds;
    }

    private int GetParentCount()
    {
        Transform parent = transform.parent;
        int count = 0;
        while (parent != transform.root && parent != null)
        {
            parent = parent.parent;
            count++;
        }

        return count;
    }

    #endregion

    #region Raycasting

    public async void StartRaycastPlacementMode()
    {
        if (ParentAttachmentPoint != null)
            return;

        DeselectAll();
        _highlightEffect.highlighted = true;

        if (transform.CompareTag("Wall"))
        {
            RaycastingColliders = GetComponentsInChildren<Collider>();
            foreach (Collider col in RaycastingColliders)
            {
                if (col is MeshCollider mc)
                {
                    mc.convex = true;
                }
                //col.isTrigger = true;
                col.enabled = false;
            }

        }

        if (CanPlaceAnywhere)
        {
            GetComponentInChildren<Collider>().enabled = false;
        }

        await Task.Yield();
        if (!Application.isPlaying) return;

        _isRaycastPlacementMode = true;
        Debug.Log($"Selectable: Raycast placement mode = true");
    }

    private async void UpdateRaycastPlacementMode()
    {
        if (!_isRaycastPlacementMode || _hasBeenPlaced)
            return;
        //  Debug.LogError("CanPlaceAnywhere");
        _measurableActiveStates.Clear();
        ToggleMeasurableActiveStatesWhilePlacing(true);

        bool isCeilingCam = OperatingRoomCamera.LiveCamera
            .CameraType == OperatingRoomCameraType.OrthoCeiling;

        bool isOrbitalCam = OperatingRoomCamera.LiveCamera
            .CameraType == OperatingRoomCameraType.Orbital;

        if (WallRestrictions.Count > 0 &&
            WallRestrictions[0] == RoomBoundaryType.Ceiling &&
            (isCeilingCam || isOrbitalCam))
        {
            RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).Collider.enabled = true;
        }

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        _isRaycastingOnSelectable = false;

        if (CanPlaceAnywhere)
        {
            int maskSelectable = 1 << LayerMask.NameToLayer("Selectable");

            if (Physics.Raycast(ray, out RaycastHit hit,
                float.MaxValue, maskSelectable))
            {
                transform.position = hit.point;
                transform.LookAt(transform.position + hit.normal, Vector3.up);
                _isRaycastingOnSelectable = true;
                AttachedTo = null;
                Transform parent = hit.transform;

                while (AttachedTo == null)
                {
                    if (parent == null)
                    {
                        _isRaycastingOnSelectable = false;
                        break;
                    }

                    AttachedTo = parent.gameObject.GetComponent<Selectable>();
                    parent = parent.parent;
                }
            }
        }

        if (!_isRaycastingOnSelectable)
        {
            //int mask = 1 << LayerMask.NameToLayer("Wall");
            var hits = Physics.RaycastAll(ray, float.MaxValue);
            foreach (var hit in hits)
            {
                void SetPosition(RaycastHit hit)
                {
                    Vector3 destination = hit.point;
                    Vector3 normal = hit.normal;

                    if (WallRestrictions.Count > 0 &&
                        WallRestrictions[0] == RoomBoundaryType.Ceiling
                        && (isCeilingCam || isOrbitalCam))
                    {
                        destination += RoomBoundary.DefaultWallThickness * Vector3.down;
                        normal *= -1;
                    }

                    bool isDoor = SpecialTypes.Contains(SpecialSelectableType.Door);
                    if (isDoor)
                    {
                        // Door mesh + WallCutter CutArea are authored for the interior
                        // face of the wall, facing into the room. Colliders live on a
                        // child of RoomBoundary, so look up the parent wall.
                        RoomBoundary roomBoundary =
                            hit.collider.GetComponentInParent<RoomBoundary>();
                        Vector3 outward;
                        float halfThickness = RoomBoundary.DefaultWallThickness / 2f;

                        if (roomBoundary != null &&
                            roomBoundary.RoomBoundaryType >= RoomBoundaryType.WallSouth &&
                            roomBoundary.RoomBoundaryType <= RoomBoundaryType.WallWest)
                        {
                            outward = GetDoorWallOutwardNormal(roomBoundary.RoomBoundaryType);
                            // Same depth as the pre-flush formula: interior face of the wall.
                            destination = roomBoundary.transform.position - outward * halfThickness;
                            // Keep the click's lateral position on the wall plane.
                            if (Mathf.Abs(outward.x) > 0.5f)
                                destination.z = hit.point.z;
                            else
                                destination.x = hit.point.x;
                        }
                        else
                        {
                            // Interior-face hit.normal points into the room.
                            Vector3 inward = hit.normal.normalized;
                            if (Mathf.Abs(inward.y) > 0.5f)
                                inward = Vector3.back; // floor/ceiling hit — don't lay flat
                            outward = -inward;
                            destination = hit.point;
                        }

                        destination.y = 0f;
                        normal = -outward; // face into the room
                    }

                    if (UI_ToggleSnapping.SnappingEnabled)
                    {
                        float yMag = Mathf.Abs(hit.normal.y);
                        float xMag = Mathf.Abs(hit.normal.x);
                        float zMag = Mathf.Abs(hit.normal.z);

                        if (yMag > xMag && yMag > zMag)
                        {
                            destination.x = RoundToNearestHalfInch(destination.x);
                            destination.z = RoundToNearestHalfInch(destination.z);
                        }
                        else if (xMag > yMag && xMag > zMag)
                        {
                            // Doors stay floor-aligned and wall-depth locked.
                            if (!isDoor)
                                destination.y = RoundToNearestHalfInch(destination.y);
                            destination.z = RoundToNearestHalfInch(destination.z);
                        }
                        else if (zMag > xMag && zMag > yMag)
                        {
                            if (!isDoor)
                                destination.y = RoundToNearestHalfInch(destination.y);
                            destination.x = RoundToNearestHalfInch(destination.x);
                        }
                    }

                    transform.SetPositionAndRotation(destination, Quaternion.LookRotation(normal));
                    _virtualParent = hit.collider.transform;
                    OnRaycastPositionUpdated?.Invoke();
                }

                static Vector3 GetDoorWallOutwardNormal(RoomBoundaryType type)
                {
                    switch (type)
                    {
                        case RoomBoundaryType.WallNorth: return Vector3.forward;
                        case RoomBoundaryType.WallSouth: return Vector3.back;
                        case RoomBoundaryType.WallEast: return Vector3.right;
                        case RoomBoundaryType.WallWest: return Vector3.left;
                        default: return Vector3.forward;
                    }
                }

                if (_virtualParent == null)
                {
                    Vector3 direction = WallRestrictions.Count > 0 && WallRestrictions[0] == RoomBoundaryType.Ceiling ? Vector3.up :
                        WallRestrictions.Count > 0 && WallRestrictions[0] == RoomBoundaryType.Floor ? Vector3.down :
                        Vector3.right;

                    var ray2 = new Ray(Vector3.zero + Vector3.up, direction);

                    if (Physics.Raycast(ray2, out RaycastHit raycastHit2,
                        float.MaxValue, 1 << LayerMask.NameToLayer("Wall")))
                    {
                        SetPosition(raycastHit2);
                        break;
                    }
                }
                else if (WallRestrictions.Count > 0)
                {
                    if (hit.collider.CompareTag("Wall") &&
                        WallRestrictions.Any(x => (int)x > 1))
                    {
                        // Additional Wall
                        SetPosition(hit);
                        break;
                    }
                    else if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall"))
                    {
                        var wall = hit.collider.GetComponentInParent<RoomBoundary>();

                        if (wall == null)
                        {
                            wall = hit.collider.GetComponent<RoomBoundary>();
                        }

                        if (WallRestrictions.Contains(wall.RoomBoundaryType))
                        {
                            SetPosition(hit);
                            break;
                        }
                    }
                }
                else
                {
                    SetPosition(hit);
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            _highlightEffect.highlighted = false;
            _hasBeenPlaced = true;

            if (_isRaycastingOnSelectable)
            {
                transform.parent = AttachedTo.transform;
            }
            else
            {
                if (_virtualParent != null && TryGetComponent<KeepRelativePosition>(out var krp))
                {
                    krp.VirtualParentChanged(_virtualParent);
                    krp.SelectablePositionChanged();
                }
                else
                {
                    Debug.LogWarning("No virtual parent detected.");
                }

                if (WallRestrictions.Count > 0 &&
                    WallRestrictions[0] == RoomBoundaryType.Ceiling &&
                    (isCeilingCam || isOrbitalCam))
                {
                    RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).Collider.enabled = false;
                }
            }

            if (transform.CompareTag("Wall"))
            {
                foreach (Collider col in RaycastingColliders)
                {
                    if (col is MeshCollider mc)
                    {
                        mc.convex = false;
                    }
                    //col.isTrigger = false;
                    col.enabled = true;
                }
            }

            PlacementLoadOptimizer.FinalizeInstanceColliders(gameObject);
            PlacementLoadOptimizer.RestoreInstanceCollidersAfterLoad(gameObject);

            await Task.Yield();
            Debug.Log("Anas => Object Placed");
            EventManager.OnCompareProximatryAlertWithOR_Table.Invoke(this.gameObject, 1, 1.5f);
            if (!Application.isPlaying) return;

            Debug.Log($"Selectable: Raycast placement mode = false");
            _isRaycastPlacementMode = false;
            OnPlaced?.Invoke();
            ToggleMeasurableActiveStatesWhilePlacing(false);

            if (FindObjectOfType<DuplicateRoom>(true))
            {
                DuplicateRoom room = FindObjectOfType<DuplicateRoom>(true);
                room.onObjectPlaced?.Invoke(this.gameObject);
            }


        }

    }

    #endregion

    private void InputHandler_KeyStateChanged(object sender, KeyStateChangedEventArgs e)
    {
        if (e.KeyCode == KeyCode.Escape && e.KeyState == KeyState.ReleasedThisFrame)
        {
            Deselect();

            if (_isRaycastPlacementMode)
            {
                Destroy(gameObject);
            }
        }
        else if (e.KeyCode == KeyCode.Delete && e.KeyState == KeyState.ReleasedThisFrame && IsSelected)
        {
            if (SceneManager.GetActiveScene().name == "ObjectEditor")
            {
                return;
            }

            if (FullScreenMenu.IsOpen)
                return;

            if (!IsDestructible)
                return;

            Deselect();

            Destroy(gameObject);
        }
    }

    public void OnPreprocessAssetBundle()
    {
#if UNITY_EDITOR
        bool needsDirty = false;

        // Set this and all children layer to "Selectable"
        var transforms = GetComponentsInChildren<Transform>(true);

        foreach (var transform in transforms)
        {
            if (transform.gameObject.layer != LayerMask.NameToLayer("Selectable"))
            {
                transform.gameObject.layer = LayerMask.NameToLayer("Selectable");
                needsDirty = true;
            }
        }

        AttachmentPoint[] attachPoints =
            GetComponentsInChildren<AttachmentPoint>(true);

        var relatedSelectables = GetComponentsInChildren<Selectable>().ToList();

        Array.ForEach(GetComponentsInChildren<Collider>(), collider =>
        {
            // Default layer
            if (collider.gameObject.layer == 0)
            {
                needsDirty = true;

                collider.gameObject.layer = LayerMask
                    .NameToLayer("Selectable");
            }
        });

        relatedSelectables.ForEach(x =>
        {
            if (relatedSelectables != x.RelatedSelectables)
            {
                x.RelatedSelectables = new List<Selectable>(relatedSelectables);
                needsDirty = true;
            }

            bool hasTrackedObject = x.TryGetComponent<TrackedObject>(out var trackedObj);
            bool hasRemoveTrackedObject = x.TryGetComponent<RemoveTrackedObject>(out _);

            if (hasTrackedObject && hasRemoveTrackedObject)
            {
                DestroyImmediate(trackedObj, true);
                needsDirty = true;
            }
            else if (!hasTrackedObject && !hasRemoveTrackedObject)
            {
                x.AddComponent<TrackedObject>();
                needsDirty = true;
            }
        });

        Array.ForEach(attachPoints, attPoint =>
        {
            bool exists = AttachmentPointDatas
                .Any(x => x.AttachmentPoint == attPoint);

            // make sure we're tracking it
            if (!exists)
            {
                string newGuid = Guid.NewGuid().ToString();
                AttachmentPointDatas.Add(
                    new AttachmentPointData
                    {
                        Guid = newGuid,
                        AttachmentPoint = attPoint
                    });

                attPoint.MetaData.Guid = newGuid;

                MetaData.AttachmentPointGuidMetaData.Add(
                    new AttachmentPointGuidMetaData
                    {
                        Guid = newGuid,
                        MetaData = attPoint.MetaData
                    });

                needsDirty = true;
            }
        });

        for (int i = AttachmentPointDatas.Count - 1; i >= 0; i--)
        {
            var attPoint = AttachmentPointDatas[i];

            if (!attachPoints.Contains(attPoint.AttachmentPoint))
            {
                needsDirty = true;

                var metaData = MetaData.AttachmentPointGuidMetaData.Where(x => x.MetaData.Guid == attPoint.Guid).ToList();
                AttachmentPointDatas.RemoveAt(i);
                if (metaData.Count > 0)
                {
                    MetaData.AttachmentPointGuidMetaData.Remove(metaData[0]);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(MetaData.Name) ||
            MetaData.Name == "Selectable")
        {
            MetaData.Name = gameObject.name;
            needsDirty = true;
        }

        // string path = AssetDatabase.GetAssetPath(gameObject);

        if (!AssetBundleManager.TryGetAssetBundleName(gameObject, out string assetBundleName))
        {
            Debug.LogError("Path was null!");
        }
        else
        {
            GUID = assetBundleName;
            needsDirty = true;
        }

        if (needsDirty)
        {
            EditorUtility.SetDirty(gameObject);
        }
#endif
    }

    // Pads an existing PNG image on disk to the target size (centered) with the given background color.
    private static void PadImageToSize(string path, int currentWidth, int currentHeight, int targetWidth, int targetHeight, Color background)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(bytes)) return;

            if (src.width == targetWidth && src.height == targetHeight)
            {
                UnityEngine.Object.Destroy(src);
                return;
            }

            var dst = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);

            // Fill background
            Color32 bg = background;
            var fill = new Color32[targetWidth * targetHeight];
            for (int i = 0; i < fill.Length; i++) fill[i] = bg;
            dst.SetPixels32(fill);

            // Blit centered
            int xOffset = Mathf.Max(0, (targetWidth - src.width) / 2);
            int yOffset = Mathf.Max(0, (targetHeight - src.height) / 2);
            var pixels = src.GetPixels(0, 0, src.width, src.height);
            dst.SetPixels(xOffset, yOffset, src.width, src.height, pixels);
            dst.Apply();

            File.WriteAllBytes(path, dst.EncodeToPNG());

            UnityEngine.Object.Destroy(src);
            UnityEngine.Object.Destroy(dst);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to pad image '{path}': {ex.Message}");
        }
    }
}