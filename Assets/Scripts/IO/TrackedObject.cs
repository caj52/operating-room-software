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
        public List<string> materialNames;
        public string keepRelativePositionParentName;

        // Add separate local and world transforms
        public Vector3 localPosition;    // New
        public Quaternion localRotation; // New
        public Vector3 worldPosition;    // New
        public Quaternion worldRotation; // New
        public Vector3 localScale;
        
        // Add parent tracking
        public string parentGuid;        // New
        public string parentPath;        // New
        public bool isAttachmentPoint;   // New
        public Vector3 originalLocalPosition; // New - for attachment points
        public Quaternion originalLocalRotation; // New - for attachment points
        public string attachedObject;
        // Price data
        public string sheetName;
        public string UIObjectName;
        public string size;
        public string priceObjectName;

        // New: lifecycle & components state
        public bool activeSelf; // New - GameObject active state
        public List<SaveUtility.ComponentEnabledState> componentEnabledStates; // New - enabled flags for components
        public List<SaveUtility.SerializedComponentState> componentStates; // New - custom component snapshots
    }

    private void Awake()
    {
        // Tracked object requires at least one of these
        // components or things will break
        if (!gameObject.TryGetComponent<Selectable>(out var _) &&
        !gameObject.TryGetComponent<AttachmentPoint>(out var _))
        {
            Debug.LogWarning($"TrackedObject component is on " +
            $"GameObject {gameObject.name} without either " +
            $"Selectable or AttachmentPoint. This is not " +
            $"allowed and the TrackedObject component will " +
            $"now be destroyed");

            Destroy(this);
        }
    }

    [NonSerialized]
    public Data data;
    
    private Vector3 _originalLocalPosition;    // New
    private Quaternion _originalLocalRotation; // New
    private bool _hasStoredOriginalTransform;  // New

    /// <summary>
    /// Used when saving
    /// </summary>
    public Data GetData()
    {
        data.objectName = gameObject.name;
        GetGUIDs();

        // Allow hooks to prepare before capture
        try
        {
            foreach (var hook in GetComponents<MonoBehaviour>().OfType<SaveUtility.ISaveHooks>())
            {
                try { hook.OnBeforeSave(); }
                catch (Exception ex) { Debug.LogWarning($"[TrackedObject] OnBeforeSave threw on {name}: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TrackedObject] Failed iterating save hooks on {name}: {ex.Message}");
        }

        // Store both local and world transforms
        data.localPosition = transform.localPosition;
        data.localRotation = transform.localRotation;
        data.worldPosition = transform.position;
        data.worldRotation = transform.rotation;
        data.localScale = transform.localScale;

        // Store parent info
        if (transform.parent != null)
        {
            data.parentPath = ConfigurationManager.GetGameObjectPath(transform.parent.gameObject);
            var parentTracked = transform.parent.GetComponent<TrackedObject>();
            if (parentTracked != null)
            {
                data.parentGuid = parentTracked.data.instance_guid;
            }
        }

        // Handle attachment points
        if (gameObject.TryGetComponent<AttachmentPoint>(out var ap))
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

        // Store component data
        if (gameObject.TryGetComponent(out Selectable s))
        {
            data.UIButtonname = s.UIButtonName;
            if (s.ScaleLevels.Count() == 0) data.scaleLevel = null;
            else
                data.scaleLevel = s.CurrentScaleLevel;
        }

        if (gameObject.TryGetComponent(out MaterialPalette palette))
        {
            data.materialNames = new List<string>();
            Material[] materials = palette.meshRenderer.materials;

            foreach (Material material in materials)
            {
                data.materialNames.Add(material.name);
            }
        }

        // Store price data
        if (gameObject.TryGetComponent(out SelectablePrice sp))
        {
            data.sheetName = sp.sheetName;
            data.UIObjectName = sp.UIObjectName;
            data.size = sp.Size;
            data.priceObjectName = sp.pricingObjectName;
        }

        // New: capture GameObject active state and component enabled state
        data.activeSelf = gameObject.activeSelf;
        try
        {
            data.componentEnabledStates = SaveUtility.CaptureEnabledStates(gameObject);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TrackedObject] Failed to capture enabled states on {name}: {ex.Message}");
        }

        // New: capture custom component snapshots (if any)
        try
        {
            data.componentStates = SaveUtility.CaptureCustomComponentStates(gameObject);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TrackedObject] Failed to capture custom component states on {name}: {ex.Message}");
        }

        return data;
    }

    public Selectable.ScaleLevel GetScaleLevel()
    {
        if (data.scaleLevel == null) return null;

        List<Selectable.ScaleLevel> scales = GetComponent<Selectable>().ScaleLevels;

        return scales.First(x => x.Size == data.scaleLevel.Size);
    }

    public Vector3 GetPosition()
    {
        return data.localPosition;
    }

    public Quaternion GetRotation()
    {
        return data.localRotation ;
    }

    public Vector3 GetScale()
    {
        return data.localScale;
    }

    public List<string> GetMaterials()
    {
        return data.materialNames;
    }

    public void StoreValues(TrackedObject.Data d)
    {
        data = d;

        // Store original transform for attachment points
        if (data.isAttachmentPoint && !_hasStoredOriginalTransform)
        {
            _originalLocalPosition = data.originalLocalPosition;
            _originalLocalRotation = data.originalLocalRotation;
            _hasStoredOriginalTransform = true;
        }

        // Handle price data
        if (!string.IsNullOrEmpty(d.sheetName) && gameObject.TryGetComponent(out SelectablePrice sp))
        {
            sp.sheetName = d.sheetName;
            sp.UIObjectName = d.UIObjectName;
            sp.Size = d.size;
            sp.pricingObjectName = d.priceObjectName;
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

        // Special handling for attachment points
        if (data.isAttachmentPoint && gameObject.TryGetComponent<AttachmentPoint>(out var ap))
        {
            // When loading from file, preserve exact saved transform; only apply original pose in runtime adjustments
            if (ap.MoveUpOnAttach && !ConfigurationManager.IsLoading)
            {
                transform.localPosition = _originalLocalPosition;
                transform.localRotation = _originalLocalRotation;
            }
        }
    }

    /// <summary>
    /// Apply saved active/enabled states and any custom component snapshots.
    /// Call this after parenting and transform restoration.
    /// </summary>
    public void ApplySavedState()
    {
        // GameObject active state
        try
        {
            if (data.activeSelf)
                gameObject.SetActive(true); // ensure activation sequence
            else
                gameObject.SetActive(false);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TrackedObject] Failed to set active state on {name}: {ex.Message}");
        }

        // Enabled flags
        try
        {
            SaveUtility.RestoreEnabledStates(gameObject, data.componentEnabledStates);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TrackedObject] Failed to restore enabled states on {name}: {ex.Message}");
        }

        // Custom component data
        try
        {
            SaveUtility.RestoreCustomComponentStates(gameObject, data.componentStates);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TrackedObject] Failed to restore custom component states on {name}: {ex.Message}");
        }

        // Hooks
        try
        {
            foreach (var hook in GetComponents<MonoBehaviour>().OfType<SaveUtility.ISaveHooks>())
            {
                try { hook.OnAfterLoad(); }
                catch (Exception ex) { Debug.LogWarning($"[TrackedObject] OnAfterLoad threw on {name}: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TrackedObject] Failed iterating load hooks on {name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Used when saving
    /// </summary>
    void GetGUIDs()
    {
        if (gameObject.TryGetComponent(out Selectable s))
        {
            if (s.guid != null)
            {
                data.instance_guid = s.guid.ToString();
            }

            data.global_guid = s.GUID;

            if (s.ParentAttachmentPoint != null) // This selectable is a child of a configuration, assign AttachmentPoint guid to it's parent ref
            {
                data.parent = ConfigurationManager.GetGameObjectPath(s.ParentAttachmentPoint.gameObject);
            }
            else if (s.AttachedTo != null)
            {
                data.parent = s.AttachedTo.gameObject.name;
                data.attachedTo = data.parent;
            }
            else if (gameObject.transform != gameObject.transform.root)
            {
                data.parent = ConfigurationManager.GetGameObjectPath(this.gameObject);
            }

            if (gameObject.TryGetComponent<KeepRelativePosition>(out var krp) &&
                krp.VirtualParent != null)
            {
                data.keepRelativePositionParentName = krp.VirtualParent.name;
            }
        }
        else
        {
            AttachmentPoint ap = gameObject.GetComponent<AttachmentPoint>();
            data.global_guid = ap.GUID;
            if (ap.AttachedSelectable.Count>0)
            {
                data.attachedObject = ap.AttachedSelectable.FirstOrDefault().name;
            }
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
        else
        {
            return false;
        }
    }
}