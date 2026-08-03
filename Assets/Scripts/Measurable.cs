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

    /// <summary>
    /// Assembly geometry AABB for this elevation pass (no dim overlays). Dim bodies are
    /// placed outside this box so lines/labels never sit on the boom silhouette.
    /// </summary>
    public static Bounds CutsheetAssemblyBounds { get; private set; }

    public static bool CutsheetAssemblyBoundsValid { get; private set; }

    public static void SetCutsheetAssemblyBounds(Bounds bounds)
    {
        CutsheetAssemblyBounds = bounds;
        CutsheetAssemblyBoundsValid = bounds.size.sqrMagnitude > 1e-6f;
    }

    public static void ClearCutsheetAssemblyBounds()
    {
        CutsheetAssemblyBoundsValid = false;
        CutsheetAssemblyBounds = default;
    }

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

        // Drop / ceiling tubes are always vertical catalog callouts — attach parents
        // often leave local forward horizontal so axis-first would mis-classify them.
        string ownerName = owner.name ?? "";
        if (IsDropTubeName(ownerName))
            return true;

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

    public static bool IsDropTubeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return name.IndexOf("DropTube", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("BoomDrop", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("ArmDrop", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Vertical hang lengths that must sit flush under a ceiling plate / flange face:
    /// drop tubes, SimFlex tube, ceiling flange.
    /// </summary>
    public static bool IsVerticalHangLengthName(string name)
    {
        if (IsDropTubeName(name))
            return true;
        if (string.IsNullOrEmpty(name))
            return false;
        return name.IndexOf("SimFlexTube", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("CielingFlange", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("CeilingFlange", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsCeilingMountName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return name.IndexOf("CeilingMount", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Ensure this Measurable is a catalog length (ToOrigin) source. Used when a
    /// ScaleLevels Selectable shipped without a Measurable in its prefab/bundle.
    /// </summary>
    public void EnsureConfiguredAsCatalogLength()
    {
        if (MeasurementTypes == null)
            MeasurementTypes = new List<MeasurementType>();
        if (!MeasurementTypes.Contains(MeasurementType.ToArmAssemblyOrigin))
            MeasurementTypes.Add(MeasurementType.ToArmAssemblyOrigin);
        ShowInElevationPhoto = true;
        Disabled = false;
    }

    /// <summary>
    /// Dual-select strips "(Clone)" / ".001" so ArmDropTube(Clone) matches ArmDropTube.001.
    /// Also normalizes the FBX typo "BoomSegement" → "BoomSegment" so dual-select twins
    /// share one stem (saved configs may still carry the misspelled GO name).
    /// </summary>
    public static string DualSelectStem(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        string s = name.Replace("(Clone)", "").Trim();
        s = s.Replace("Segement", "Segment");
        int dot = s.LastIndexOf('.');
        if (dot > 0)
        {
            string suf = s.Substring(dot + 1);
            bool digits = suf.Length > 0;
            for (int i = 0; digits && i < suf.Length; i++)
                digits = char.IsDigit(suf[i]);
            if (digits)
                s = s.Substring(0, dot);
        }
        return s;
    }

    /// <summary>
    /// World bounds of ONLY this Size owner's meshes (nearest Selectable == owner).
    /// Excludes dual-select twins / child arms — those inflated BoomDropTube ticks
    /// into the flange and elbow.
    /// </summary>
    public static bool TryGetStrictOwnRendererBounds(Selectable owner, out Bounds bounds)
    {
        bounds = default;
        if (owner == null)
            return false;

        bool any = false;
        foreach (var r in owner.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            var nearestSel = r.GetComponentInParent<Selectable>(true);
            if (nearestSel != owner)
                continue;

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

    /// <summary>
    /// World bounds of the length owner's meshes, including same-stem dual-select twins
    /// (Size on .001, mesh on Clone). Never includes unrelated child Selectables.
    /// </summary>
    public static bool TryGetOwnRendererBounds(Selectable owner, out Bounds bounds)
    {
        bounds = default;
        if (owner == null)
            return false;

        var owners = new System.Collections.Generic.HashSet<Selectable> { owner };
        string stem = DualSelectStem(owner.name);
        if (!string.IsNullOrEmpty(stem))
        {
            if (owner.RelatedSelectables != null)
            {
                foreach (var rel in owner.RelatedSelectables)
                {
                    if (rel != null && DualSelectStem(rel.name) == stem)
                        owners.Add(rel);
                }
            }

            // Sibling / parent twins under a shared mount (Related list often empty).
            Transform walk = owner.transform.parent;
            for (int depth = 0; walk != null && depth < 6; depth++, walk = walk.parent)
            {
                foreach (var sib in walk.GetComponentsInChildren<Selectable>(true))
                {
                    if (sib != null && DualSelectStem(sib.name) == stem)
                        owners.Add(sib);
                }
            }
        }

        bool any = false;
        foreach (var sel in owners)
        {
            if (sel == null) continue;
            foreach (var r in sel.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;

                var nearestSel = r.GetComponentInParent<Selectable>(true);
                if (nearestSel == null || !owners.Contains(nearestSel))
                    continue;

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
        }

        return any;
    }

    /// <summary>
    /// True when <paramref name="ap"/> belongs to <paramref name="owner"/> (or same-stem
    /// dual-select twin), not a descendant arm/head Selectable. Critical for drop tubes:
    /// the whole boom hangs under the tip AP, so naive GetComponentsInChildren finds RailPlate.
    /// </summary>
    public static bool AttachmentPointOwnedByLengthOwner(AttachmentPoint ap, Selectable owner)
    {
        if (ap == null || owner == null)
            return false;
        var nearest = ap.GetComponentInParent<Selectable>(true);
        if (nearest == null)
            return false;
        if (nearest == owner)
            return true;
        return DualSelectStem(nearest.name) == DualSelectStem(owner.name);
    }

    /// <summary>
    /// Distal feature for a catalog length: farthest AttachmentPoint under the owner
    /// (or dual-select twin) along the length axis from the proximal mount.
    /// </summary>
    AttachmentPoint FindDistalAttachmentPoint(Selectable owner, AttachmentPoint proximal)
    {
        if (owner == null)
            return null;

        Vector3 origin = proximal != null
            ? proximal.transform.position
            : owner.transform.position;
        Vector3 axis = owner.transform.TransformDirection(Vector3.forward);
        if (axis.sqrMagnitude < 1e-8f)
            axis = Vector3.down;
        axis.Normalize();

        AttachmentPoint best = null;
        float bestAlong = 0.05f;

        void Consider(AttachmentPoint ap)
        {
            if (ap == null || ap == proximal)
                return;
            // Skip measurement helper spheres / deco APs (BoomDropTube aimed at "Sphere").
            string n = ap.name ?? "";
            if (n.IndexOf("Sphere", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            if (n.IndexOf("Measur", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            if (!AttachmentPointOwnedByLengthOwner(ap, owner))
                return;

            float along = Vector3.Dot(ap.transform.position - origin, axis);
            if (along < 0f)
                along = Vector3.Dot(ap.transform.position - origin, -axis);
            if (along > bestAlong)
            {
                bestAlong = along;
                best = ap;
            }
        }

        foreach (var ap in owner.GetComponentsInChildren<AttachmentPoint>(true))
            Consider(ap);

        if (owner.RelatedSelectables != null)
        {
            foreach (var rel in owner.RelatedSelectables)
            {
                if (rel == null || rel == owner)
                    continue;
                if (DualSelectStem(rel.name) != DualSelectStem(owner.name))
                    continue;
                foreach (var ap in rel.GetComponentsInChildren<AttachmentPoint>(true))
                    Consider(ap);
            }
        }

        return best;
    }

    /// <summary>
    /// Vertical catalog ticks = Size-owner mesh top/bottom. Hang AP local Z on the ceiling
    /// mount prefab (Blender plate thickness) must place the socket on the underside so
    /// mesh top == free-hang start. Read-only — no runtime transform guessing.
    /// </summary>
    static bool TryBuildVerticalCatalogYTicks(
        Selectable owner,
        float catalogLen,
        out Vector3 featureA,
        out Vector3 featureB,
        out string proximalName,
        out string distalName)
    {
        featureA = featureB = default;
        proximalName = distalName = "";
        if (owner == null || catalogLen < 0.02f)
            return false;
        if (!TryGetStrictOwnRendererBounds(owner, out Bounds rb) || rb.size.y < 0.02f)
            return false;

        float cx = rb.center.x;
        float cz = rb.center.z;
        featureA = new Vector3(cx, rb.max.y, cz);
        featureB = new Vector3(cx, rb.min.y, cz);
        if (featureA.y < featureB.y)
        {
            Vector3 swap = featureA;
            featureA = featureB;
            featureB = swap;
        }
        proximalName = "tubeMeshTop";
        distalName = "tubeMeshTip";

        float meshMm = rb.size.y * 1000f;
        float catalogMm = catalogLen * 1000f;
        bool sizeMismatch = Mathf.Abs(rb.size.y - catalogLen) > 0.02f;

        AttachmentPoint hang = null;
        if (owner.ParentAttachmentPoint != null)
            hang = owner.ParentAttachmentPoint;
        else if (owner.RelatedSelectables != null)
        {
            for (int i = 0; i < owner.RelatedSelectables.Count; i++)
            {
                var rel = owner.RelatedSelectables[i];
                if (rel != null && rel.ParentAttachmentPoint != null)
                {
                    hang = rel.ParentAttachmentPoint;
                    break;
                }
            }
        }

        float hangInsetM = 0f;
        bool hangInsetBroken = false;
        if (hang != null)
        {
            hangInsetM = featureA.y - hang.transform.position.y;
            hangInsetBroken = hangInsetM > 0.02f;
        }

        if (sizeMismatch || hangInsetBroken)
        {
            Debug.LogWarning(
                $"[ElevDim] VERT TUBE length-contract broken owner={owner.name} " +
                $"catalogMm={Mathf.RoundToInt(catalogMm)} meshMm={Mathf.RoundToInt(meshMm)} " +
                $"topY={featureA.y:F3} tipY={featureB.y:F3} " +
                $"hangInsetMm={Mathf.RoundToInt(hangInsetM * 1000f)} " +
                $"(sizeMismatch={sizeMismatch} hangInset={hangInsetBroken}; " +
                $"fix ScaleZ / hang AP local Z on ceiling mount prefab)",
                owner);
        }
        else
        {
            Debug.Log(
                $"[ElevDim] VERT TUBE owner={owner.name} catalogMm={Mathf.RoundToInt(catalogMm)} " +
                $"meshMm={Mathf.RoundToInt(meshMm)} topY={featureA.y:F3} tipY={featureB.y:F3} " +
                $"anchors={proximalName}→{distalName}",
                owner);
        }
        return true;
    }

    /// <summary>
    /// SINGLE cutsheet length path (tubes and arms): printed mm = catalog Size;
    /// tick ends = Size-owner mesh ends (drop tubes: world-Y mesh top/tip).
    /// </summary>
    private bool TryBuildCatalogLengthCallout(
        Measurement item,
        Measurer measurer,
        Camera camera,
        float catalogLen,
        bool vertical,
        ref float heightMod)
    {
        if (catalogLen <= 0f || measurer == null)
            return false;

        var owner = GetOwningSelectable();
        if (owner == null)
            return false;

        Vector3 featureA;
        Vector3 featureB;
        Vector3 axis;
        string proximalName;
        string distalName;

        // Vertical tubes/necks: ticks = Size-owner mesh ends (length isolation ⇒ mesh == Size).
        // Must run BEFORE distal-AP aim — that used to lock onto RailPlate.
        if (vertical
            && TryBuildVerticalCatalogYTicks(
                owner, catalogLen, out featureA, out featureB, out proximalName, out distalName))
        {
            axis = Vector3.down;
        }
        else
        {
            AttachmentPoint proximalAp = ResolveProximalForLengthOwner(owner)
                ?? GetSegmentProximalAttachmentPoint()
                ?? HighestAssemblyAttachmentPoint;
            AttachmentPoint distalAp = FindDistalAttachmentPoint(owner, proximalAp);

            Vector3 proximalFeat = proximalAp != null
                ? proximalAp.transform.position
                : owner.transform.position;

            axis = owner.transform.TransformDirection(Vector3.forward);
            if (axis.sqrMagnitude < 1e-8f)
                axis = vertical ? Vector3.down : Vector3.right;
            axis.Normalize();

            Vector3 aimPoint = distalAp != null
                ? distalAp.transform.position
                : transform.position;
            Vector3 toward = aimPoint - proximalFeat;
            if (toward.sqrMagnitude > 1e-6f && Vector3.Dot(axis, toward) < 0f)
                axis = -axis;

            if (vertical)
            {
                if (Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.5f)
                    axis = Vector3.down;
                else if (Vector3.Dot(axis, Vector3.down) < 0f)
                    axis = -axis;
            }
            else
            {
                axis.y = 0f;
                if (axis.sqrMagnitude < 1e-6f)
                {
                    toward.y = 0f;
                    axis = toward.sqrMagnitude > 1e-6f ? toward.normalized : Vector3.right;
                }
                else
                    axis.Normalize();
            }

            featureA = proximalFeat;
            featureB = featureA + axis * catalogLen;
            proximalName = proximalAp != null ? proximalAp.name : "pivot";
            distalName = distalAp != null ? distalAp.name : "aim";

            bool haveMesh = vertical
                ? TryGetStrictOwnRendererBounds(owner, out Bounds meshRb)
                : TryGetOwnRendererBounds(owner, out meshRb);
            if (haveMesh && meshRb.size.sqrMagnitude > 1e-4f)
            {
                Vector3 center = meshRb.center;
                float cAlong = Vector3.Dot(center, axis);
                float meshMin = ProjectBoundsMin(meshRb, axis);
                float meshMax = ProjectBoundsMax(meshRb, axis);
                featureA = center + axis * (meshMin - cAlong);
                featureB = center + axis * (meshMax - cAlong);
                if (Vector3.Dot(featureB - featureA, axis) < 0f)
                {
                    Vector3 swap = featureA;
                    featureA = featureB;
                    featureB = swap;
                }

                if (vertical)
                {
                    if (featureA.y < featureB.y)
                    {
                        Vector3 swap = featureA;
                        featureA = featureB;
                        featureB = swap;
                    }
                    proximalName = "meshTop";
                    distalName = "meshTip";
                }
                else
                {
                    float armY = meshRb.center.y;
                    featureA.y = armY;
                    featureB.y = armY;
                    proximalName = "meshNear";
                    distalName = "meshFar";
                }
            }
        }

        // --- Layout: offset MUST be perpendicular to the length axis ---
        // Lateral offset along the arm collapses leaders into the body (single line
        // through the tube with no end ticks). Horizontal → up/down; vertical → cam-right.
        Vector3 offset;
        float lane;
        bool labelBelow = false;
        float labelSideSign = 0f;
        Bounds asmBounds = CutsheetAssemblyBoundsValid
            ? CutsheetAssemblyBounds
            : (TryGetOwnRendererBounds(owner, out Bounds ownB) ? ownB : new Bounds(featureA, Vector3.one * 0.2f));

        if (vertical)
        {
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.right;
            right.Normalize();

            // Short tubes: keep leaders shorter than the dim span so a 100mm callout
            // does not draw as a wide empty rectangle beside the tube (0.18m leaders
            // on a 0.10m span looked like a floating box).
            bool shortTube = catalogLen <= 0.35f || IsDropTubeName(owner.name);
            lane = shortTube
                ? Mathf.Clamp(catalogLen * 0.35f, 0.03f, 0.08f)
                : (CutsheetLayoutSideMeters > 0.05f ? CutsheetLayoutSideMeters : 0.28f);
            float pad = shortTube ? 0.02f : ElevationDimPlacement.AssemblyClearPadMeters;
            Bounds tubeBounds = asmBounds;
            if (TryGetStrictOwnRendererBounds(owner, out Bounds colB) && colB.size.y > 0.02f)
                tubeBounds = colB;
            else if (TryGetOwnRendererBounds(owner, out Bounds ownTube) && ownTube.size.sqrMagnitude > 1e-4f)
                tubeBounds = ownTube;

            float tubeMinR = ProjectBoundsMin(tubeBounds, right);
            float tubeMaxR = ProjectBoundsMax(tubeBounds, right);
            float featR = Vector3.Dot(featureA, right);
            Vector3 reach = transform.position - featureA;
            reach.y = 0f;
            bool preferNeg = true;
            if (reach.sqrMagnitude > 0.01f && Vector3.Dot(right, reach.normalized) < -0.15f)
                preferNeg = false;
            float targetR = preferNeg
                ? tubeMinR - pad - lane
                : tubeMaxR + pad + lane;
            offset = right * (targetR - featR);
            float maxLeader = shortTube
                ? Mathf.Clamp(catalogLen * 0.55f, 0.035f, 0.08f)
                : 0.45f;
            float leader = Mathf.Abs(targetR - featR);
            if (leader > maxLeader)
                offset = right * ((preferNeg ? -1f : 1f) * maxLeader);
            labelSideSign = preferNeg ? -1f : 1f;

            // Extension-line feet on the tube's outboard face (drafting: leaders meet the part).
            Bounds faceRb = tubeBounds;
            if (faceRb.size.y > 0.02f)
            {
                float faceExtent =
                    Mathf.Abs(right.x) * faceRb.extents.x + Mathf.Abs(right.z) * faceRb.extents.z;
                Vector3 toFace = right * (preferNeg ? -faceExtent : faceExtent);
                featureA = new Vector3(faceRb.center.x, featureA.y, faceRb.center.z) + toFace;
                featureB = new Vector3(faceRb.center.x, featureB.y, faceRb.center.z) + toFace;
            }

            measurer.ElevationTextLane = Mathf.Max(1, Mathf.RoundToInt(lane * 10f));
        }
        else
        {
            // Always offset along world up (⊥ arm axis in elevation) so leaders form end ticks.
            lane = CutsheetLayoutLiftMeters > 0.05f ? CutsheetLayoutLiftMeters : 0.28f;
            float pad = ElevationDimPlacement.AssemblyClearPadMeters;
            float ceilingY = ElevationDimPlacement.CeilingUndersideY();
            float floorY = ElevationDimPlacement.FloorTopY();
            float margin = ElevationDimPlacement.CutsheetInFrameMarginMeters;
            float featureY = 0.5f * (featureA.y + featureB.y);
            float armTopY = featureY;
            float armBotY = featureY;
            if (TryGetOwnRendererBounds(owner, out Bounds armRb) && armRb.size.y > 0.01f)
            {
                armTopY = armRb.max.y;
                armBotY = armRb.min.y;
            }
            float labelBand = ElevationDimPlacement.LabelGapMeters + 0.08f;
            // Clear THIS arm (not full assembly top — flange always blocks "above").
            float aboveY = armTopY + pad + lane;
            float belowY = armBotY - pad - lane;

            if (aboveY + labelBand <= ceilingY - margin)
            {
                offset = Vector3.up * (aboveY - featureY);
                labelBelow = false;
            }
            else if (belowY - labelBand >= floorY + margin)
            {
                offset = Vector3.up * (belowY - featureY);
                labelBelow = true;
            }
            else
            {
                float y = Mathf.Max(floorY + margin + labelBand, armBotY - pad - Mathf.Min(lane, 0.2f));
                offset = Vector3.up * (y - featureY);
                labelBelow = true;
            }

            // Guaranteed ⊥ to horizontal length axis — never collapse the U.
            if (Mathf.Abs(offset.y) < 0.05f)
            {
                offset = Vector3.down * (pad + lane);
                labelBelow = true;
            }
        }

        measurer.ElevationPreferLabelBelow = labelBelow;
        measurer.ElevationLabelSideSign = labelSideSign;

        // Body outside the silhouette; extension lines stub to mesh ends.
        // Label always prints catalog mm (Measurer), even when drawn span follows mesh.
        item.Origin = featureA + offset;
        item.HitPoint = featureB + offset;

        measurer.ElevationLeaderFeatureA = featureA;
        measurer.ElevationLeaderFeatureB = featureB;
        measurer.ElevationLeadersValid = true;

        measurer.gameObject.SetActive(true);
        measurer.UpdateTransform(camera);
        // UpdateTransform LookAt/scale runs after SetActive — re-assert extension legs.
        measurer.RefreshCutsheetLeaders(camera, _lineRendererSizeScalar);
        if (measurer.TryGetLeaderPair(out var lead0, out var lead1))
        {
            ForceBlackLeaders(lead0);
            ForceBlackLeaders(lead1);
        }

        heightMod += vertical ? 0.16f : 0.32f;

        float span = Vector3.Distance(item.Origin, item.HitPoint);
        bool meshExtent = distalName != null
            && (distalName.StartsWith("mesh", System.StringComparison.Ordinal)
                || distalName.IndexOf("Far", System.StringComparison.Ordinal) >= 0);
        bool spanOk = meshExtent || Mathf.Abs(span - catalogLen) < 0.002f;
        if (!spanOk)
        {
            Debug.LogWarning(
                $"[ElevDim] CATALOG PLACE span mismatch owner={owner.name} " +
                $"catalog={catalogLen:F3} span={span:F3}",
                this);
        }
        Debug.Log(
            $"[ElevDim] CATALOG PLACE owner={owner.name} vertical={vertical} " +
            $"catalogMm={Mathf.RoundToInt(catalogLen * 1000f)} span={span:F3} spanOk={spanOk} " +
            $"proximal={proximalName} distal={distalName} lane={lane:F2} labelBelow={labelBelow} " +
            $"leaderLen0={Vector3.Distance(item.Origin, featureA):F3} " +
            $"leaderLen1={Vector3.Distance(item.HitPoint, featureB):F3} " +
            $"asm={(CutsheetAssemblyBoundsValid ? CutsheetAssemblyBounds.size.ToString("F2") : "none")}",
            this);
        return true;
    }

    /// <summary>
    /// Column bounds for a drop tube: own/dual-select first, else any renderer under the
    /// stem root (last resort so ArmDropTube still gets a vertical callout).
    /// </summary>
    static bool TryGetDropTubeColumnBounds(Selectable owner, out Bounds bounds)
    {
        // Prefer strict Size-owner mesh — twin encapsulate is for arms, not tube ticks.
        if (TryGetStrictOwnRendererBounds(owner, out bounds) && bounds.size.y > 0.02f)
            return true;
        if (TryGetOwnRendererBounds(owner, out bounds) && bounds.size.y > 0.02f)
            return true;

        bounds = default;
        if (owner == null)
            return false;

        Transform root = owner.transform;
        string stem = DualSelectStem(owner.name);
        for (Transform t = owner.transform.parent; t != null; t = t.parent)
        {
            if (!t.TryGetComponent(out Selectable anc))
                continue;
            if (DualSelectStem(anc.name) == stem)
                root = t;
            else
                break;
        }

        bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (r.bounds.size.y < 0.02f)
                continue;
            // Skip huge assembly leftovers — keep column-like pieces.
            if (r.bounds.size.y < Mathf.Max(r.bounds.size.x, r.bounds.size.z) * 0.8f)
                continue;
            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
                bounds.Encapsulate(r.bounds);
        }
        return any && bounds.size.y > 0.02f;
    }

    static float ProjectBoundsMin(Bounds b, Vector3 axis)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;
        float min = float.MaxValue;
        for (int ix = -1; ix <= 1; ix += 2)
        for (int iy = -1; iy <= 1; iy += 2)
        for (int iz = -1; iz <= 1; iz += 2)
        {
            Vector3 corner = c + new Vector3(ix * e.x, iy * e.y, iz * e.z);
            min = Mathf.Min(min, Vector3.Dot(corner, axis));
        }
        return min;
    }

    static float ProjectBoundsMax(Bounds b, Vector3 axis)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;
        float max = float.MinValue;
        for (int ix = -1; ix <= 1; ix += 2)
        for (int iy = -1; iy <= 1; iy += 2)
        for (int iz = -1; iz <= 1; iz += 2)
        {
            Vector3 corner = c + new Vector3(ix * e.x, iy * e.y, iz * e.z);
            max = Mathf.Max(max, Vector3.Dot(corner, axis));
        }
        return max;
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

            // Pinned cutsheet length (incl. dual-select borrow) wins over the host
            // Measurable's ShowInElevationPhoto flag — borrow already curated the owner.
            bool cutsheetLength = drawLength
                && item.MeasurementType == MeasurementType.ToArmAssemblyOrigin
                && catalogLen > 0f
                && (ShowInElevationPhoto || CutsheetCatalogLengthMeters > 0f);

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
            if (item.MeasurementType == MeasurementType.ToArmAssemblyOrigin)
                item.Measurer.RefreshCutsheetLeaders(camera, _lineRendererSizeScalar);
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

        bool lengthGate = ShowInElevationPhoto || CutsheetCatalogLengthMeters > 0f;
        if (drawLength
            && catalogLen <= 0f
            && lengthGate
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
                 && lengthGate
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
        if (Measurements != null)
        {
            foreach (var m in Measurements)
            {
                if (m?.Measurer == null) continue;
                m.Measurer.ElevationPreferLabelBelow = false;
                m.Measurer.ElevationLabelSideSign = 0f;
                m.Measurer.ElevationLeadersValid = false;
            }
        }
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
        else if (TryGetServiceHeadFloorRoot(owner, out Transform headRoot))
        {
            // Rails/shelves under the head must not own the clearance tick — use the full
            // head mesh (lowest vertex) so the dim reads floor → service-head bottom.
            root = headRoot;
            allowParentSelectable = false;
            allRenderersUnderRoot = true;
            Debug.Log(
                $"[ElevDim] FLOOR ROOT serviceHead owner={owner.name} " +
                $"head={headRoot.name} measurable={name} allRenderers=True",
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
        lr.useWorldSpace = true;
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

        // Rear_Rail / shelves: their Floor measurables parked ticks mid-head. One clearance
        // dim on the service-head body (allRenderers) is enough.
        if (IsServiceHeadAccessoryDimOwner(sel))
            return true;

        return false;
    }

    /// <summary>
    /// Rails/shelves/drawers under a service head — not tube/arm length, not floor clearance.
    /// </summary>
    public static bool IsServiceHeadAccessoryDimOwner(Selectable sel)
    {
        if (sel == null)
            return false;
        // The head body itself (row-tier ScaleLevels) is not an accessory.
        if (sel.GetComponent<BoomHeadScaleHandler>() != null)
            return false;
        if (!TryGetServiceHeadFloorRoot(sel, out _))
            return false;

        string n = sel.name;
        return n.IndexOf("Rail", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Shelf", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Drawer", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Basket", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Selectable / parent that owns the service-head body (BoomHeadScaleHandler or SH name).
    /// </summary>
    public static bool TryGetServiceHeadFloorRoot(Selectable owner, out Transform headRoot)
    {
        headRoot = null;
        if (owner == null)
            return false;

        var handler = owner.GetComponent<BoomHeadScaleHandler>()
            ?? owner.GetComponentInParent<BoomHeadScaleHandler>();
        if (handler != null)
        {
            headRoot = handler.transform;
            return true;
        }

        for (Transform t = owner.transform; t != null; t = t.parent)
        {
            string n = t.name;
            if (n.IndexOf("SpringBottom", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("1000SH", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ServiceHead", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                headRoot = t;
                return true;
            }
        }

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

                    var measurer = item.Measurer;
                    if (measurer == null)
                    {
                        if (Selectable.IsInElevationPhotoMode)
                            Debug.LogWarning($"[ElevDim] SKIP ToOrigin (Measurer null) measurable={name}", this);
                        break;
                    }

                    // Cutsheet: one builder for tubes and arms — Size label, Size tick span.
                    if (Selectable.IsInElevationPhotoMode)
                    {
                        float catalogLen = CutsheetCatalogLengthMeters > 0f
                            ? CutsheetCatalogLengthMeters
                            : ElevationLengthFormat.ResolveOwnSizeMeters(GetOwningSelectable());
                        if (catalogLen <= 0f)
                        {
                            measurer.gameObject.SetActive(false);
                            break;
                        }

                        float horiz = EstimateProximalHorizontalSpan(GetOwningSelectable());
                        bool vertical = ShouldUseVerticalCatalogCallout(catalogLen, horiz);
                        if (!TryBuildCatalogLengthCallout(
                                item, measurer, camera, catalogLen, vertical, ref heightMod))
                            measurer.gameObject.SetActive(false);
                        break;
                    }

                    // Live (non-elevation) interactive ToOrigin — world proximal→tip.
                    var proximalAp = GetSegmentProximalAttachmentPoint();
                    if (proximalAp == null)
                        proximalAp = HighestAssemblyAttachmentPoint;
                    if (proximalAp == null)
                    {
                        measurer.gameObject.SetActive(false);
                        break;
                    }

                    if (!measurer.TryGetLeaderPair(out _, out _))
                        break;

                    Vector3 addedHeight = Vector3.up * heightMod;
                    Vector3 origin = transform.position;
                    item.HitPoint = proximalAp.transform.position + addedHeight;
                    origin.y = proximalAp.transform.position.y;
                    item.Origin = origin + addedHeight;

                    if (!measurer.TryGetLeaderPair(out var hLead0, out var hLead1))
                    {
                        heightMod += 0.22f;
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

                    hLead1.enabled = true;
                    hLead1.positionCount = 2;
                    Vector3 line2Start = addedHeight + proximalAp.transform.position;
                    Vector3 line2End = proximalAp.transform.position;
                    hLead1.SetPosition(0, line2Start);
                    hLead1.SetPosition(1, line2End);
                    hLead1.startWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line2Start, camera);
                    hLead1.endWidth = _lineRendererSizeScalar * GetDistanceToCameraPlane(line2End, camera);

                    heightMod += 0.22f;
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
        // (and heightMod=0) shoved tube dims into the elbow after a correct CATALOG PLACE.
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
