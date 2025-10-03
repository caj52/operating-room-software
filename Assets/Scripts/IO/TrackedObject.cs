using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.VirtualTexturing;

public class TrackedObject : MonoBehaviour
{
    [Serializable]
    public struct Data
    {
        public string objectName;
        public string UIButtonname;
        public string instance_guid;
        public string global_guid;
        public string parent;
        public string attachedTo;
        public Selectable.ScaleLevel scaleLevel;
        public List<Selectable.ScaleLevel> scaleLevels; // NEW: store all scale levels
        public List<string> materialNames;
        public string keepRelativePositionParentName;
        // Add separate local and world transforms
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 worldPosition;
        public Quaternion worldRotation;
        public Vector3 localScale;
        // Add parent tracking
        public string parentGuid;
        public string parentPath;
        public bool isAttachmentPoint;
        public Vector3 originalLocalPosition;
        public Quaternion originalLocalRotation;
        public string attachedObject;
        // Price data
        public string sheetName;
        public string UIObjectName;
        public string size;
        public string priceObjectName;
        // New: lifecycle & components state
        public bool activeSelf;
        public List<SaveUtility.ComponentEnabledState> componentEnabledStates;
        public List<SaveUtility.SerializedComponentState> componentStates;
        // New: full self path for reliable lookup post-instantiation
        public string selfPath; // NEW
    }

    private void Awake()
    {
        if (!gameObject.TryGetComponent<Selectable>(out var _) && !gameObject.TryGetComponent<AttachmentPoint>(out var _))
        {
            Debug.LogWarning($"TrackedObject component is on GameObject {gameObject.name} without either Selectable or AttachmentPoint. This is not allowed and the TrackedObject component will now be destroyed");
            Destroy(this);
        }
    }

    [NonSerialized] public Data data;
    private Vector3 _originalLocalPosition;
    private Quaternion _originalLocalRotation;
    private bool _hasStoredOriginalTransform;

    public Data GetData()
    {
        data.objectName = gameObject.name;
        GetGUIDs();
        // capture self path early (before potential renames on save)
        data.selfPath = ConfigurationManager.GetGameObjectPath(gameObject);
        try
        {
            foreach (var hook in GetComponents<MonoBehaviour>().OfType<SaveUtility.ISaveHooks>())
            {
                try { hook.OnBeforeSave(); }
                catch (Exception ex) { Debug.LogWarning($"[TrackedObject] OnBeforeSave threw on {name}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[TrackedObject] Failed iterating save hooks on {name}: {ex.Message}"); }
        data.localPosition = transform.localPosition;
        data.localRotation = transform.localRotation;
        data.worldPosition = transform.position;
        data.worldRotation = transform.rotation;
        data.localScale = transform.localScale;
        if (transform.parent != null)
        {
            data.parentPath = ConfigurationManager.GetGameObjectPath(transform.parent.gameObject);
            var parentTracked = transform.parent.GetComponent<TrackedObject>();
            data.parentGuid = parentTracked != null ? parentTracked.data.instance_guid : null;
        }
        else
        {
            data.parentPath = null;
            data.parentGuid = null;
        }
        // Only set isAttachmentPoint true if not root object
        if (gameObject.TryGetComponent<AttachmentPoint>(out var ap) && transform.parent != null)
        {
            data.isAttachmentPoint = true;
            if (!_hasStoredOriginalTransform)
            {
                _originalLocalPosition = transform.localPosition;
                _originalLocalRotation = transform.localRotation;
                _hasStoredOriginalTransform = true;
            }
            data.originalLocalPosition = _originalLocalPosition;
            data.originalLocalRotation = _originalLocalRotation;
        }
        else
        {
            data.isAttachmentPoint = false;
        }
        if (gameObject.TryGetComponent(out Selectable s))
        {
            data.UIButtonname = s.UIButtonName;
            data.scaleLevel = s.ScaleLevels.Count() == 0 ? null : s.CurrentScaleLevel;
            data.scaleLevels = s.ScaleLevels != null ? new List<Selectable.ScaleLevel>(s.ScaleLevels) : null; // NEW: store all scale levels
        }
        if (gameObject.TryGetComponent(out MaterialPalette palette))
        {
            data.materialNames = new List<string>();
            Material[] materials = palette.meshRenderer.materials;
            foreach (Material material in materials) data.materialNames.Add(material.name);
        }
        if (gameObject.TryGetComponent(out SelectablePrice sp))
        {
            data.sheetName = sp.sheetName;
            data.UIObjectName = sp.UIObjectName;
            data.size = sp.Size;
            data.priceObjectName = sp.pricingObjectName;
        }
        data.activeSelf = gameObject.activeSelf;
        try { data.componentEnabledStates = SaveUtility.CaptureEnabledStates(gameObject); }
        catch (Exception ex) { Debug.LogWarning($"[TrackedObject] Failed to capture enabled states on {name}: {ex.Message}"); }
        try { data.componentStates = SaveUtility.CaptureCustomComponentStates(gameObject); }
        catch (Exception ex) { Debug.LogWarning($"[TrackedObject] Failed to capture custom component states on {name}: {ex.Message}"); }
        return data;
    }

    public Selectable.ScaleLevel GetScaleLevel()
    {
        if (data.scaleLevel == null) return null;
        List<Selectable.ScaleLevel> scales = GetComponent<Selectable>().ScaleLevels;
        return scales.First(x => x.Size == data.scaleLevel.Size);
    }
    public Vector3 GetPosition() => data.localPosition;
    public Quaternion GetRotation() => data.localRotation;
    public Vector3 GetScale() => data.localScale;
    public List<string> GetMaterials() => data.materialNames;

    public void StoreValues(TrackedObject.Data d)
    {
        data = d;
        if (data.isAttachmentPoint && !_hasStoredOriginalTransform)
        {
            _originalLocalPosition = data.originalLocalPosition;
            _originalLocalRotation = data.originalLocalRotation;
            _hasStoredOriginalTransform = true;
        }
        if (!string.IsNullOrEmpty(d.sheetName) && gameObject.TryGetComponent(out SelectablePrice sp))
        {
            sp.sheetName = d.sheetName; sp.UIObjectName = d.UIObjectName; sp.Size = d.size; sp.pricingObjectName = d.priceObjectName;
        }
        // Restore scale levels for Selectable (including embedded selectables)
        if (gameObject.TryGetComponent(out Selectable selectable))
        {
            if (d.scaleLevels != null && d.scaleLevels.Count > 0)
            {
                selectable.ScaleLevels = new List<Selectable.ScaleLevel>(d.scaleLevels);
                selectable.ScaleLevelsRestoredFromSave = true;
            }
            if (d.scaleLevel != null)
            {
                var scale = selectable.ScaleLevels?.FirstOrDefault(x => x.Size == d.scaleLevel.Size);
                if (scale != null)
                {
                    selectable.SetScaleLevel(scale, true, false);
                }
            }
        }
    }

    public void RestoreTransform(bool isRoot = false)
    {
        if (isRoot)
        {
            transform.position = data.worldPosition;
            transform.rotation = data.worldRotation;
        }
        else
        {
            transform.localPosition = data.localPosition;
            transform.localRotation = data.localRotation;
        }
        transform.localScale = data.localScale;
        if (data.isAttachmentPoint && gameObject.TryGetComponent<AttachmentPoint>(out var ap))
        {
            if (ap.MoveUpOnAttach && !ConfigurationManager.IsLoading)
            {
                transform.localPosition = _originalLocalPosition;
                transform.localRotation = _originalLocalRotation;
            }
        }
    }

    public void ApplySavedState()
    {
        try { if (data.activeSelf) gameObject.SetActive(true); else gameObject.SetActive(false); }
        catch (Exception ex) { Debug.LogWarning($"[TrackedObject] Failed to set active state on {name}: {ex.Message}"); }
        try { SaveUtility.RestoreEnabledStates(gameObject, data.componentEnabledStates); }
        catch (Exception ex) { Debug.LogWarning($"[TrackedObject] Failed to restore enabled states on {name}: {ex.Message}"); }
        try { SaveUtility.RestoreCustomComponentStates(gameObject, data.componentStates); }
        catch (Exception ex) { Debug.LogWarning($"[TrackedObject] Failed to restore custom component states on {name}: {ex.Message}"); }
        try
        {
            foreach (var hook in GetComponents<MonoBehaviour>().OfType<SaveUtility.ISaveHooks>())
            {
                try { hook.OnAfterLoad(); }
                catch (Exception ex) { Debug.LogWarning($"[TrackedObject] OnAfterLoad threw on {name}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[TrackedObject] Failed iterating load hooks on {name}: {ex.Message}"); }
    }

    void GetGUIDs()
    {
        if (gameObject.TryGetComponent(out Selectable s))
        {
            if (s.guid != null) data.instance_guid = s.guid.ToString();
            data.global_guid = s.GUID;
            if (s.ParentAttachmentPoint != null) data.parent = ConfigurationManager.GetGameObjectPath(s.ParentAttachmentPoint.gameObject);
            else if (s.AttachedTo != null) { data.parent = s.AttachedTo.gameObject.name; data.attachedTo = data.parent; }
            else if (gameObject.transform != gameObject.transform.root) data.parent = ConfigurationManager.GetGameObjectPath(this.gameObject);
            if (gameObject.TryGetComponent<KeepRelativePosition>(out var krp) && krp.VirtualParent != null) data.keepRelativePositionParentName = krp.VirtualParent.name;
        }
        else
        {
            AttachmentPoint ap = gameObject.GetComponent<AttachmentPoint>();
            data.global_guid = ap.GUID;
            if (ap.AttachedSelectable.Count > 0) data.attachedObject = ap.AttachedSelectable.FirstOrDefault().name;
            data.parent = ConfigurationManager.GetGameObjectPath(gameObject);
        }
    }

    public bool IsDecal()
    {
        if (!string.IsNullOrEmpty(data.attachedTo) || GetComponent<Selectable>().AttachedTo != null)
        {
            data.attachedTo = "";
            return true;
        }
        else return false;
    }
}