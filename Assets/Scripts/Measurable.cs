using RTG;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Measurable : MonoBehaviour
{

    [System.Serializable]
    public class Measurement
    {
        public Measurement(Measurable measurable)
        {

            Measurable = measurable;
            Measurable.StartCoroutine(GetMeasurer());
        }

        private IEnumerator GetMeasurer()
        {

            if (!Measurer.Initialized)
            {
                yield return new WaitUntil(() => Measurer.Initialized);
            }
            Measurer = Measurer.GetMeasurer(this);
        }

        public Vector3 Origin { get; set; }
        public Vector3 Direction { get; set; }
        public Vector3 HitPoint { get; set; }
        public Measurer Measurer { get; private set; }
        public Measurable Measurable { get; }
        public MeasurementType MeasurementType { get; set; }
        public RoomBoundaryType RoomBoundaryType { get; set; }

        /// <summary>
        /// Dual-selectable tubes often wake inactive; StartCoroutine never finishes and
        /// Measurer stays null — elevation then silently skips them. Create synchronously.
        /// </summary>
        public void EnsureMeasurerExists()
        {
            if (Measurer != null)
                return;
            // Class static — not the instance property (which is null here).
            if (!global::Measurer.Initialized)
                return;
            Measurer = global::Measurer.GetMeasurer(this);
        }
    }

    public static UnityEvent ActiveMeasurablesChanged { get; } = new UnityEvent();
    private static readonly float _lineRendererSizeScalar = 0.005f;
    /// <summary>Floor-dim mm values already placed this elevation capture (dedupe note 1).</summary>
    private static readonly HashSet<int> _elevationFloorMmUsed = new();
    private static int _elevationFloorSepIndex;

    public static void BeginElevationMeasurementPass()
    {
        _elevationFloorMmUsed.Clear();
        _elevationFloorSepIndex = 0;
    }

    public List<Measurement> Measurements { get; } = new();
    [field: SerializeField] public List<MeasurementType> MeasurementTypes { get; private set; } = new();
    [field: SerializeField] private bool ForwardOnly { get; set; }
    [field: SerializeField] public bool ShowInElevationPhoto { get; private set; }
    private AttachmentPoint HighestAssemblyAttachmentPoint { get; set; }
    public bool ArmAssemblyActiveInElevationPhotoMode { get; set; }

    /// <summary>
    /// Pinned by <see cref="ApplyCutsheetElevation"/> for this capture pass only.
    /// Lets ToOrigin / Measurer use the assembly selectable's catalog Size even when
    /// dual-select GetOwningSelectable momentarily can't resolve Size.
    /// </summary>
    public float CutsheetCatalogLengthMeters { get; set; }

    /// <summary>
    /// Length owner chosen by ElevationCutsheetPass for this pass. Prevents RelatedSelectables
    /// Size-steal (tube must not inherit arm Size / horizontal geometry).
    /// </summary>
    public Selectable CutsheetLengthOwner { get; set; }

    /// <summary>
    /// Cutsheet layout: world meters to lift a horizontal catalog dim above the part.
    /// Assigned by ElevationCutsheetPass so arms don't share the tube's corner.
    /// </summary>
    public float CutsheetLayoutLiftMeters { get; set; }

    /// <summary>
    /// Cutsheet layout: world meters to offset a vertical catalog dim beside the part
    /// (along camera-right). Large enough that tube labels clear horizontal arm dims.
    /// </summary>
    public float CutsheetLayoutSideMeters { get; set; }

    /// <summary>True when this pass's length callout is drawn vertically (tube/flange).</summary>
    public bool CutsheetLengthIsVertical { get; set; }

    /// <summary>Cutsheet pass gate — UpdateMeasurements must not revive the opposite type.</summary>
    public bool CutsheetAllowLengthDraw { get; set; } = true;

    /// <summary>Cutsheet pass gate — UpdateMeasurements must not revive Floor during length pass.</summary>
    public bool CutsheetAllowFloorDraw { get; set; } = true;

    private Selectable proxyAlertWithORTABLE;
    public bool IsActive { get; private set; }
    [SerializeField] float minThreshold = 1;
    [SerializeField] float maxThreshold = 2;
    [SerializeField] bool isAdjustmentNeeded;


    [field: SerializeField]
    public bool Disabled { get; set; }

    private void OnEnable()
    {
        EventManager.OnCompareProximatryAlertWithOR_Table += CheckProximity;
     
    }

    private void Awake()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        MeasurementTypes = MeasurementTypes.Distinct().ToList();
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        Initialize();
        proxyAlertWithORTABLE = FindObjectsByType<Selectable>(FindObjectsSortMode.None)
    .FirstOrDefault(x => x.MetaData.Name == "OR_Table_0");
    }

    private void Initialize()
    {
        if (Measurements.Count > 0)
            return;

        bool newMeasurement = false;
        MeasurementTypes.ForEach(item =>
        {
            if (item == MeasurementType.Walls)
            {
                if (ForwardOnly)
                {
                    Measurements.Add(new Measurement(this)
                    {
                        MeasurementType = item
                    });
                }
                else
                {
                    new List<RoomBoundaryType>()
                    {
                        RoomBoundaryType.WallEast,
                        RoomBoundaryType.WallWest,
                        RoomBoundaryType.WallSouth,
                        RoomBoundaryType.WallNorth,
                    }.ForEach(type =>
                    {
                        Measurements.Add(new Measurement(this)
                        {
                            MeasurementType = item,
                            RoomBoundaryType = type
                        });
                    });
                }

            }
            else
            {
                Measurements.Add(new Measurement(this)
                {
                    MeasurementType = item
                });
            }
            newMeasurement = true;
        });

        if (newMeasurement)
        {
            ActiveMeasurablesChanged?.Invoke();
        }

        if (MeasurementTypes.Contains(MeasurementType.ToArmAssemblyOrigin))
        {
            Transform parent = transform.parent;
            AttachmentPoint attachmentPoint = null;
            while (parent != null)
            {
                if (parent.gameObject.TryGetComponent<AttachmentPoint>(out var point))
                    attachmentPoint = point;

                parent = parent.parent;
            }

            // Drop tubes keep their attach AP as a child; the mount AP only exists once
            // parented into an assembly. Don't hard-fail Start — resolve lazily later.
            HighestAssemblyAttachmentPoint = attachmentPoint;
            if (attachmentPoint == null)
                Debug.LogWarning($"{name}: no parent AttachmentPoint yet for ToArmAssemblyOrigin; will resolve when attached.", this);
        }
    }

    /// <summary>Nearest parent Selectable for this measurable (the segment that owns the dim).</summary>
    public Selectable GetOwningSelectable()
    {
        // Cutsheet pass pins the length owner explicitly — never let RelatedSelectables
        // Size-steal redirect a drop-tube dim onto the arm.
        if (Selectable.IsInElevationPhotoMode && CutsheetLengthOwner != null)
            return CutsheetLengthOwner;

        // Boom/light prefabs often have dual Selectables: ScaleLevels on one GO,
        // Measurable on another under the same related group. Prefer the selectable
        // that actually owns catalog Size / lists this measurable.
        Selectable nearest = GetComponentInParent<Selectable>(true);

        if (nearest != null)
        {
            // Prefer own Size only — ResolveSizeMeters walks RelatedSelectables and can
            // make a tube look like it "owns" the arm's 800 mm.
            if (nearest.Measurables != null && nearest.Measurables.Contains(this)
                && ElevationLengthFormat.ResolveOwnSizeMeters(nearest) > 0f)
                return nearest;

            if (ElevationLengthFormat.ResolveOwnSizeMeters(nearest) > 0f)
                return nearest;

            if (nearest.RelatedSelectables != null)
            {
                foreach (var rel in nearest.RelatedSelectables)
                {
                    if (rel == null)
                        continue;
                    if (rel.Measurables != null && rel.Measurables.Contains(this)
                        && ElevationLengthFormat.ResolveOwnSizeMeters(rel) > 0f)
                        return rel;
                }
            }
        }

        Transform t = transform;
        while (t != null)
        {
            if (t.TryGetComponent(out Selectable s)
                && ElevationLengthFormat.ResolveOwnSizeMeters(s) > 0f)
                return s;
            t = t.parent;
        }

        // Last resort: listed owner even with unresolved Size (caller may still fail draw).
        if (nearest != null && nearest.Measurables != null && nearest.Measurables.Contains(this))
            return nearest;

        return nearest;
    }

    /// <summary>True when the owning selectable has a positive catalog ScaleLevel.Size.</summary>
    public bool HasOwningCatalogLength()
    {
        if (CutsheetCatalogLengthMeters > 0f)
            return true;
        // Own Size only — ResolveSizeMeters steals Related arm lengths onto tubes.
        return ElevationLengthFormat.ResolveOwnSizeMeters(GetOwningSelectable()) > 0f;
    }

    /// <summary>
    /// Proximal end of this segment's length callout: the AttachmentPoint that parents
    /// the owning Selectable (not the assembly-root mount).
    /// </summary>
    public AttachmentPoint GetSegmentProximalAttachmentPoint()
    {
        var owner = GetOwningSelectable();
        if (owner == null)
            return HighestAssemblyAttachmentPoint;

        // Runtime attach wires ParentAttachmentPoint on the placed root. Dual-selectable
        // length owners (ScaleLevels on .001) often leave it null — the Mount wrapper in
        // RelatedSelectables holds the real parent AP.
        if (owner.ParentAttachmentPoint != null)
            return owner.ParentAttachmentPoint;

        if (owner.RelatedSelectables != null)
        {
            foreach (var rel in owner.RelatedSelectables)
            {
                if (rel != null && rel.ParentAttachmentPoint != null)
                    return rel.ParentAttachmentPoint;
            }
        }

        Transform t = owner.transform.parent;
        while (t != null)
        {
            if (t.TryGetComponent(out AttachmentPoint ap))
                return ap;
            t = t.parent;
        }

        return HighestAssemblyAttachmentPoint;
    }

    /// <summary>
    /// Elevation: catalog length is drawn vertically when the cutsheet layout classified
    /// it that way, else from ScaleLevel axis / own-mesh proportions — not part names.
    /// </summary>
    private bool ShouldUseVerticalCatalogCallout(float catalogLenMeters, float horizontalSpanMeters)
    {
        if (catalogLenMeters <= 0f)
            return false;

        // Layout already chose V/H and wrote side/lift lanes — honor that exactly.
        if (CutsheetCatalogLengthMeters > 0f)
            return CutsheetLengthIsVertical;

        return ClassifyCutsheetLengthVertical(GetOwningSelectable(), catalogLenMeters, horizontalSpanMeters);
    }

    /// <summary>
    /// SINGLE classifier for layout + draw. Length axis (local Z) first — never force
    /// Vertical from a tiny horiz span when the axis is clearly horizontal (dual-select
    /// .001 pivots sit at the mount and used to make 800 mm arms draw as 100 mm stubs).
    /// </summary>
    public static bool ClassifyCutsheetLengthVertical(
        Selectable owner, float catalogLenMeters, float horizontalSpanMeters)
    {
        if (catalogLenMeters <= 0f || owner == null)
            return false;

        Vector3 lengthAxis = owner.transform.TransformDirection(Vector3.forward);
        float upDot = 0f;
        if (lengthAxis.sqrMagnitude > 1e-8f)
            upDot = Mathf.Abs(Vector3.Dot(lengthAxis.normalized, Vector3.up));

        // Axis-first.
        if (upDot >= 0.75f)
            return true;
        if (upDot <= 0.35f)
            return false;

        // Ambiguous tilt: reach then tall own-mesh.
        if (horizontalSpanMeters >= 0.08f)
            return false;

        if (!TryGetOwnRendererBounds(owner, out Bounds rb))
            return false;
        float height = rb.size.y;
        float xz = Mathf.Max(rb.size.x, rb.size.z);
        return height > xz * 1.25f;
    }

    /// <summary>
    /// Horizontal span from proximal mount to distal tip — same tip as the draw path
    /// (this Measurable's transform), not lengthOwner.pivot which is often at the mount.
    /// </summary>
    public float EstimateProximalHorizontalSpan(Selectable lengthOwner)
    {
        AttachmentPoint proximal = ResolveProximalForLengthOwner(lengthOwner);
        if (proximal == null)
            proximal = HighestAssemblyAttachmentPoint;
        if (proximal == null)
            return 0f;

        // Match ToOrigin draw: distal = measurable transform, not lengthOwner pivot.
        Vector3 tip = transform.position;
        Vector3 delta = tip - proximal.transform.position;
        return new Vector2(delta.x, delta.z).magnitude;
    }

    AttachmentPoint ResolveProximalForLengthOwner(Selectable lengthOwner)
    {
        if (lengthOwner == null)
            return null;

        if (lengthOwner.ParentAttachmentPoint != null)
            return lengthOwner.ParentAttachmentPoint;

        if (lengthOwner.RelatedSelectables != null)
        {
            foreach (var rel in lengthOwner.RelatedSelectables)
            {
                if (rel != null && rel.ParentAttachmentPoint != null)
                    return rel.ParentAttachmentPoint;
            }
        }

        Transform t = lengthOwner.transform.parent;
        while (t != null)
        {
            if (t.TryGetComponent(out AttachmentPoint ap))
                return ap;
            t = t.parent;
        }

        return null;
    }

    /// <summary>
    /// Length axis clearly world-vertical (tubes/flanges). Arms with horizontal Z return false.
    /// </summary>
    public static bool IsVerticalLengthOwner(Selectable owner)
    {
        if (owner == null)
            return false;

        Vector3 lengthAxis = owner.transform.TransformDirection(Vector3.forward);
        if (lengthAxis.sqrMagnitude > 1e-8f
            && Mathf.Abs(Vector3.Dot(lengthAxis.normalized, Vector3.up)) >= 0.75f)
            return true;

        return false;
    }

    /// <summary>
    /// World bounds of the length owner's own meshes only — never child Selectables
    /// (arms/lights under a drop tube). Encapsulates every own renderer so tube dims
    /// span the full visible column, not a single slightly-short mesh piece.
    /// </summary>
    public static bool TryGetOwnRendererBounds(Selectable owner, out Bounds bounds)
    {
        bounds = default;
        if (owner == null)
            return false;

        bool any = false;
        foreach (var r in owner.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;

            // Nearest Selectable must be this length owner — skip arm/light/outlet kids.
            var nearestSel = r.GetComponentInParent<Selectable>(true);
            if (nearestSel != owner)
                continue;

            // Skip tiny deco / measurement helpers that would inflate XZ without height.
            float h = r.bounds.size.y;
            float xz = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
            if (h < 0.01f && xz < 0.05f)
                continue;

            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
                bounds.Encapsulate(r.bounds);
        }

        return any;
    }

    private bool TryGetOwnerRendererBounds(out Bounds bounds)
    {
        return TryGetOwnRendererBounds(GetOwningSelectable(), out bounds);
    }

    /// <summary>
    /// Place a vertical catalog-length callout on the length owner's own column mesh.
    /// </summary>
    private bool TryBuildVerticalCatalogCallout(
        Measurement item,
        Measurer measurer,
        Camera camera,
        float catalogLen,
        ref float heightMod)
    {
        if (catalogLen <= 0f || measurer == null)
            return false;

        var owner = GetOwningSelectable();
        Vector3 top;
        Vector3 bottom;
        float meshH = 0f;

        if (TryGetOwnerRendererBounds(out Bounds rb) && rb.size.y > 0.02f)
        {
            // Span the full visible own-mesh column. Label still shows catalog Size;
            // ending mid-tube (catalog-from-top when mesh > Size) looked arbitrary.
            meshH = rb.max.y - rb.min.y;
            top = new Vector3(rb.center.x, rb.max.y, rb.center.z);
            bottom = new Vector3(rb.center.x, rb.min.y, rb.center.z);
            if (meshH > 0.02f && Mathf.Abs(meshH - catalogLen) > 0.04f)
            {
                Debug.Log(
                    $"[ElevDim] vertical meshH={meshH:F3} vs catalog={catalogLen:F3} " +
                    $"owner={(owner != null ? owner.name : "null")} — ticks span full own mesh",
                    this);
            }
        }
        else if (owner != null)
        {
            // No own mesh: drop catalog length down the owner's length axis from its pivot.
            Vector3 origin = owner.transform.position;
            Vector3 axis = owner.transform.TransformDirection(Vector3.forward);
            if (axis.sqrMagnitude < 1e-8f)
                axis = Vector3.down;
            axis.Normalize();
            if (Vector3.Dot(axis, Vector3.down) < 0f)
                axis = -axis;
            top = origin;
            bottom = origin + axis * catalogLen;
            Debug.LogWarning(
                $"[ElevDim] vertical FALLBACK no own mesh owner={(owner != null ? owner.name : "null")} " +
                $"catalogMm={Mathf.RoundToInt(catalogLen * 1000f)}",
                this);
        }
        else
            return false;

        if (bottom.y > top.y)
            (top, bottom) = (bottom, top);

        Vector3 right = camera != null ? camera.transform.right : Vector3.right;
        right.y = 0f;
        if (right.sqrMagnitude < 1e-6f)
            right = Vector3.right;
        right.Normalize();

        // Keep the callout on the side opposite the arm reach so 150 mm never lands
        // on the elbow (camera-right alone pushed it into the spring-arm in front shots).
        Vector3 armReach = Vector3.zero;
        var proximal = GetSegmentProximalAttachmentPoint() ?? HighestAssemblyAttachmentPoint;
        if (proximal != null)
        {
            armReach = transform.position - proximal.transform.position;
            armReach.y = 0f;
        }
        if (armReach.sqrMagnitude > 0.01f && Vector3.Dot(right, armReach.normalized) > 0f)
            right = -right;

        // Prefer curated side lane. Never fall back to heightMod stacking — that shoved
        // 150 mm tube dims ~0.77 m sideways into the elbow joint.
        float side = CutsheetLayoutSideMeters > 0.05f
            ? CutsheetLayoutSideMeters
            : (CutsheetLengthIsVertical ? 0.48f : 0.42f);

        // Land ticks on the column face toward the dim (not the centerline through the solid).
        if (TryGetOwnerRendererBounds(out Bounds faceRb) && faceRb.size.y > 0.02f)
        {
            float faceExtent =
                Mathf.Abs(right.x) * faceRb.extents.x + Mathf.Abs(right.z) * faceRb.extents.z;
            Vector3 toFace = right * faceExtent;
            top = new Vector3(faceRb.center.x, top.y, faceRb.center.z) + toFace;
            bottom = new Vector3(faceRb.center.x, bottom.y, faceRb.center.z) + toFace;
        }

        item.Origin = bottom + right * side;
        item.HitPoint = top + right * side;

        if (measurer != null)
            measurer.ElevationTextLane = Mathf.Max(1, Mathf.RoundToInt(side * 10f));

        measurer.gameObject.SetActive(true);
        measurer.UpdateTransform(camera);

        if (!measurer.TryGetLeaderPair(out var lead0, out var lead1))
            return true;

        lead0.enabled = true;
        lead0.positionCount = 2;
        lead0.SetPosition(0, item.Origin);
        lead0.SetPosition(1, bottom);
        lead0.startWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(item.Origin, camera);
        lead0.endWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(bottom, camera);
        ForceBlackLeaders(lead0);

        lead1.enabled = true;
        lead1.positionCount = 2;
        lead1.SetPosition(0, item.HitPoint);
        lead1.SetPosition(1, top);
        lead1.startWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(item.HitPoint, camera);
        lead1.endWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(top, camera);
        ForceBlackLeaders(lead1);

        heightMod += 0.16f;

        Debug.Log(
            $"[ElevDim] VERTICAL PLACE owner={(owner != null ? owner.name : "null")} " +
            $"catalogMm={Mathf.RoundToInt(catalogLen * 1000f)} " +
            $"meshH={meshH:F3} topY={top.y:F3} bottomY={bottom.y:F3} span={(top.y - bottom.y):F3} " +
            $"side={side:F2} xz=({top.x:F2},{top.z:F2})",
            this);
        return true;
    }

    /// <summary>
    /// Elevation capture must not depend on Start/coroutines having already run
    /// (inactive dual-selectables often never spawned their Measurer).
    /// </summary>
    public void EnsureInitializedForElevation()
    {
        if (Disabled)
            return;

        if (Measurements.Count == 0 && MeasurementTypes != null && MeasurementTypes.Count > 0)
            Initialize();

        foreach (var m in Measurements)
            m?.EnsureMeasurerExists();

        // Re-bind CurrentScaleLevel from Selected so ShouldDraw sees catalog Size
        // (deferred init / dual-selectables can leave CurrentScaleLevel null).
        var owner = GetOwningSelectable();
        owner?.EnsureCurrentScaleLevelFromCatalog();
    }

    /// <summary>
    /// Cutsheet elevation: enable and update ONLY the requested Floor / catalog ToOrigin
    /// measurers. Walls/Ceiling stay off — never activated, never "erased later".
    /// Does not fire ActiveMeasurablesChanged (that re-enables shared-canvas labels).
    /// </summary>
    /// <param name="drawLength">Draw ToArmAssemblyOrigin catalog callout when Size &gt; 0.</param>
    /// <param name="drawFloor">Draw Floor clearance when ShowInElevation allows.</param>
    public void ApplyCutsheetElevation(
        ref float heightMod,
        Camera camera,
        Selectable lengthOwner,
        bool drawLength = true,
        bool drawFloor = true)
    {
        if (!Selectable.IsInElevationPhotoMode || Disabled)
            return;

        CutsheetLengthOwner = lengthOwner;
        lengthOwner?.EnsureCurrentScaleLevelFromCatalog();

        // Own Size only. ResolveSizeMeters walks RelatedSelectables and made drop tubes
        // inherit the arm's 800 mm (curated 150 → DRAW 800).
        float catalogLen = CutsheetCatalogLengthMeters > 0f
            ? CutsheetCatalogLengthMeters
            : ElevationLengthFormat.ResolveOwnSizeMeters(lengthOwner);
        if (catalogLen <= 0f)
            catalogLen = ElevationLengthFormat.ResolveOwnSizeMeters(GetOwningSelectable());

        if (drawLength)
            CutsheetCatalogLengthMeters = catalogLen;

        CutsheetAllowLengthDraw = drawLength;
        CutsheetAllowFloorDraw = drawFloor;

        // Soft flag only — do not SetActive(true) (that enables Walls + fires canvas events).
        IsActive = true;

        foreach (var item in Measurements)
        {
            if (item?.Measurer == null)
                continue;

            bool cutsheetFloor = drawFloor
                && item.MeasurementType == MeasurementType.Floor
                && ShowInElevationPhoto
                && !ShouldSkipElevationFloorDim();

            bool cutsheetLength = drawLength
                && item.MeasurementType == MeasurementType.ToArmAssemblyOrigin
                && ShowInElevationPhoto
                && catalogLen > 0f;

            if (!cutsheetFloor && !cutsheetLength)
            {
                // Shared Floor+ToOrigin Measurable: length/floor passes run separately.
                // Do not kill the type this pass is not drawing — the other pass owns it.
                if (item.MeasurementType == MeasurementType.ToArmAssemblyOrigin && !drawLength)
                    continue;
                if (item.MeasurementType == MeasurementType.Floor && !drawFloor)
                    continue;

                item.Measurer.gameObject.SetActive(false);
                item.Measurer.DisableElevationVisuals();
                if (item.Measurer.MeasurementText != null)
                    item.Measurer.MeasurementText.gameObject.SetActive(false);
                if (item.Measurer.LineRenderers != null)
                {
                    foreach (var lr in item.Measurer.LineRenderers)
                    {
                        if (lr != null)
                            lr.enabled = false;
                    }
                }
                continue;
            }

            item.Measurer.gameObject.SetActive(true);
        }

        UpdateMeasurements(ref heightMod, camera);

        foreach (var item in Measurements)
        {
            if (item?.Measurer == null || !item.Measurer.gameObject.activeSelf)
                continue;
            if (item.MeasurementType != MeasurementType.Floor
                && item.MeasurementType != MeasurementType.ToArmAssemblyOrigin)
                continue;

            item.Measurer.UpdateTransform(camera);
            if (item.Measurer.MeasurementText != null)
                item.Measurer.MeasurementText.UpdateVisibilityAndPosition(camera, force: true);

            // Catch "measurer on but label dead/imperial" — the leftover-glyph failure mode.
            if (item.MeasurementType == MeasurementType.ToArmAssemblyOrigin
                && item.Measurer.MeasurementText != null)
            {
                var mt = item.Measurer.MeasurementText;
                string dist = item.Measurer.Distance;
                bool textOn = mt.gameObject.activeSelf;
                bool hasMm = !string.IsNullOrEmpty(dist)
                    && dist.IndexOf("mm", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!textOn || !hasMm)
                {
                    Debug.LogWarning(
                        $"[ElevDim] LABEL FAIL measurable={name} type={item.MeasurementType} " +
                        $"textOn={textOn} distance='{dist}' " +
                        $"shouldDraw={item.Measurer.ShouldDrawInElevationPhoto()} " +
                        $"pinnedM={CutsheetCatalogLengthMeters:F3} " +
                        $"bind={{ {ElevationLengthFormat.DiagnoseSizeBinding(lengthOwner)} }}",
                        this);
                }
            }
        }

        if (drawLength
            && catalogLen <= 0f
            && ShowInElevationPhoto
            && Measurements.Any(m => m != null && m.MeasurementType == MeasurementType.ToArmAssemblyOrigin))
        {
            Debug.LogWarning(
                $"[ElevDim] SKIP catalog length measurable={name} " +
                $"lengthOwner={(lengthOwner != null ? lengthOwner.name : "null")} " +
                $"bindOwner={{ {ElevationLengthFormat.DiagnoseSizeBinding(lengthOwner)} }}",
                this);
        }
        else if (drawLength
                 && catalogLen > 0f
                 && ShowInElevationPhoto
                 && Measurements.Any(m => m != null
                     && m.MeasurementType == MeasurementType.ToArmAssemblyOrigin
                     && m.Measurer != null
                     && m.Measurer.gameObject.activeSelf))
        {
            Debug.Log(
                $"[ElevDim] DRAW catalog length measurable={name} " +
                $"lengthOwner={(lengthOwner != null ? lengthOwner.name : "null")} " +
                $"catalogMm={Mathf.RoundToInt(catalogLen * 1000f)} pinnedM={CutsheetCatalogLengthMeters:F3} " +
                $"owner={(GetOwningSelectable() != null ? GetOwningSelectable().name : "null")}",
                this);
        }
    }

    public void SetActive(bool active)
    {

        IsActive = active && !Disabled;

        Measurements.ToList().ForEach(item =>
        {
            if (item.Measurer)
            {
                bool enable = IsActive;
                // Elevation must not briefly enable wall/ceiling measurers (imperial labels
                // live on a shared canvas; ActivateMeasurablesChanged would revive them).
                if (enable && Selectable.IsInElevationPhotoMode)
                {
                    enable = item.Measurer.ShouldDrawInElevationPhoto()
                        && !(item.MeasurementType == MeasurementType.Floor && ShouldSkipElevationFloorDim());
                }

                item.Measurer.gameObject.SetActive(enable);
                item.Measurer.LineRenderers.ForEach(renderer => renderer.enabled =
                    item.MeasurementType == MeasurementType.ToArmAssemblyOrigin && enable);
                if (!enable)
                    item.Measurer.DisableElevationVisuals();
            }
           
        });
        ActiveMeasurablesChanged?.Invoke();
    }

    /// <summary>
    /// Clears cutsheet pins and forces IsActive off so Update() cannot keep
    /// redrawing elevation dims into the live scene after PDF capture.
    /// </summary>
    public void ClearCutsheetElevationState()
    {
        CutsheetCatalogLengthMeters = -1f;
        CutsheetLengthOwner = null;
        CutsheetLayoutLiftMeters = 0f;
        CutsheetLayoutSideMeters = 0f;
        CutsheetLengthIsVertical = false;
        CutsheetAllowLengthDraw = true;
        CutsheetAllowFloorDraw = true;
        ArmAssemblyActiveInElevationPhotoMode = false;
        IsActive = false;
        if (Measurements == null)
            return;
        foreach (var item in Measurements)
        {
            if (item?.Measurer == null)
                continue;
            item.Measurer.DisableElevationVisuals();
            item.Measurer.gameObject.SetActive(false);
            if (item.Measurer.MeasurementText != null)
                item.Measurer.MeasurementText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Elevation RT only: keep Floor + catalog ToOrigin measurers; hide walls/ceiling/etc.
    /// MeasurementText lives on a shared canvas, so disabling the Measurer is required.
    /// </summary>
    public void ApplyElevationMeasurerWhitelist()
    {
        if (!Selectable.IsInElevationPhotoMode)
            return;

        foreach (var item in Measurements)
        {
            if (item?.Measurer == null)
                continue;

            bool keep = item.Measurer.ShouldDrawInElevationPhoto()
                && !((item.MeasurementType == MeasurementType.Floor) && ShouldSkipElevationFloorDim());

            item.Measurer.gameObject.SetActive(keep);
            if (!keep && item.Measurer.MeasurementText != null)
                item.Measurer.MeasurementText.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        Measurements.ToList().ForEach(item =>
        {
            if (item.Measurer != null)
            {
                Destroy(item.Measurer.gameObject);
            }
        });
        EventManager.OnCompareProximatryAlertWithOR_Table -= CheckProximity;
    }

    private int GetTotalNeededMeasurements()
    {
        int count = 0;
        MeasurementTypes.ForEach(item =>
        {
            switch (item)
            {
                case MeasurementType.Walls:
                    count += 4;
                    break;
                case MeasurementType.Floor:
                case MeasurementType.Ceiling:
                case MeasurementType.ToArmAssemblyOrigin:
                    count++;
                    break;
            }
        });
        return count;
    }

    private static Dictionary<RoomBoundaryType, Vector3> _wallDirectionVectors = new()
    {
        { RoomBoundaryType.WallNorth, Vector3.forward },
        { RoomBoundaryType.WallSouth, -Vector3.forward },
        { RoomBoundaryType.WallEast, Vector3.right },
        { RoomBoundaryType.WallWest, -Vector3.forward }
    };

    private void UpdateMeasurementViaRaycast(Vector3 direction, Measurement measurement, bool ignoreSelectables = false)
    {
        // Cutsheets: cast from the underside of the element (note 7), not center/child pivot.
        Vector3 origin = GetElevationCastOrigin(direction);

        // Elevation floor: always commit plumb underside → floor-top. Do not Wall-ray
        // afterward — that overwrote HitPoint XZ and skewed clearance lines.
        if (Selectable.IsInElevationPhotoMode
            && direction.y < -0.5f
            && measurement.MeasurementType == MeasurementType.Floor)
        {
            measurement.Origin = origin;
            float floorY = ElevationDimPlacement.FloorTopY();
            measurement.HitPoint = new Vector3(origin.x, floorY, origin.z);
            return;
        }

        Ray ray = new Ray(origin, direction);
        int mask = LayerMask.GetMask("Wall");
        if (ignoreSelectables)
        {
            mask = LayerMask.GetMask("Wall");
        }

        bool floorIsOn = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor).gameObject.activeSelf;

        if (!floorIsOn)
        {
            RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor).gameObject.SetActive(true);
        }

        if (Physics.Raycast(ray, out RaycastHit raycastHit, 1000f, mask))
        {
            var obj = raycastHit.collider.gameObject;
            if (obj.layer == LayerMask.NameToLayer("Wall") || obj.CompareTag("Wall") || obj.CompareTag("Baseboard"))
            {
                measurement.Origin = ray.origin;
                measurement.HitPoint = raycastHit.point;
            }
        }

        if (!floorIsOn)
        {
            RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor).gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Elevation floor/ceiling ray start — delegates to ElevationDimPlacement
    /// (cast-up underside, not down into internal colliders).
    /// </summary>
    private Vector3 GetElevationCastOrigin(Vector3 direction)
    {
        Vector3 fallback = transform.position;
        if (!Selectable.IsInElevationPhotoMode)
            return fallback;

        bool floorCast = direction.y < -0.5f;
        bool ceilCast = direction.y > 0.5f;
        if (!floorCast && !ceilCast)
            return fallback;

        Transform root = null;
        bool allowParentSelectable = true;
        bool allRenderersUnderRoot = false;

        // Light product roots (U202 / Simeon_*): Floor measurable is a child of the root
        // Selectable, while LightFactory + head meshes live under a *different* child
        // Selectable. Parent/self factory search always missed, so UndersideOrigin filtered
        // to yoke-only meshes and the tick sat on the arm. Include every renderer under
        // the light root. Boom/Arm names are excluded so hanging lights don't steal boom floors.
        var owner = GetOwningSelectable() ?? GetComponentInParent<Selectable>();
        if (IsLightProductFloorOwner(owner))
        {
            root = owner.transform;
            allowParentSelectable = false;
            allRenderersUnderRoot = true;
            Debug.Log(
                $"[ElevDim] FLOOR ROOT lightProduct owner={owner.name} " +
                $"measurable={name} allRenderers=True",
                this);
        }
        else if (owner != null)
        {
            root = owner.transform;
        }

        if (root == null)
            return fallback;

        if (floorCast)
            return ElevationDimPlacement.UndersideOrigin(
                root, fallback, allowParentSelectable, allRenderersUnderRoot);

        // Ceiling: highest renderer top (rare on cutsheets).
        float maxY = float.MinValue;
        Vector3 at = fallback;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (r.bounds.max.y > maxY)
            {
                maxY = r.bounds.max.y;
                at = new Vector3(r.bounds.center.x, r.bounds.max.y, r.bounds.center.z);
            }
        }
        return maxY > float.MinValue ? at : fallback;
    }

    /// <summary>
    /// U|ONE / U|002 style light roots — not boom/arm segments that may contain a hanging light.
    /// </summary>
    static bool IsLightProductFloorOwner(Selectable owner)
    {
        if (owner == null)
            return false;

        string n = owner.name;
        if (n.IndexOf("Boom", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("ArmSegment", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("ArmDrop", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        if (n.IndexOf("Simeon", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("U202", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("U002", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("UOne", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Fallback: light factory under this owner, still excluding boom/arm names above.
        return owner.GetComponentInChildren<LightFactory>(true) != null;
    }

    private static void ForceBlackLeaders(LineRenderer lr)
    {
        if (lr == null) return;
        lr.startColor = Color.black;
        lr.endColor = Color.black;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        lr.colorGradient = grad;
    }

    /// <summary>
    /// Note 2: mid top-arm floor dims (BoomSegment_1 / ArmSegment_1 ToFloor) are ambiguous
    /// vs cutsheet callouts — hide them in elevation photos only.
    /// Light assemblies still skip those arm floors; the light-head Floor is what remains.
    /// </summary>
    private bool ShouldSkipElevationFloorDim()
    {
        if (!Selectable.IsInElevationPhotoMode)
            return false;

        var sel = GetComponentInParent<Selectable>();
        if (sel == null || sel.gameObject == null)
            return false;

        string n = sel.gameObject.name;
        // Always skip proximal light/boom arms — even when a LightFactory sits under the arm.
        // Keeping them used to draw clearance to the yoke while the head hangs lower.
        if (n.IndexOf("BoomSegment_1", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("ArmSegment_1", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("TopArm", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Top boom housing / IU cover naming.
        if (n.IndexOf("TurningCover", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("CeilingCover", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("TandemCover", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private float GetDistanceToCameraPlane(Vector3 point, Camera camera = null)
    {
        if (camera == null)
        {
            camera = Camera.main;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        Plane nearFrustrumPlane = planes[4];

        Vector3 planePoint = nearFrustrumPlane.ClosestPointOnPlane(point);
        return Vector3.Distance(point, planePoint);
    }

    public void UpdateMeasurements(ref float heightMod, Camera camera = null)
    {
        if (camera == null)
        {
            camera = Camera.main;
        }

        foreach (var item in Measurements)
        {
            switch (item.MeasurementType)
            {
                case MeasurementType.Walls:
                    // Cutsheet elevations never draw wall dims (imperial interactive leftovers).
                    if (Selectable.IsInElevationPhotoMode)
                    {
                        if (item.Measurer != null)
                        {
                            item.Measurer.gameObject.SetActive(false);
                            if (item.Measurer.MeasurementText != null)
                                item.Measurer.MeasurementText.gameObject.SetActive(false);
                        }
                        break;
                    }
                    if (ForwardOnly)
                    {
                        UpdateMeasurementViaRaycast(transform.forward, item);
                    }
                    else
                    {
                        UpdateMeasurementViaRaycast(_wallDirectionVectors[item.RoomBoundaryType], item);
                    }
                    break;
                case MeasurementType.Ceiling:
                    if (Selectable.IsInElevationPhotoMode)
                    {
                        if (item.Measurer != null)
                        {
                            item.Measurer.gameObject.SetActive(false);
                            if (item.Measurer.MeasurementText != null)
                                item.Measurer.MeasurementText.gameObject.SetActive(false);
                        }
                        break;
                    }
                    UpdateMeasurementViaRaycast(Vector3.up, item);
                    break;
                case MeasurementType.Floor:
                    // Length/floor are separate cutsheet passes on possibly-shared Measurables.
                    // When this pass isn't drawing floors, leave existing floor dims alone.
                    if (Selectable.IsInElevationPhotoMode && !CutsheetAllowFloorDraw)
                        break;
                    if (ShouldSkipElevationFloorDim())
                    {
                        if (item.Measurer != null)
                            item.Measurer.gameObject.SetActive(false);
                        break;
                    }

                    UpdateMeasurementViaRaycast(Vector3.down, item, true);

                    if (Selectable.IsInElevationPhotoMode && item.Measurer != null)
                    {
                        // Snap floor end to decorative floor TOP before measuring span.
                        var hp = item.HitPoint;
                        hp.y = ElevationDimPlacement.FloorTopY();
                        item.HitPoint = hp;

                        // Note 1: dedupe near-identical floor heights; separate survivors laterally.
                        float span = Vector3.Distance(item.Origin, item.HitPoint);
                        int mm = Mathf.RoundToInt(span * 1000f);
                        bool duplicate = false;
                        foreach (int used in _elevationFloorMmUsed)
                        {
                            if (Mathf.Abs(used - mm) <= 5)
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (duplicate)
                        {
                            item.Measurer.gameObject.SetActive(false);
                            break;
                        }

                        _elevationFloorMmUsed.Add(mm);
                        _elevationFloorSepIndex++;

                        // Keep the dim line ON the equipment (ticks must read). Text
                        // placement alone handles left/right separation — do not shove
                        // Origin/HitPoint away from the part.
                        item.Measurer.ElevationTextLane = _elevationFloorSepIndex;
                        item.Measurer.gameObject.SetActive(true);
                        item.Measurer.UpdateTransform(camera);

                        // Explicit end ticks so the underside/floor anchors read clearly.
                        if (item.Measurer.TryGetLeaderPair(out var fLead0, out var fLead1)
                            && camera != null)
                        {
                            Vector3 right = camera.transform.right;
                            right.y = 0f;
                            if (right.sqrMagnitude < 1e-6f)
                                right = Vector3.right;
                            right.Normalize();
                            float tick = ElevationDimPlacement.TickHalfLengthMeters;
                            Vector3 o = item.Origin;
                            Vector3 h = item.HitPoint;
                            h.y = ElevationDimPlacement.FloorTopY();

                            fLead0.enabled = true;
                            fLead0.positionCount = 2;
                            fLead0.SetPosition(0, o - right * tick);
                            fLead0.SetPosition(1, o + right * tick);
                            fLead0.startWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(o, camera);
                            fLead0.endWidth = fLead0.startWidth;
                            ForceBlackLeaders(fLead0);

                            fLead1.enabled = true;
                            fLead1.positionCount = 2;
                            fLead1.SetPosition(0, h - right * tick);
                            fLead1.SetPosition(1, h + right * tick);
                            fLead1.startWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(h, camera);
                            fLead1.endWidth = fLead1.startWidth;
                            ForceBlackLeaders(fLead1);
                        }

                        Debug.Log(
                            $"[ElevDim] FLOOR PLACE measurable={name} " +
                            $"owner={(GetOwningSelectable() != null ? GetOwningSelectable().name : "null")} " +
                            $"undersideY={item.Origin.y:F3} floorY={item.HitPoint.y:F3} " +
                            $"spanMm={mm} xz=({item.Origin.x:F2},{item.Origin.z:F2})",
                            this);
                    }
                    break;
                case MeasurementType.ToArmAssemblyOrigin:
                {
                    // Length/floor are separate cutsheet passes on possibly-shared Measurables.
                    // When this pass isn't drawing lengths, leave existing length dims alone.
                    if (Selectable.IsInElevationPhotoMode && !CutsheetAllowLengthDraw)
                        break;

                    // Cutsheet: only configurable segments (catalog Size) get length callouts
                    // in elevation photos — spring arms with empty ScaleLevels stay unlabeled.
                    if (Selectable.IsInElevationPhotoMode && !HasOwningCatalogLength())
                    {
                        if (item.Measurer != null)
                            item.Measurer.gameObject.SetActive(false);
                        var skipOwner = GetOwningSelectable();
                        Debug.LogWarning(
                            $"[ElevDim] SKIP ToOrigin (no catalog Size) measurable={name} " +
                            $"pinnedM={CutsheetCatalogLengthMeters:F3} " +
                            $"bind={{ {ElevationLengthFormat.DiagnoseSizeBinding(skipOwner)} }}",
                            this);
                        break;
                    }

                    var proximalAp = GetSegmentProximalAttachmentPoint();
                    if (proximalAp == null)
                        proximalAp = HighestAssemblyAttachmentPoint;

                    var measurer = item.Measurer;
                    if (measurer == null)
                    {
                        if (Selectable.IsInElevationPhotoMode)
                            Debug.LogWarning($"[ElevDim] SKIP ToOrigin (Measurer null) measurable={name}", this);
                        break;
                    }

                    if (!measurer.TryGetLeaderPair(out _, out _))
                    {
                        if (Selectable.IsInElevationPhotoMode)
                        {
                            Debug.LogWarning(
                                $"[ElevDim] SKIP ToOrigin (LineRenderers<{2}) measurable={name} " +
                                $"count={(measurer.LineRenderers != null ? measurer.LineRenderers.Count : 0)}",
                                this);
                            // Still place the mm label even if leader ticks are missing.
                            if (HasOwningCatalogLength())
                            {
                                measurer.gameObject.SetActive(true);
                                measurer.UpdateTransform(camera);
                            }
                        }
                        break;
                    }

                    // Configurable catalog length with no proximal AP: still label it
                    // on the owner's own column mesh (ceiling tube/flange) when vertical.
                    if (proximalAp == null)
                    {
                        if (Selectable.IsInElevationPhotoMode && HasOwningCatalogLength())
                        {
                            float len = CutsheetCatalogLengthMeters > 0f
                                ? CutsheetCatalogLengthMeters
                                : ElevationLengthFormat.ResolveOwnSizeMeters(GetOwningSelectable());
                            bool vertical = ShouldUseVerticalCatalogCallout(len, 0f);
                            if (vertical
                                && TryBuildVerticalCatalogCallout(item, measurer, camera, len, ref heightMod))
                                break;
                            // Horizontal with no proximal: cannot place arm-style leaders.
                        }

                        measurer.gameObject.SetActive(false);
                        break;
                    }

                    Vector3 proximalPos = proximalAp.transform.position;
                    Vector3 distalPos = transform.position;
                    Vector3 delta = distalPos - proximalPos;
                    float horiz = new Vector2(delta.x, delta.z).magnitude;
                    float catalogLen = Selectable.IsInElevationPhotoMode
                        ? (CutsheetCatalogLengthMeters > 0f
                            ? CutsheetCatalogLengthMeters
                            : ElevationLengthFormat.ResolveOwnSizeMeters(GetOwningSelectable()))
                        : -1f;
                    // Vertical vs horizontal from geometry + catalog Size — not part names.
                    bool verticalCallout = Selectable.IsInElevationPhotoMode
                        && ShouldUseVerticalCatalogCallout(catalogLen, horiz);

                    if (Selectable.IsInElevationPhotoMode)
                    {
                        var owner = GetOwningSelectable();
                        float upDot = 0f;
                        float boundsH = 0f;
                        float boundsXz = 0f;
                        if (owner != null)
                        {
                            Vector3 axis = owner.transform.TransformDirection(Vector3.forward);
                            if (axis.sqrMagnitude > 1e-8f)
                                upDot = Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up));
                        }
                        if (TryGetOwnerRendererBounds(out Bounds diagRb))
                        {
                            boundsH = diagRb.size.y;
                            boundsXz = Mathf.Max(diagRb.size.x, diagRb.size.z);
                        }
                        Debug.Log(
                            $"[ElevDim] ToOrigin owner={(owner != null ? owner.name : "null")} " +
                            $"catalogMm={Mathf.RoundToInt(Mathf.Max(0f, catalogLen) * 1000f)} " +
                            $"pinnedM={CutsheetCatalogLengthMeters:F3} " +
                            $"horiz={horiz:F3} upDot={upDot:F2} ownMeshH={boundsH:F3} ownMeshXz={boundsXz:F3} " +
                            $"vertical={verticalCallout} " +
                            $"proximal={(proximalAp != null ? proximalAp.name : "null")} " +
                            $"measurable={name}",
                            this);

                        if (!verticalCallout && catalogLen > 0f && (horiz < 0.15f || upDot >= 0.5f))
                        {
                            Debug.LogWarning(
                                $"[ElevDim] EXPECTED VERTICAL but got horizontal callout " +
                                $"owner={(owner != null ? owner.name : "null")} " +
                                $"catalogMm={Mathf.RoundToInt(catalogLen * 1000f)} " +
                                $"horiz={horiz:F3} upDot={upDot:F2} ownMeshH={boundsH:F3} ownMeshXz={boundsXz:F3}",
                                this);
                        }
                    }

                    if (verticalCallout)
                    {
                        if (TryBuildVerticalCatalogCallout(item, measurer, camera, catalogLen, ref heightMod))
                            break;

                        measurer.gameObject.SetActive(false);
                        break;
                    }

                    // Curated lift lane, else a fixed clear band — never heightMod
                    // (accumulator once left arm dims ~0.1 m up, sitting on the part).
                    Vector3 addedHeight = Vector3.up * (
                        CutsheetLayoutLiftMeters > 0.05f
                            ? CutsheetLayoutLiftMeters
                            : (Selectable.IsInElevationPhotoMode ? 0.40f : heightMod));
                    Vector3 origin = transform.position;
                    item.HitPoint = proximalAp.transform.position + addedHeight;
                    origin.y = proximalAp.transform.position.y;
                    item.Origin = origin + addedHeight;

                    if (Selectable.IsInElevationPhotoMode)
                        measurer.gameObject.SetActive(true);

                    if (Selectable.IsInElevationPhotoMode)
                        measurer.UpdateTransform(camera);

                    if (!measurer.TryGetLeaderPair(out var hLead0, out var hLead1))
                    {
                        heightMod += Selectable.IsInElevationPhotoMode ? 0.32f : 0.22f;
                        break;
                    }

                    hLead0.enabled = true;
                    hLead0.positionCount = 2;
                    Vector3 line1Start = addedHeight + origin;
                    Vector3 line1End = transform.position;
                    hLead0.SetPosition(0, line1Start);
                    hLead0.SetPosition(1, line1End);
                    hLead0.startWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line1Start, camera);
                    hLead0.endWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line1End, camera);
                    if (Selectable.IsInElevationPhotoMode)
                        ForceBlackLeaders(hLead0);

                    hLead1.enabled = true;
                    hLead1.positionCount = 2;
                    Vector3 line2Start = addedHeight + proximalAp.transform.position;
                    Vector3 line2End = proximalAp.transform.position;
                    hLead1.SetPosition(0, line2Start);
                    hLead1.SetPosition(1, line2End);
                    hLead1.startWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line2Start, camera);
                    hLead1.endWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line2End, camera);
                    if (Selectable.IsInElevationPhotoMode)
                        ForceBlackLeaders(hLead1);

                    // Extra vertical separation so elevation PDF dims do not overlap.
                    heightMod += Selectable.IsInElevationPhotoMode ? 0.32f : 0.22f;

                    break;
                }
            }
        }
    }

    private void Update()
    {
        //CheckProximity(GetComponentInParent<Selectable>().gameObject, 1, 2);

        if (!IsActive) return;
        // Cutsheet pass owns Origin/HitPoint/leaders. Re-running here with Camera.main
        // (and heightMod=0) shoved tube dims into the elbow after a correct VERTICAL PLACE.
        if (Selectable.IsInElevationPhotoMode)
            return;
        float _ = 0;
        UpdateMeasurements(ref _);
    }

    public void CheckProximity(GameObject referenceObject, float minThresholdDistance, float maxThresholdDistance)
    {
        if (UI_ToggleProximityAlerts.IsActive)
        {

            Selectable selectable = referenceObject.GetComponent<Selectable>();

            if (selectable.SpecialTypes.Count == 0)
                return;
            if (selectable.SpecialTypes[0].Equals(SpecialSelectableType.Mount) && proxyAlertWithORTABLE)
            {
                Vector3 refPos = referenceObject.transform.position;
                Vector3 tablePos = proxyAlertWithORTABLE.transform.position;

                Vector3 adjustedRefPos = new Vector3(refPos.x, 0, refPos.z);
                Vector3 adjustedTablePos = new Vector3(tablePos.x, 0, tablePos.z);

                float distance = Vector3.Distance(adjustedRefPos, adjustedTablePos);
                float moveAwayBy = minThresholdDistance - distance;
                float moveCloserBy = distance - maxThresholdDistance;

                if (distance < minThresholdDistance)
                {
                    UI_DialogPrompt.Open($"{selectable.MetaData.Name} is TOO CLOSE to OR_Table_0 . Suggested Adjustment: Move it farther by {moveAwayBy:F2} meters.",
                    new ButtonAction
                    {
                        //ButtonText = "Auto Placement",
                        //Action = () =>
                        //{
                        //    isAdjustmentNeeded = false;
                        //    Vector3 newPosition = MoveAway(referenceObject, tablePos, moveAwayBy);
                        //    referenceObject.transform.position = newPosition;
                        //    Debug.Log($"Auto-Moving {selectable.MetaData.Name} farther by {moveAwayBy:F2} meters.");
                        //    UI_DialogPrompt.Close();
                        //},
                        ButtonText = "OK",
                        Action = () =>
                        {
                            isAdjustmentNeeded = true;
                            UI_DialogPrompt.Close();
                        },

                    }
                   //new ButtonAction
                   //{
                   //    ButtonText = "Cancel",
                   //    Action = () =>
                   //    {
                   //        UI_DialogPrompt.Close();
                   //    },
                   //}
                   );
                }
                else if (distance > maxThresholdDistance)
                {
                    Vector3 newPosition = MoveCloser(referenceObject, tablePos, moveCloserBy);
                    Debug.Log($"Auto-Moving {selectable.MetaData.Name} closer by {moveCloserBy:F2} meters.");
                    UI_DialogPrompt.Open($"{selectable.MetaData.Name} is TOO FAR from OR_Table_0.Suggested Adjustment: Move it closer by {moveCloserBy:F2} meters.",
                     new ButtonAction
                     {
                         ButtonText = "Ok",
                         Action = () =>
                         {
                             UI_DialogPrompt.Close();
                             isAdjustmentNeeded = true;
                         },
                     }
                    );
                }
                else
                {
                    if (isAdjustmentNeeded)
                    {
                        Debug.Log($"Anas => {selectable.MetaData.Name} is at an IDEAL DISTANCE from OR_Table_0");
                        UI_DialogPrompt.Open($"{selectable.MetaData.Name} is at an IDEAL DISTANCE from OR_Table_0.No adjustment needed",
                         new ButtonAction
                         {
                             ButtonText = "Ok",
                             Action = () =>
                             {
                                 UI_DialogPrompt.Close();
                                 isAdjustmentNeeded = false;
                             },
                         }
                        );
                    }
                }
            }
        }

    }
    private Vector3 MoveAway(GameObject obj, Vector3 tablePos, float moveBy)
    {
        Vector3 direction = (obj.transform.position - tablePos).normalized;
        Vector3 newPosition = new Vector3(obj.transform.position.x + (direction.x * moveBy), obj.transform.position.y, obj.transform.position.z + (direction.z * moveBy));

        return FindValidPosition(newPosition, obj);
    }
    private Vector3 MoveCloser(GameObject obj, Vector3 tablePos, float moveBy)
    {
        float offset = 3;
        Vector3 direction = (tablePos - obj.transform.position).normalized;
        Vector3 newPosition = new Vector3(obj.transform.position.x + (direction.x * moveBy) + offset, obj.transform.position.y, obj.transform.position.z + (direction.z * moveBy) + offset);

        return FindValidPosition(newPosition, obj);
    }
    private Vector3 FindValidPosition(Vector3 targetPosition, GameObject obj)
    {
        int maxAttempts = 8;
        float offset = 0.5f;
        int layerMask = ~LayerMask.GetMask("Wall");

        for (int i = 0; i < maxAttempts; i++)
        {
            if (!Physics.CheckSphere(targetPosition, 0.1f, layerMask))
            {
                Debug.Log($"Valid position found for {obj.name} at {targetPosition}");
                return targetPosition;
            }

            // Try shifting in multiple directions instead of just one
            targetPosition += new Vector3(offset * (i % 2 == 0 ? 1 : -1), 0, offset * (i % 3 == 0 ? 1 : -1));
        }

        Debug.LogWarning($"{obj.name} could not find a valid position after multiple attempts.");
        return obj.transform.position;
    }
    //private Vector3 FindValidPosition(Vector3 targetPosition, GameObject obj)
    //{
    //    int maxAttempts = 5; 
    //    float offset = 0.5f;
    //    Debug.Log("Checking the Adjusted Location");
    //    int layerMask = ~LayerMask.GetMask("Wall");

    //    for (int i = 0; i < maxAttempts; i++)
    //    {
    //        if (!Physics.CheckSphere(targetPosition, 0.2f, layerMask)) 
    //        {

    //            Debug.Log("Checking the Adjusted Location 1");
    //            return targetPosition;
    //        }

    //        targetPosition += new Vector3(offset, 0, offset);

    //        Debug.Log("Checking the Adjusted Location 2");
    //    }

    //    Debug.LogWarning($"{obj.name} could not find a valid position after multiple attempts.");
    //    return obj.transform.position;
    //}
}

public enum MeasurementType
{
    Walls,
    Floor,
    Ceiling,
    ToArmAssemblyOrigin
}
