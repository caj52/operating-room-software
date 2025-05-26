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
        [field: SerializeField] public string key { get; private set; }
        [field: SerializeField] public string value { get; private set; }

        public Metadata(string k = "", string v = "")
        {
            key = "";
            value = "";
        }
    }

    #region Fields and Properties
    public static List<Selectable> ActiveSelectables { get; } = new List<Selectable>();

    public static Action SelectionChanged;
    public static List<Selectable> SelectedSelectables { get; private set; } = new();
    public static bool IsInElevationPhotoMode { get; private set; }
    public static UnityEvent ActiveSelectablesInSceneChanged { get; } = new();

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

    #endregion

    #region Monobehaviour
    private void Awake()
    {
        //if (!ConfigurationManager._instance.isDebug && GUID != "" && !ConfigurationManager._instance.isRoomBoundary(GUID)) gameObject.name = guid.ToString();

        if (AllowInverseControl)
        {
            if (GetComponent<CCDIK>() == null)
            {
                this.gameObject.AddComponent<CCDIK>();
            }
        }

        ActiveSelectables.Add(this);
        Transform parent = transform.parent;

        while (parent != null)
        {
            if (parent.TryGetComponent<AttachmentPoint>(out var attachmentPoint))
            {
                if (attachmentPoint != ParentAttachmentPoint)
                {
                    ParentAttachmentPoint = attachmentPoint;
                }
                break;
            }

            if (parent.TryGetComponent<Selectable>(out var selectable))
            {
                ParentSelectable = selectable;
                break;
            }

            parent = parent.parent;
        }

        _cameraRenderTextureElevation = GetComponentInChildren<Camera>();
        if (_cameraRenderTextureElevation != null)
        {
            _cameraRenderTextureElevation.enabled = false;
        }

        _originalRotation = transform.rotation;
        _originalLocalRotation = transform.localRotation;

        _highlightEffect = GetComponent<HighlightEffect>();

        _highlightProfileSelected = Resources.Load<HighlightProfile>
            ("HighlightProfile_SelectableSelected");

        if (_highlightEffect.profile != _highlightProfileSelected)
        {
            _highlightEffect.ProfileLoad(_highlightProfileSelected);
        }

        _gizmoHandler = GetComponent<GizmoHandler>();

        InputHandler.KeyStateChanged += InputHandler_KeyStateChanged;

        GizmoSettingsList.ForEach(item =>
        {
            if (item.OnlyIfRoot && ParentAttachmentPoint != null)
            {
                return;
            }

            if (!GizmoSettings.ContainsKey(item.GizmoType))
            {
                GizmoSettings[item.GizmoType] = new();
            }
            GizmoSettings[item.GizmoType][item.Axis] = item;
        });

        ActiveSelectablesInSceneChanged?.Invoke();
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

        //Debug.Log($"Mouse up detected over {gameObject.name}");

        if (InputHandler.IsPointerOverUIElement() /*||
        !InputHandler.WasProperClick*/)
        {
            //    Debug.Log($"Pointer Is Over UI {gameObject.name}");

            return;

        }

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


        if (GUID != "" &&
        !ConfigurationManager.IsRoomBoundary(GUID) &&
        !ConfigurationManager.IsBaseboard(GUID) &&
        !ConfigurationManager.IsWallProtector(GUID) &&
        transform.parent == null)
        {
            GenerateGuidName();
        }

        if (ScaleLevels.Count > 0)
        {
            CurrentScaleLevel = ScaleLevels.First(item => item.ModelDefault);
            CurrentPreviewScaleLevel = CurrentScaleLevel;
            CurrentScaleLevel.ScaleZ = transform.localScale.z;

            //Debug.Log($"Model default scale level is {CurrentScaleLevel.ScaleZ}");

            StoreChildScales();

            // Ensure CurrentScaleLevel is updated
            for (int i = 0; i < ScaleLevels.Count; i++)
            {
                var item = ScaleLevels[i];

                if (!item.ModelDefault)
                {
                    if (CurrentScaleLevel.Size == 0)
                    {
                        Debug.LogError("CurrentScaleLevel.Size is 0! Cannot calculate scale.");
                        continue;
                    }

                    float perc = item.Size / CurrentScaleLevel.Size;

                    if (!isDuplicated)
                    {
                        item.ScaleZ = CurrentScaleLevel.ScaleZ * perc;
                    }

                    //  Debug.Log($"Updated ScaleZ for ScaleLevel {i}: {item.ScaleZ}");
                }
                else
                {
                    item.ScaleZ = 1;
                }
            }




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

        Vector3 adjustedOffsetVector = new Vector3
            (InitialLocalPositionOffset.x * transform.localScale.x,
            InitialLocalPositionOffset.y * transform.localScale.y,
            InitialLocalPositionOffset.z * transform.localScale.z);

        transform.localPosition += adjustedOffsetVector;
        Started = true;
        ToggleMeasurableActiveStates(true);
        //Storing Reference for the Measurers
        Measurers.AddRange(Measurables
                .SelectMany(m => m.Measurements)
                .Where(measurement => measurement.Measurer != null)
                .Select(measurement => measurement.Measurer)
        );

   

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
                if (selectable.GetComponent
                <GizmoHandler>().GizmoUsedLastFrame)
                    return;
            }

            SelectedSelectables[0].Deselect();
            //SelectionChanged?.Invoke(null, null);
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
        Debug.Log($"Attempting to select {gameObject.name}");
       
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            return;
        }

        if (IsSelected)
        {
            //Debug.Log($"Could not select {gameObject.name} because it was already selected");
            return;
        }

        if (_isRaycastPlacementMode)
        {
            //Debug.Log($"Could not select {gameObject.name} because it is in raycast placement mode");
            return;
        }

        if (GizmoHandler.GizmoBeingUsed)
        {
            //Debug.Log($"Could not select {gameObject.name} because a gizmo is being used");
            return;
        }

        if (SelectedSelectables.Count > 0)
        {
            //Debug.Log($"Deselecting previously selected selectables");
            SelectedSelectables[0].Deselect(false);
        }

        //Debug.Log($"Currently selected selectable count is {SelectedSelectables.Count} (should be 0)");

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

    public void SetScaleLevel(ScaleLevel scaleLevel, bool setSelected, bool fireEvent = true)
    {
        Transform oldParent = null;

        //if (TryGetComponent(out ScaleGroup _))
        //{
        //    oldParent = transform.parent;
        //    transform.SetParent(null);
        //}
 
        CurrentPreviewScaleLevel = scaleLevel;

        if (fireEvent)
        {
            OnScaleChange?.Invoke(CurrentPreviewScaleLevel);
        }

        Quaternion storedRotation = transform.rotation;
        transform.rotation = _originalRotation;

        for (int j = 0; j < 2; j++) //not sure if still need to do this twice
        {
            Vector3 parentOriginalScale = transform.localScale;
            Vector3 newScale = new Vector3(transform.localScale.x, transform.localScale.y, scaleLevel.ScaleZ);
            transform.localScale = newScale;

            if (scaleLevel == CurrentScaleLevel)
            {
                //Debug.Log("Using stored child scales");
                for (int i = 0; i < transform.childCount; i++)
                    transform.GetChild(i).transform.localScale = _childScales[i];
            }
            else
            {
                // Debug.Log("Calculating child scales");
                Vector3 newParentScale = newScale;
                // Get the relative difference to the original scale
                var diffX = newParentScale.x / parentOriginalScale.x;
                var diffY = newParentScale.y / parentOriginalScale.y;
                var diffZ = newParentScale.z / parentOriginalScale.z;

                // Debug.Log($"Relative Difference ({diffX}, {diffY}, {diffZ})");

                // This inverts the scale differences
                var diffVector = new Vector3(1 / diffX, 1 / diffY, 1 / diffZ);

                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);

                    if (child.TryGetComponent(out IgnoreInverseScaling ignore))
                    {
                        if (ignore != null)
                        {
                            if (ignore.IgnoreX && ignore.IgnoreY && ignore.IgnoreZ) continue;
                        }
                    }

                    Vector3 localDiff = child.transform.InverseTransformVector(diffVector);
                    // Debug.Log($"{child.name} Current Scale is ({child.transform.localScale.x}, {child.transform.localScale.y}, {child.transform.localScale.z})");
                    // Debug.Log($"Local Diff after InverseTransformVector for {child.name} is ({localDiff.x}, {localDiff.y}, {localDiff.z})");
                    if (child.TryGetComponent(out Selectable selectable))
                    {
                        if (selectable.IsGizmoSettingAllowed(GizmoType.Scale, Axis.Z))
                        {
                            child.transform.localScale = Vector3.Scale(child.transform.localScale, diffVector);
                        }
                    }
                    else if (gameObject.TryGetComponent(out BoomHeadScaleHandler headScale))
                    {
                        child.transform.localScale = Vector3.Scale(child.transform.localScale, diffVector);
                    }
                    else
                    {
                        float x = Mathf.Abs(child.transform.localScale.x * localDiff.x);
                        float y = Mathf.Abs(child.transform.localScale.y * localDiff.y);
                        float z = Mathf.Abs(child.transform.localScale.z * localDiff.z);

                        if (ignore != null)
                        {
                            if (ignore.IgnoreX) x = child.transform.localScale.x;
                            if (ignore.IgnoreY) y = child.transform.localScale.y;
                            if (ignore.IgnoreZ) z = child.transform.localScale.z;
                        }
                        // Debug.Log($"Applying new scale of ({x}, {y}, {z})");
                        child.transform.localScale = new Vector3(x, y, z);
                    }
                }
            }
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

        transform.rotation = storedRotation;

        //if (TryGetComponent(out ScaleGroup _))
        //{
        //    transform.SetParent(oldParent);
        //}
    }

    public void UpdateZScaling(bool setSelected)
    {
        if (ScaleLevels.Count == 0) return;
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

    public List<PdfExporter.PdfImageData> GetAssemblyPDFImageData(Camera camera)
    {
        var imageDatas = new List<PdfExporter.PdfImageData>();

        for (int i = 0; i < 2; i++)
        {
            void FaceAllTowardGround()
            {
                _assemblySelectables
                    .Where(x => x.ZAlwaysFacesGround || x.ZAlwaysFacesGroundElevationOnly)
                    .ToList()
                    .ForEach(item => item.FaceZTowardGround());
            }

            // your existing rotation/ceiling‐avoidance loop
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
            var bounds = GetAssemblyBounds();

            // **only change**: pass invertDirection = (i == 1)
            string path = GetElevationPhoto(
                camera,
                bounds,
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
            var camera = GetComponentInChildren<Camera>();
            ActiveCameraRenderTextureElevation = camera;

            SetAssemblyToDefaultRotations();
            _measurableActiveStates.Clear();
            ToggleMeasurableActiveStates(true);

            //store visibility states of all selectables in scene for later
            List<bool> visibilities = ActiveSelectables.ConvertAll(x => x.gameObject.activeSelf);

            //shut off all selectables in the scene except for the ones in this arm assembly
            ActiveSelectables
                .Where(x => !_assemblySelectables.Contains(x))
                .ToList()
                .ForEach(x => x.gameObject.SetActive(false));

            PdfExporter.ExportElevationPdf(
                GetAssemblyPDFImageData(camera),
                _assemblySelectables, title, subtitle, assemblyDatas);

            for (int i = 0; i < ActiveSelectables.Count; i++)
                ActiveSelectables[i].gameObject.SetActive(visibilities[i]);

            RestoreArmAssemblyRotations();
            _assemblySelectables.ForEach(x => x.FaceZTowardGround());
            IsInElevationPhotoMode = false;
            ToggleMeasurableActiveStates(false);
        }
    }



    public List<PdfExporter.PdfImageData> ExportElevationPdf()
    {
        if (TryGetArmAssemblyRoot(out GameObject rootObj))
        {
            if (rootObj != gameObject)
            {
                rootObj.GetComponent<Selectable>().ExportElevationPdf();
                Debug.Log($"going to root object! root {rootObj.name} and current {gameObject.name}");
                return null;
            }

            // this obj is the ceiling mount
            IsInElevationPhotoMode = true;
            var camera = GetComponentInChildren<Camera>();
            ActiveCameraRenderTextureElevation = camera;

            SetAssemblyToDefaultRotations();
            _measurableActiveStates.Clear();
            ToggleMeasurableActiveStates(true);

            //store visibility states of all selectables in scene for later
            List<bool> visibilities = ActiveSelectables.ConvertAll(x => x.gameObject.activeSelf);

            //shut off all selectables in the scene except for the ones in this arm assembly
            ActiveSelectables
                .Where(x => !_assemblySelectables.Contains(x))
                .ToList()
                .ForEach(x => x.gameObject.SetActive(false));

            //PdfExporter.ExportElevationPdf(GetAssemblyPDFImageData(camera), _assemblySelectables, title, subtitle, assemblyDatas);

            // Generate PDF image data before restoring visibility
            var pdfImageData = GetAssemblyPDFImageData(camera);

            for (int i = 0; i < ActiveSelectables.Count; i++)
                ActiveSelectables[i].gameObject.SetActive(visibilities[i]);

            RestoreArmAssemblyRotations();
            _assemblySelectables.ForEach(x => x.FaceZTowardGround());
            IsInElevationPhotoMode = false;
            ToggleMeasurableActiveStates(false);
            return pdfImageData;
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
    /// Captures an elevation photo of the assembly from either the “front” or “back”
    /// depending on the invertDirection flag. All of your existing measurement‐,
    /// fitting‐ and rendering‐logic remains exactly as before.
    /// </summary>
    private string GetElevationPhoto(
        Camera camera,
        Bounds bounds,
        out int imageWidth,
        out int imageHeight,
        int fileIndex,
        bool invertDirection = false)
    {
        // enable & switch to ortho
        camera.enabled = true;
        camera.orthographic = true;

        // save original transform
        Vector3 cameraOriginalPos = camera.transform.position;

        // compute direction – invert if requested
        Vector3 outwardDirection = cameraOriginalPos - transform.position;
        if (invertDirection)
            outwardDirection = -outwardDirection;

        // position & aim
        camera.transform.position = bounds.center + (outwardDirection.normalized * bounds.extents.magnitude);
        camera.transform.LookAt(bounds.center, Vector3.up);
        camera.orthographicSize = bounds.extents.y;

        // run your existing “show only those measurables” logic
        float addedHeight = 0.1f;
        _assemblySelectables.ForEach(item =>
        {
            if (item.Measurables.Count == 0) return;
            item.Measurables.ForEach(measurable =>
            {
                if (measurable.Disabled) return;

                var valid = measurable.Measurements
                    .Where(m => m.Measurable.ShowInElevationPhoto)
                    .ToList();
                if (valid.Count == 0)
                {
                    measurable.SetActive(false);
                    return;
                }

                measurable.SetActive(true);
                measurable.UpdateMeasurements(ref addedHeight, camera);

                valid.ForEach(measurement =>
                {
                    measurement.Measurer.MeasurementText
                        .UpdateVisibilityAndPosition(camera, force: true);
                    measurement.Measurer.UpdateTransform(camera);

                    bounds.Encapsulate(measurement.Measurer.Renderer.bounds);
                    var textBounds = new Bounds(measurement.Measurer.TextPosition, Vector3.one * 1f);
                    bounds.Encapsulate(textBounds);
                });
            });
        });

        // reposition & re‐aim now that bounds may have grown
        camera.transform.position = bounds.center + (outwardDirection.normalized * bounds.extents.magnitude);
        camera.transform.LookAt(bounds.center, Vector3.up);
        camera.orthographicSize = bounds.extents.y;

        // grow ortho size until min & max world points fit in RT
        int safetyCounter = 1000;
        Vector2 screenMin = camera.WorldToScreenPoint(bounds.min);
        Vector2 screenMax = camera.WorldToScreenPoint(bounds.max);
        RenderTexture rt = camera.targetTexture;
        bool InShot(Vector2 p) =>
            p.x > 0 && p.y > 0 &&
            p.x < rt.width && p.y < rt.height;

        while (--safetyCounter > 0)
        {
            camera.orthographicSize += 1f;
            screenMin = camera.WorldToScreenPoint(bounds.min);
            screenMax = camera.WorldToScreenPoint(bounds.max);
            if (InShot(screenMin) && InShot(screenMax)) break;
        }
        if (safetyCounter == 0)
            throw new Exception("Could not get bounds of Arm Assembly for photo");

        // compute pixel dims
        imageWidth = Mathf.CeilToInt(Mathf.Abs(screenMax.x - screenMin.x));
        imageHeight = Mathf.CeilToInt(Mathf.Abs(screenMax.y - screenMin.y));

        // render
        Canvas.ForceUpdateCanvases();
        InGameLight.ToggleLights(false);
        var camLight = camera.GetComponentInChildren<Light>(true);
        camLight.gameObject.SetActive(true);

        camera.Render();
        camera.enabled = false;

        camLight.gameObject.SetActive(false);
        InGameLight.ToggleLights(true);

        // read back
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);
        float minX = Mathf.Min(screenMin.x, screenMax.x);
        float minY = Mathf.Min(screenMin.y, screenMax.y);
        tex.ReadPixels(new Rect(minX, minY, imageWidth, imageHeight), 0, 0);
        RenderTexture.active = null;

        // save PNG
        byte[] pngData = tex.EncodeToPNG();
        string filenameImage = Path.Combine(
            Application.persistentDataPath,
            $"ExportedArmAssemblyElevationShot{fileIndex}{(invertDirection ? "_back" : "_front")}.png");
    File.WriteAllBytes(filenameImage, pngData);

    // restore original camera position
    camera.transform.position = cameraOriginalPos;

    return filenameImage;
}

    public void RestoreArmAssemblyRotations()
    {
        if (TryGetArmAssemblyRoot(out GameObject rootObj))
        {
            if (rootObj == gameObject)
            {
                _assemblySelectables.ForEach(item =>
                {
                    if (item.AlignForElevationPhoto || item.ChangeHeightForElevationPhoto || item.ZAlwaysFacesGroundElevationOnly)
                        item.transform.localRotation = _originalRotations[item];
                });
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
                _assemblySelectables = GetComponentsInChildren<Selectable>().ToList();
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
    void ToggleMeasurableActiveStatesWhilePlacing(bool enable)
    {
        //Debug.LogError("ToggleMeasurableActiveStatesWhilePlacing");
        if (Measurables.Count > 0)
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

    private void ToggleMeasurableActiveStates(bool active)
    {
        if (active)
        {
            _assemblySelectables.ForEach(item =>
            {
                if (item.Measurables.Count > 0)
                {
                    item.Measurables.ForEach(measurable =>
                    {
                        measurable.ArmAssemblyActiveInElevationPhotoMode = true;
                        _measurableActiveStates[measurable] = measurable.IsActive;
                        measurable.SetActive(true);
                    });
                }
            });
        }
        else
        {
            _measurableActiveStates.Keys.ToList().ForEach(item =>
            {
                item.ArmAssemblyActiveInElevationPhotoMode = false;
                item.SetActive(_measurableActiveStates[item]);
                float _ = 0;
                item.UpdateMeasurements(ref _);
            });
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

        if (WallRestrictions[0] == RoomBoundaryType.Ceiling &&
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

                    if (WallRestrictions[0] == RoomBoundaryType.Ceiling
                        && (isCeilingCam || isOrbitalCam))
                    {
                        destination += RoomBoundary.DefaultWallThickness * Vector3.down;
                        normal *= -1;
                    }

                    if (SpecialTypes.Contains(SpecialSelectableType.Door))
                    {
                        if (hit.collider.gameObject.TryGetComponent(out RoomBoundary roomBoundary))
                        {
                            RoomBoundaryType roomBoundaryType = roomBoundary.RoomBoundaryType;

                            float halfThickness = RoomBoundary.DefaultWallThickness / 2f;
                            if (roomBoundaryType == RoomBoundaryType.WallNorth)
                            {
                                destination.z = hit.collider.transform.position.z - halfThickness;
                                //normal = -Vector3.forward;
                            }
                            else if (roomBoundaryType == RoomBoundaryType.WallSouth)
                            {
                                destination.z = hit.collider.transform.position.z + halfThickness;
                                //normal = Vector3.forward;
                            }
                            else if (roomBoundaryType == RoomBoundaryType.WallWest)
                            {
                                destination.x = hit.collider.transform.position.x + halfThickness;
                                //normal = Vector3.right;
                            }
                            else if (roomBoundaryType == RoomBoundaryType.WallEast)
                            {
                                destination.x = hit.collider.transform.position.x - halfThickness;
                                //normal = -Vector3.right;
                            }

                        }

                        destination.y = 0;
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
                            destination.y = RoundToNearestHalfInch(destination.y);
                            destination.z = RoundToNearestHalfInch(destination.z);
                        }
                        else if (zMag > xMag && zMag > yMag)
                        {
                            destination.y = RoundToNearestHalfInch(destination.y);
                            destination.x = RoundToNearestHalfInch(destination.x);
                        }
                    }

                    transform.SetPositionAndRotation(destination, Quaternion.LookRotation(normal));
                    _virtualParent = hit.collider.transform;
                    OnRaycastPositionUpdated?.Invoke();
                }

                if (_virtualParent == null)
                {
                    Vector3 direction = WallRestrictions[0] == RoomBoundaryType.Ceiling ? Vector3.up :
                        WallRestrictions[0] == RoomBoundaryType.Floor ? Vector3.down :
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

                if (WallRestrictions[0] == RoomBoundaryType.Ceiling &&
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

            if (CanPlaceAnywhere)
            {
                GetComponentInChildren<Collider>().enabled = true;
            }

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
}