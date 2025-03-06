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

    [field: SerializeField] private MeshRenderer Renderer { get; set; }
    private Collider _collider; 
    private bool _isDestroyed;

    private bool AreAnyParentSelectablesSelected => 
        ParentSelectables.Any(x => Selectable.SelectedSelectables.Contains(x));

    #region Monobehaviour
    private void Awake()
    {
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

        var childSelectables = GetComponentsInChildren<Selectable>();
        if (childSelectables.Length > 0 && AttachedSelectable.Count == 0)
        {
            foreach (Selectable s in childSelectables)
            {
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
        SetToProperParent();
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
        AttachedSelectable.Add(selectable);
        EndHoverStateIfHovered();
        UpdateComponentStatus();
    }

    public void DetachSelectable(Selectable selectable)
    {
        if (_isDestroyed) return;
        AttachedSelectable.Remove(selectable);
        AttachedSelectable.TrimExcess();
        SetToOriginalParent();
        UpdateComponentStatus();
    }

    public void SetToOriginalParent()
    {
        if (MoveUpOnAttach)
        {
            transform.parent = _originalParent;
        }
    }

    public async void SetToProperParent()
    {
        while (ConfigurationManager.IsLoading) 
        {
            await Task.Yield();
            if (!Application.isPlaying)
                throw new AppQuitInTaskException();
        }

        if (MoveUpOnAttach)
        {
            Transform parent = transform.parent;
            AttachmentPoint attachmentPoint = parent.GetComponent<AttachmentPoint>();
            while (attachmentPoint == null)
            {
                parent = parent.parent;
                attachmentPoint = parent.GetComponent<AttachmentPoint>();
            }

            transform.parent = attachmentPoint.transform.parent;
        }
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
        int multiAllowed = MultiAttach ? MultiLimit : 0; // check if this is a multiattach point and use the limit, otherwise use 0 for default points. 

        bool isMouseOverAnyParentSelectable = ParentSelectables.FirstOrDefault(item => item.IsMouseOver) != default;
        bool areAnyParentSelectablesSelected = AreAnyParentSelectablesSelected;
        Renderer.enabled = (isMouseOverAnyParentSelectable || _attachmentPointHovered) && !areAnyParentSelectablesSelected && AttachedSelectable.Count <= multiAllowed;
        HighlightHovered.highlighted = _attachmentPointHovered && !areAnyParentSelectablesSelected && AttachedSelectable.Count <= multiAllowed;
        _collider.enabled = AttachedSelectable.Count <= multiAllowed && !areAnyParentSelectablesSelected;

        StatusUpdated?.Invoke(AttachedSelectable.Count > 0);
    }

    private void EmptyNullList()
    {
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