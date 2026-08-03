using HighlightPlus;
using SplenSoft.UnityUtilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(TrackedObject))]
public partial class AttachmentPoint : MonoBehaviour
{
    public static AttachmentPoint HoveredAttachmentPoint { get; private set; }
    public static AttachmentPoint SelectedAttachmentPoint { get; private set; }
    public static UnityEvent SelectedAttachmentPointChanged { get; } = new();
    public static EventHandler AttachmentPointHoverStateChanged;
    public static EventHandler AttachmentPointClicked;

    /// <summary>
    /// Passes a bool which, if true, indicates that this attachment point
    /// has a selectable attached to it
    /// </summary>
    [field: SerializeField]
    public UnityEvent<bool> StatusUpdated { get; private set; }

    /// <summary>
    /// Global GUID
    /// </summary>
    [field: SerializeField] 
    public string GUID { get; private set; } = "_AP";

    [field: SerializeField] 
    public List<Selectable> AttachedSelectable { get; private set; } = new(0);

    [SerializeField, ReadOnly] 
    private bool _attachmentPointHovered;

    [field: SerializeField] 
    private HighlightEffect HighlightHovered { get; set; }

    /// <summary>
    /// Should be considered "seed" or backup data. 
    /// The most recent version should be pulled from 
    /// the online database (or a cached version thereof)
    /// </summary>
    [field: SerializeField]
    public AttachmentPointMetaData MetaData { get; set; }

    /// <summary>
    /// Moves up in transform hierarchy to same parent as first parent attachment point. Used to keep rotations separate for multiple arm assemblies
    /// </summary>
    [field: SerializeField] 
    public bool MoveUpOnAttach { get; private set; }

    /// <summary>
    /// Lower transform hierarchy items will use this attachment point as a rotation reference when taking elevation photos (instead of using ceiling mount attachment points). This is used for arm segments having opposite rotation directions in elevation photos.
    /// </summary>
    [field: SerializeField] 
    public bool TreatAsTopMost { get; private set; }

    /// <summary>
    /// Structural role for length parts: tip of measurable length vs hinge/joint vs mount.
    /// Used by editor tooling and future solid-stack length contracts — not for row-config heads.
    /// </summary>
    [field: SerializeField]
    public AttachPointRole Role { get; private set; } = AttachPointRole.Unspecified;

    /// <summary>
    /// Allows the attachment point to have multiple attached selectables, otherwise attachpoint will disable once an attachment is selected
    /// </summary>
    [field: SerializeField] 
    private bool MultiAttach { get; set; }

    /// <summary>
    /// Sets the maximum number of attachments for a attachment points with MultiAttach set to True
    /// </summary>
    [field: SerializeField] private int MultiLimit { get; set; } = 3;

    public List<Selectable> ParentSelectables { get; } = new();

    [field: SerializeField, ReadOnly] 
    public Transform _originalParent { get; private set; }

    [field: SerializeField] private bool _hasNormalizedParent = false;
    [field: SerializeField] private MeshRenderer Renderer { get; set; }
    private Collider _collider; 
    private bool _isDestroyed;

    // Add new fields to track state
    [field: SerializeField] private Vector3 _originalLocalPosition;
    [field: SerializeField] private Quaternion _originalLocalRotation;
    [field: SerializeField] private bool _hasBeenInitialized;

    private bool AreAnyParentSelectablesSelected => 
        ParentSelectables.Any(x => Selectable.SelectedSelectables.Contains(x));

    #region Monobehaviour
    private void Awake()
    {
        RemoveNullSelectables(); // Clean up nulls on awake
        EmptyNullList();

        _collider = GetComponentInChildren<Collider>();

        if (Renderer == null)
        {
            Renderer = GetComponentInChildren<MeshRenderer>();
        }

        Transform parent = transform.parent;
        _originalParent = parent;

        while (parent.GetComponent<AttachmentPoint>() == null)
        {
            var selectable = parent.GetComponent<Selectable>();
            if (selectable != null && !ParentSelectables.Contains(selectable))
            {
                ParentSelectables.Add(selectable);
                selectable.SelectableDestroyed.AddListener(() => Destroy(gameObject));
            }
            parent = parent.parent;
            if (parent == null) break;
        }

        // Only direct child selectables — same contract as room-load wiring.
        // GetComponentsInChildren would also grab nested service-head/outlet
        // selectables; those get disabled/rebuilt on place and leave dead refs
        // that crash room save in TrackedObject.GetGUIDs.
        if (AttachedSelectable.Count == 0)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i).TryGetComponent(out Selectable s))
                    SetAttachedSelectable(s);
            }
        }

        ParentSelectables.ForEach(item =>
        {
            item.MouseOverStateChanged += MouseOverStateChanged;
        });

        Selectable.SelectionChanged += SelectionChanged;
        ObjectMenu.ActiveStateChanged.AddListener(EndHoverStateIfHovered);
        UpdateComponentStatus();
    }

    private void Start()
    {
        if (!ConfigurationManager.IsLoading)
            SetToProperParent();
        RefreshSolidCoverPlateVisibility();
        // Room-load / already-parented accessories: same face contract as SetAttachedSelectable.
        RemoveNullSelectables();
        if (AttachedSelectable != null)
        {
            for (int i = 0; i < AttachedSelectable.Count; i++)
                EnsureBoomHeadAccessoryFacesCover(AttachedSelectable[i]);
        }
    }

    private void OnDestroy()
    {
        _isDestroyed = true;
        if (SelectedAttachmentPoint == this)
        {
            SelectedAttachmentPoint = null;
            SelectedAttachmentPointChanged?.Invoke();
        }
        ObjectMenu.ActiveStateChanged.RemoveListener(EndHoverStateIfHovered);
        ParentSelectables.ForEach(item =>
        {
            item.MouseOverStateChanged -= MouseOverStateChanged;
        });
        Selectable.SelectionChanged -= SelectionChanged;

        if (HoveredAttachmentPoint == this) 
        {
            HoveredAttachmentPoint = null;
            AttachmentPointHoverStateChanged?.Invoke(this,null);
        }
        AttachedSelectable.Clear();
    }

    private void OnMouseEnter()
    {
        if (GizmoHandler.GizmoBeingUsed || InputHandler.IsPointerOverUIElement()) return;
        HoveredAttachmentPoint = this;
        AttachmentPointHoverStateChanged?.Invoke(this, EventArgs.Empty);
        _attachmentPointHovered = true;
        UpdateComponentStatus();
    }

    private void OnMouseExit()
    {
        if (GizmoHandler.GizmoBeingUsed || InputHandler.IsPointerOverUIElement()) return;
        EndHoverState();
    }

    private void OnMouseUpAsButton()
    {
        if (GizmoHandler.GizmoBeingUsed || InputHandler.IsPointerOverUIElement()) return;
        // Prevent click if not MultiAttach and already has an attached object
        if (!MultiAttach && AttachedSelectable.Count > 0)
        {
            return;
        }
        AttachmentPointClicked?.Invoke(this, EventArgs.Empty);
        SelectedAttachmentPoint = this;
        SelectedAttachmentPointChanged?.Invoke();
        UpdateComponentStatus();

        if (SceneManager.GetActiveScene().name != "ObjectEditor")
            ObjectMenu.Open(this);
    }
    #endregion

    public void SetAttachedSelectable(Selectable selectable)
    {
        if (selectable == null || AttachedSelectable.Contains(selectable))
            return;
        AttachedSelectable.Add(selectable);
        // Subscribe to SelectableDestroyed event
        selectable.SelectableDestroyed.AddListener(() => OnAttachedSelectableDestroyed(selectable));
        EndHoverStateIfHovered();
        UpdateComponentStatus();
        RefreshSolidCoverPlateVisibility();
        EnsureBoomHeadAccessoryFacesCover(selectable);
    }

    private void OnAttachedSelectableDestroyed(Selectable selectable)
    {
        if (AttachedSelectable.Contains(selectable))
        {
            AttachedSelectable.Remove(selectable);
            AttachedSelectable.TrimExcess();
            RemoveNullSelectables();
            UpdateComponentStatus();
            RefreshSolidCoverPlateVisibility();
        }
    }

    /// <summary>
    /// Boom-head HV/LV plates are solid meshes with no cutouts. When any AP under
    /// that attachment has accessories, hide the plate so outlets are visible in
    /// the live room and elevation.
    /// </summary>
    void RefreshSolidCoverPlateVisibility()
    {
        Transform t = transform;
        while (t != null)
        {
            if (t.name.StartsWith("BoomHeadAttachment_", StringComparison.Ordinal) &&
                t.TryGetComponent(out MeshRenderer cover))
            {
                bool populated = false;
                foreach (var ap in t.GetComponentsInChildren<AttachmentPoint>(true))
                {
                    if (ap == null)
                        continue;
                    ap.RemoveNullSelectables();
                    if (ap.AttachedSelectable != null && ap.AttachedSelectable.Count > 0)
                    {
                        populated = true;
                        break;
                    }
                }
                cover.enabled = !populated;
                return;
            }
            t = t.parent;
        }
    }

    /// <summary>
    /// CDN/local outlet meshes often face into the head (elev logs: cover towardCam≈+0.7,
    /// GasOutletPlate towardCam≈−0.4, cull Back). Flip once so the accessory face matches
    /// the cover outward normal — attach/load contract, not elevation capture.
    /// </summary>
    void EnsureBoomHeadAccessoryFacesCover(Selectable accessory)
    {
        if (accessory == null)
            return;

        MeshRenderer cover = FindBoomHeadCoverRenderer();
        if (cover == null)
            return;

        MeshRenderer face = FindPreferredOutletFaceRenderer(accessory);
        if (face == null)
            return;

        Vector3 coverOut = AverageWorldNormal(cover);
        Vector3 faceOut = AverageWorldNormal(face);
        if (coverOut.sqrMagnitude < 1e-6f || faceOut.sqrMagnitude < 1e-6f)
            return;

        float coverVsFace = Vector3.Dot(faceOut.normalized, coverOut.normalized);
        if (coverVsFace >= 0f)
        {
            Debug.Log(
                $"[ElevOutletDiag] AttachmentPoint face-ok '{accessory.name}' " +
                $"coverVsFaceDot={coverVsFace:F3}",
                accessory);
            return;
        }

        Transform meshRoot = face.transform;
        while (meshRoot.parent != null && meshRoot.parent != accessory.transform)
            meshRoot = meshRoot.parent;

        meshRoot.Rotate(0f, 180f, 0f, Space.Self);

        float after = Vector3.Dot(AverageWorldNormal(face).normalized, coverOut.normalized);
        Debug.Log(
            $"[ElevOutletDiag] AttachmentPoint face-fix '{accessory.name}' " +
            $"coverVsFaceDot {coverVsFace:F3} -> {after:F3} (meshRoot={meshRoot.name})",
            accessory);
    }

    MeshRenderer FindBoomHeadCoverRenderer()
    {
        Transform t = transform;
        while (t != null)
        {
            if (t.name.StartsWith("BoomHeadAttachment_", StringComparison.Ordinal) &&
                t.TryGetComponent(out MeshRenderer cover))
                return cover;
            t = t.parent;
        }
        return null;
    }

    static MeshRenderer FindPreferredOutletFaceRenderer(Selectable accessory)
    {
        MeshRenderer idPlate = null;
        MeshRenderer fallback = null;
        foreach (var mr in accessory.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null || !mr.enabled)
                continue;
            if (mr.gameObject.name.StartsWith("Sphere", StringComparison.Ordinal))
                continue;

            if (mr.sharedMaterials != null)
            {
                foreach (var mat in mr.sharedMaterials)
                {
                    if (mat != null && mat.name.StartsWith("GasOutletPlate_", StringComparison.Ordinal))
                    {
                        idPlate = mr;
                        break;
                    }
                }
            }
            if (idPlate != null)
                break;
            if (fallback == null)
                fallback = mr;
        }
        return idPlate != null ? idPlate : fallback;
    }

    static Vector3 AverageWorldNormal(MeshRenderer mr)
    {
        var mf = mr.GetComponent<MeshFilter>();
        var mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null || mesh.normals == null || mesh.normals.Length == 0)
            return mr.transform.forward;

        var norms = mesh.normals;
        var acc = Vector3.zero;
        int step = Mathf.Max(1, norms.Length / 64);
        int n = 0;
        for (int i = 0; i < norms.Length; i += step)
        {
            acc += mr.transform.TransformDirection(norms[i]);
            n++;
        }
        return n > 0 ? acc / n : mr.transform.forward;
    }

    public void MarkParentNormalized() => _hasNormalizedParent = true;

    public void DetachSelectable(Selectable selectable)
    {
        if (_isDestroyed) return;
        AttachedSelectable.Remove(selectable);
        AttachedSelectable.TrimExcess();
        RemoveNullSelectables(); // Clean up after detach
        SetToOriginalParent();
        UpdateComponentStatus();
        RefreshSolidCoverPlateVisibility();
    }

    private void RemoveNullSelectables()
    {
        AttachedSelectable.RemoveAll(item => item == null);
        AttachedSelectable.TrimExcess();
    }

    /// <summary>Drops Unity-destroyed entries so save/load can safely read the list.</summary>
    public void PurgeDestroyedAttachedSelectables() => RemoveNullSelectables();
    //Anwar Edits
    public void SetToOriginalParent()
    {
        if (!MoveUpOnAttach || _originalParent == null) return;

        ScaleAuditLog.Event("AP.SetToOriginalParent",
            $"begin path={name} hasNormalized={_hasNormalizedParent} " +
            $"origParent={_originalParent.name}");
        ReparentPreservingWorldPoseAndScale(_originalParent);
        // Allow a later SetToProperParent / MoveUp (e.g. after config/room save).
        _hasNormalizedParent = false;

        // Store original local transform if not initialized
        if (!_hasBeenInitialized)
        {
            _originalLocalPosition = transform.localPosition;
            _originalLocalRotation = transform.localRotation;
            _hasBeenInitialized = true;
        }
    }
    /// <summary>
    /// Runtime entry point. During room load this waits until loading finishes so callers
    /// that fire from Start/Awake don't MoveUp before saved transforms are applied.
    /// Room load itself must call <see cref="ApplyProperParentImmediate"/> synchronously
    /// after transforms are restored — otherwise settle runs in the pre-MoveUp tree and
    /// the deferred MoveUp freezes wrong world poses.
    /// </summary>
    public async void SetToProperParent()
    {
        while (ConfigurationManager.IsLoading)
        {
            await Task.Yield();
            if (!Application.isPlaying) throw new AppQuitInTaskException();
        }

        ApplyProperParentImmediate();
    }

    /// <summary>
    /// Synchronously promote this AP when <see cref="MoveUpOnAttach"/> is set.
    /// Safe to call during load after saved transforms have been applied.
    /// </summary>
    public void ApplyProperParentImmediate()
    {
        if (!MoveUpOnAttach || _hasNormalizedParent) return;

        // Find parent attachment point
        Transform current = transform.parent;
        AttachmentPoint parentAP = null;

        while (current != null && parentAP == null)
        {
            parentAP = current.GetComponent<AttachmentPoint>();
            if (parentAP == null) current = current.parent;
        }

        if (parentAP == null)
        {
            ScaleAuditLog.Event("AP.ApplyProperParent",
                $"skip no-parentAP path={name} curParent={(transform.parent != null ? transform.parent.name : "null")}");
            return;
        }

        Transform targetParent = parentAP.transform.parent;
        if (targetParent == null)
        {
            ScaleAuditLog.Event("AP.ApplyProperParent",
                $"skip null-targetParent path={name} parentAP={parentAP.name}");
            return;
        }

        ScaleAuditLog.Event("AP.ApplyProperParent",
            $"begin path={name} parentAP={parentAP.name} targetParent={targetParent.name}");
        ReparentPreservingWorldPoseAndScale(targetParent);
        _hasNormalizedParent = true;
    }

    /// <summary>
    /// Reparent while keeping world position, rotation, and lossy scale.
    /// Critical for Z-scaled tubes: under-tube inverse Z must not escape as squash
    /// after MoveUp, and returning under a tube must not drop compensation.
    /// </summary>
    private void ReparentPreservingWorldPoseAndScale(Transform newParent)
    {
        Vector3 worldPos = transform.position;
        Quaternion worldRot = transform.rotation;
        Vector3 worldScale = transform.lossyScale;

        ScaleAuditLog.ReparentBegin("AP.Reparent", transform, newParent, worldScale);

        transform.SetParent(newParent, false);
        transform.position = worldPos;
        transform.rotation = worldRot;
        SetWorldScale(transform, worldScale);

        ScaleAuditLog.ReparentEnd("AP.Reparent", transform, worldScale);
    }

    /// <summary>
    /// Set localScale so <paramref name="t"/>'s lossy/world scale matches
    /// <paramref name="worldScale"/>. Rotation-safe replacement for axis guesses /
    /// InverseTransformVector when isolating children under a length-scaled parent.
    /// </summary>
    public static void SetWorldScale(Transform t, Vector3 worldScale)
    {
        if (t == null) return;
        t.localScale = Vector3.one;
        Vector3 parentLossyAtOne = t.lossyScale;
        // Full XYZ: length stretch on a rotated mesh parent often lands on X/Y in the AP's
        // local frame. Forcing XY=1 left downstream at lossy (stretch,1,1/z).
        Vector3 computedLocal = new Vector3(
            SafeDiv(worldScale.x, parentLossyAtOne.x),
            SafeDiv(worldScale.y, parentLossyAtOne.y),
            SafeDiv(worldScale.z, parentLossyAtOne.z));
        ScaleAuditLog.Event("AP.SetWorldScale",
            $"name={t.name} targetWorld={Fmt(worldScale)} parentLossyAtLocalOne={Fmt(parentLossyAtOne)} " +
            $"computedLocal={Fmt(computedLocal)}");
        t.localScale = computedLocal;
    }

    private static string Fmt(Vector3 v) =>
        $"({v.x:G6},{v.y:G6},{v.z:G6})";

    private static float SafeDiv(float numerator, float denominator)
    {
        float d = Mathf.Abs(denominator) < 1e-8f ? 1f : denominator;
        float result = numerator / d;
        if (float.IsNaN(result) || float.IsInfinity(result))
            return 1f;
        return result;
    }

    // Add method to reset to original local transform
    public void ResetToOriginalTransform()
    {
        if (!_hasBeenInitialized) return;
        
        transform.localPosition = _originalLocalPosition;
        transform.localRotation = _originalLocalRotation;
    }

    private void EndHoverState()
    {
        HoveredAttachmentPoint = null;
        AttachmentPointHoverStateChanged?.Invoke(this, EventArgs.Empty);
        _attachmentPointHovered = false;
        UpdateComponentStatus();
    }

    private void EndHoverStateIfHovered()
    {
        if (HoveredAttachmentPoint == this)
        {
            EndHoverState();
        }
    }

    private void SelectionChanged()
    {
        if (AreAnyParentSelectablesSelected)
            UpdateComponentStatus();

        if (Selectable.SelectedSelectables.Count > 0)
        {
            SelectedAttachmentPoint = null;
            SelectedAttachmentPointChanged?.Invoke();
        }
    }

    private void MouseOverStateChanged(object sender, EventArgs e)
    {
        UpdateComponentStatus();
    }

    private void UpdateComponentStatus()
    {
        RemoveNullSelectables(); // Ensure no nulls before updating status
        int multiAllowed = MultiAttach ? MultiLimit : 0;
        bool isMouseOverAnyParentSelectable = ParentSelectables.Any(item => item != null && item.IsMouseOver);
        bool areAnyParentSelectablesSelected = AreAnyParentSelectablesSelected;
        bool showInteractable = AttachedSelectable.Count <= multiAllowed && !areAnyParentSelectablesSelected;

        // Never draw AP placeholder meshes / hover FX into elevation or PDF captures.
        if (Selectable.IsInElevationPhotoMode)
        {
            if (Renderer != null)
                Renderer.enabled = false;
            if (HighlightHovered != null)
                HighlightHovered.highlighted = false;
            if (_collider == null)
                _collider = GetComponentInChildren<Collider>(true);
            if (_collider != null)
                _collider.enabled = false;
            StatusUpdated?.Invoke(AttachedSelectable.Count > 0);
            return;
        }

        if (Renderer != null)
            Renderer.enabled = (isMouseOverAnyParentSelectable || _attachmentPointHovered) && showInteractable;
        if (HighlightHovered != null)
            HighlightHovered.highlighted = _attachmentPointHovered && showInteractable;

        // Collider may be missing on some AP meshes, or stripped/restored during load optimization.
        if (_collider == null)
            _collider = GetComponentInChildren<Collider>(true);
        if (_collider != null)
            _collider.enabled = showInteractable;

        StatusUpdated?.Invoke(AttachedSelectable.Count > 0);
    }

    public void RefreshStatusForLoad()
    {
        RemoveNullSelectables(); // Clean up after loading
        if (_collider == null)
            _collider = GetComponentInChildren<Collider>(true);
        UpdateComponentStatus();
    }

    private void EmptyNullList()
    {
        RemoveNullSelectables(); // Always clean up nulls
        if (AttachedSelectable.Count == 1)
        {
            if (AttachedSelectable[0] == null)
            {
                AttachedSelectable.Clear();
                AttachedSelectable.TrimExcess();
            }
        }
    }
}

/// <summary>
/// How this attach point relates to the parent part's length / joints.
/// </summary>
public enum AttachPointRole
{
    Unspecified = 0,
    /// <summary>Distal end of a measurable length part — next solid should hang here.</summary>
    LengthTip = 1,
    /// <summary>Hinge / joint socket (often short local Z and tilted).</summary>
    Joint = 2,
    /// <summary>Mount / flange face — not a length tip.</summary>
    Mount = 3,
}

[Serializable]
public class AttachmentPointMetaData
{
    [field: SerializeField, ReadOnly]
    public string Guid 
    { get; set; }

    [field: SerializeField] 
    public List<string> AllowedSelectableCategories 
    { get; set; } = new();

    [field: SerializeField] 
    public List<string> AllowedSelectableAssetBundleNames 
    { get; set; } = new();
}