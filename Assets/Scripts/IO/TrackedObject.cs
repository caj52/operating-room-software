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
        // Legacy schema (pre local/world split). Read on load only — never written.
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 scale;
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
        public bool isBoomObject;
        /// <summary>True when <see cref="isBoomObject"/> was written by a build that persists it.</summary>
        public bool hasIsBoomObject;
        // New: lifecycle & components state
        public bool activeSelf;
        public List<SaveUtility.ComponentEnabledState> componentEnabledStates;
        public List<SaveUtility.SerializedComponentState> componentStates;
        // New: full self path for reliable lookup post-instantiation
        public string selfPath; // NEW

        public bool ShouldSerializepos() => false;
        public bool ShouldSerializerot() => false;
        public bool ShouldSerializescale() => false;

        /// <summary>
        /// Maps old pos/rot/scale into modern fields when a save predates the local/world split.
        /// </summary>
        public void ApplyLegacyFields()
        {
            if (!HasValidRotation(localRotation) && HasValidRotation(rot))
            {
                worldPosition = pos;
                worldRotation = rot;
            }

            if (localScale.sqrMagnitude >= 1e-8f)
                return;

            if (scale.sqrMagnitude >= 1e-8f)
                localScale = scale;
            else if (scaleLevel != null && scaleLevel.ScaleZ > 1e-4f)
                localScale = new Vector3(1f, 1f, scaleLevel.ScaleZ);
            else
                localScale = Vector3.one;
        }

        private static bool HasValidRotation(Quaternion q) =>
            q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 1e-8f;
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
    // Data is a struct — default(Data) has Quaternion(0,0,0,0) and Vector3.zero, which would
    // zero out scale/rotation if RestoreTransform ran before StoreValues was ever called
    // (e.g. dynamically-created child rows/panels that have no row in the save file).
    [NonSerialized] public bool HasStoredValues;
    /// <summary>
    /// Stable per-instance id for save/load parenting. Never used as the GameObject name
    /// for attached boom accessories (names drive outlet detection / cover plates).
    /// </summary>
    [NonSerialized] string _runtimeInstanceId;
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
        bool logScale = gameObject.GetComponent<AttachmentPoint>() != null
            || IsScaleRelevantName(name)
            || IsNonUniformScale(data.localScale)
            || (TryGetComponent(out Selectable selForLog) && selForLog.ScaleLevels != null && selForLog.ScaleLevels.Count > 0);
        if (logScale)
        {
            ScaleAuditLog.Event("Tracked.GetData",
                $"name={name} captureLocal={data.localScale} lossy={transform.lossyScale} " +
                $"parent={(transform.parent != null ? transform.parent.name : "null")} " +
                $"parentLocal={(transform.parent != null ? transform.parent.localScale.ToString() : "n/a")} " +
                $"parentLossy={(transform.parent != null ? transform.parent.lossyScale.ToString() : "n/a")}");
        }
        if (transform.parent != null)
        {
            data.parentPath = ConfigurationManager.GetGameObjectPath(transform.parent.gameObject);
            var parentTracked = transform.parent.GetComponent<TrackedObject>();
            // Only write parentGuid when the parent actually participates in id parenting
            // (catalog placeable or attach slot). Embedded parents stay path-only.
            if (parentTracked != null && ParentOffersRuntimeInstanceId(parentTracked))
                data.parentGuid = parentTracked.EnsureRuntimeInstanceId();
            else
                data.parentGuid = null;
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
            data.isBoomObject = sp.isBoomObject;
            data.hasIsBoomObject = true;
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
        d.ApplyLegacyFields();
        data = d;
        HasStoredValues = true;
        if (!string.IsNullOrEmpty(d.instance_guid))
            ApplyRuntimeInstanceId(d.instance_guid);
        if (data.isAttachmentPoint && !_hasStoredOriginalTransform)
        {
            _originalLocalPosition = data.originalLocalPosition;
            _originalLocalRotation = data.originalLocalRotation;
            _hasStoredOriginalTransform = true;
        }
        if (!string.IsNullOrEmpty(d.sheetName) && !string.IsNullOrEmpty(d.priceObjectName))
        {
            bool isBoom = d.hasIsBoomObject
                ? d.isBoomObject
                : PricingManager.InferIsBoomObject(d.sheetName, d.priceObjectName, d.UIObjectName);

            // Prefabs do not carry SelectablePrice — recreate from save identity on every load.
            if (PricingManager.Instance != null)
            {
                PricingManager.Instance.EnsurePricingFromIdentity(
                    gameObject,
                    d.sheetName,
                    d.priceObjectName,
                    d.UIObjectName,
                    d.size,
                    isBoom);
            }
            else if (gameObject.TryGetComponent(out SelectablePrice sp))
            {
                sp.sheetName = d.sheetName;
                sp.UIObjectName = d.UIObjectName;
                sp.Size = d.size;
                sp.pricingObjectName = d.priceObjectName;
                sp.isBoomObject = isBoom;
                sp.EnsurePricingDataLoaded(force: true);
            }
        }
        // Restore scale levels for Selectable (including embedded selectables)
        if (gameObject.TryGetComponent(out Selectable selectable))
        {
            if (d.scaleLevels != null && d.scaleLevels.Count > 0)
            {
                selectable.ScaleLevels = new List<Selectable.ScaleLevel>(d.scaleLevels);
                selectable.ScaleLevelsRestoredFromSave = true;
            }

            if (d.scaleLevel != null && selectable.ScaleLevels != null)
            {
                var scale = selectable.ScaleLevels.FirstOrDefault(x => x.Size == d.scaleLevel.Size)
                            ?? d.scaleLevel;
                if (scale != null)
                {
                    // Metadata only — never SetScaleLevel here. Transforms come from
                    // RestoreTransform; length isolation from FixLoaded → Reapply → SetScaleLevel.
                    selectable.RestoreScaleLevelFromSave(scale);
                    // Even when scaleLevels was empty/missing, a concrete saved scale must
                    // block InitializeAfterStart from resetting to ModelDefault.
                    selectable.ScaleLevelsRestoredFromSave = true;
                }
            }
        }
    }

    public void RestoreTransform(bool isRoot = false)
    {
        // Legacy saves only stored world pos/rot. Missing localRotation deserializes as
        // (0,0,0,0); treat that as "use world" so old configs load without re-saving.
        bool hasLocalRotation = data.localRotation.x * data.localRotation.x
            + data.localRotation.y * data.localRotation.y
            + data.localRotation.z * data.localRotation.z
            + data.localRotation.w * data.localRotation.w > 1e-8f;

        bool moveUpAP = IsMoveUpAttachmentPoint();
        bool usedWorldPose = isRoot || !hasLocalRotation || moveUpAP;
        if (usedWorldPose)
        {
            // A MoveUp AP's data.localPosition/localRotation were captured relative to
            // whatever parent held it at save time (its promoted "parked" parent). That
            // parent is not a stable reference frame: re-promotion later in the load can
            // land it at a world pose that differs by float noise from the exact save-time
            // instant, and every downstream joint in a chain would then compound that same
            // small local-vs-parent error. World pose is unambiguous regardless of which
            // parent currently holds it, and the later MoveUp promote
            // (AttachmentPoint.ApplyProperParentImmediate) reparents while preserving world
            // pose, so this lines up exactly with the interactive rotate/translate contract
            // (Selectable.BeginRigidPoseChange/EndRigidPoseChange). Always use world pose
            // for MoveUp APs — promoted or not — never their local pose.
            transform.position = data.worldPosition;
            transform.rotation = data.worldRotation;
        }
        else
        {
            transform.localPosition = data.localPosition;
            transform.localRotation = data.localRotation;
        }
        // Struct default / legacy JSON without localScale deserializes as (0,0,0).
        // Never write that onto the hierarchy — it collapses meshes and attach chains.
        Vector3 scale = data.localScale;
        if (scale.sqrMagnitude < 1e-8f)
        {
            if (data.scaleLevel != null && data.scaleLevel.ScaleZ > 1e-4f)
                scale = new Vector3(1f, 1f, data.scaleLevel.ScaleZ);
            else
                scale = Vector3.one;
        }
        transform.localScale = scale;
        if (data.isAttachmentPoint || IsScaleRelevantName(name) || IsNonUniformScale(scale))
        {
            ScaleAuditLog.Event("Tracked.RestoreTransform",
                $"name={name} isRoot={isRoot} isAP={data.isAttachmentPoint} " +
                $"appliedLocal={scale} savedLocal={data.localScale} " +
                $"scaleLevelZ={(data.scaleLevel != null ? data.scaleLevel.ScaleZ.ToString("G6") : "null")} " +
                $"parent={(transform.parent != null ? transform.parent.name : "null")} " +
                $"lossyAfter={transform.lossyScale} " +
                $"poseMode={(moveUpAP ? "worldMoveUpAP" : usedWorldPose ? "worldRootOrLegacy" : "local")} " +
                $"localEulerAfter={transform.localEulerAngles} worldEulerAfter={transform.eulerAngles} " +
                $"savedLocalEuler={data.localRotation.eulerAngles} savedWorldEuler={data.worldRotation.eulerAngles}");
        }
        if (data.isAttachmentPoint && gameObject.TryGetComponent<AttachmentPoint>(out var ap))
        {
            if (ap.MoveUpOnAttach && !ConfigurationManager.IsLoading)
            {
                transform.localPosition = _originalLocalPosition;
                transform.localRotation = _originalLocalRotation;
            }
        }
    }

    /// <summary>
    /// Re-apply saved local position/rotation without touching scale.
    /// Used after length-isolation re-derive so temporary parent Z/rotation thrash
    /// cannot leave arms slightly translated (e.g. tip sitting lower).
    /// </summary>
    public void RestoreLocalPoseKeepingScale()
    {
        if (!HasStoredValues)
            return;

        if (IsMoveUpAttachmentPoint())
        {
            transform.position = data.worldPosition;
            transform.rotation = data.worldRotation;
            ScaleAuditLog.Event("Tracked.RestoreLocalPoseKeepingScale",
                $"name={name} poseMode=worldMoveUpAP worldEulerAfter={transform.eulerAngles} " +
                $"savedWorldEuler={data.worldRotation.eulerAngles}");
            return;
        }

        bool hasLocalRotation = data.localRotation.x * data.localRotation.x
            + data.localRotation.y * data.localRotation.y
            + data.localRotation.z * data.localRotation.z
            + data.localRotation.w * data.localRotation.w > 1e-8f;
        if (!hasLocalRotation)
            return;

        transform.localPosition = data.localPosition;
        transform.localRotation = data.localRotation;
    }

    /// <summary>
    /// True for any MoveUp AttachmentPoint, promoted or not. <c>data.localPosition</c>/
    /// <c>data.localRotation</c> were captured relative to whatever parent held it at save
    /// time — its "parked" promoted parent, which does not exist yet if load hasn't
    /// promoted this AP, and which can land at a world pose that differs from the exact
    /// save-time instant by a small float/order-of-operations margin even once it is
    /// re-promoted. Either way, local coordinates are the wrong reference frame: world pose
    /// is the only value that is unambiguous and stable regardless of parent/promotion
    /// state, and the MoveUp promote (AttachmentPoint.ApplyProperParentImmediate) reparents
    /// while preserving world pose, so this always lines up with the interactive
    /// rotate/translate contract (Selectable.BeginRigidPoseChange/EndRigidPoseChange).
    /// </summary>
    private bool IsMoveUpAttachmentPoint()
    {
        return data.isAttachmentPoint
            && TryGetComponent<AttachmentPoint>(out var ap)
            && ap.MoveUpOnAttach;
    }

    private static bool IsNonUniformScale(Vector3 s) =>
        Mathf.Abs(s.x - s.y) > 0.02f || Mathf.Abs(s.y - s.z) > 0.02f || Mathf.Abs(s.x - s.z) > 0.02f;

    private static bool IsScaleRelevantName(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        return n.IndexOf("DropTube", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("AttachmentPoint", StringComparison.OrdinalIgnoreCase) >= 0
            || n.Equals("AttachPoint", StringComparison.OrdinalIgnoreCase)
            || n.IndexOf("ArmSegment", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Cardanic", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("BoomHead", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("LightHead", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void ApplySavedState()
    {
        // Active state is applied in one batch after all objects are restored (see ConfigurationManager.BatchActivateLoadedObjects).
        if (!ConfigurationManager.IsLoading)
        {
            try { gameObject.SetActive(data.activeSelf); }
            catch (Exception ex) { Debug.LogWarning($"[TrackedObject] Failed to set active state on {name}: {ex.Message}"); }
        }

        try { RestoreMaterials(); }
        catch (Exception ex) { Debug.LogWarning($"[TrackedObject] Failed to restore materials on {name}: {ex.Message}"); }
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

    /// <summary>Re-applies saved material names onto any MaterialPalette on this object.</summary>
    public void RestoreMaterials()
    {
        if (data.materialNames == null || data.materialNames.Count == 0)
            return;
        if (!TryGetComponent(out MaterialPalette palette))
            return;

        for (int i = 0; i < data.materialNames.Count; i++)
        {
            string modifiedName = data.materialNames[i].Replace(" (Instance)", "");
            palette.Assign(modifiedName, i);
        }
    }

    void GetGUIDs()
    {
        // Only mint/save runtime instance ids for catalog placeables and attach slots.
        // Embedded prefab parts (empty Selectable.GUID) stay path-resolved like before —
        // assigning them ids does not help parenting and pollutes the save.
        if (gameObject.TryGetComponent(out AttachmentPoint _))
            data.instance_guid = EnsureRuntimeInstanceId();
        else if (gameObject.TryGetComponent(out Selectable s0) &&
                 (!string.IsNullOrEmpty(s0.GUID) || !string.IsNullOrEmpty(s0.guid)))
            data.instance_guid = EnsureRuntimeInstanceId();
        else if (!string.IsNullOrEmpty(_runtimeInstanceId))
            data.instance_guid = _runtimeInstanceId;
        else
            data.instance_guid = null;

        if (gameObject.TryGetComponent(out Selectable s))
        {
            data.global_guid = s.GUID;
            if (s.ParentAttachmentPoint != null) data.parent = ConfigurationManager.GetGameObjectPath(s.ParentAttachmentPoint.gameObject);
            else if (s.AttachedTo != null) { data.parent = s.AttachedTo.gameObject.name; data.attachedTo = data.parent; }
            else if (gameObject.transform != gameObject.transform.root) data.parent = ConfigurationManager.GetGameObjectPath(this.gameObject);
            if (gameObject.TryGetComponent<KeepRelativePosition>(out var krp) && krp.VirtualParent != null) data.keepRelativePositionParentName = krp.VirtualParent.name;
        }
        else
        {
            AttachmentPoint ap = gameObject.GetComponent<AttachmentPoint>();
            if (ap == null)
            {
                data.parent = ConfigurationManager.GetGameObjectPath(gameObject);
                return;
            }

            // Place-spawned boom/service heads can leave destroyed entries in this list;
            // scrub before reading so room save cannot NRE mid-collect.
            ap.PurgeDestroyedAttachedSelectables();
            data.global_guid = ap.GUID;
            Selectable attached = ap.AttachedSelectable.FirstOrDefault(sel => sel != null);
            data.attachedObject = attached != null ? attached.name : null;
            data.parent = ConfigurationManager.GetGameObjectPath(gameObject);
        }
    }

    /// <summary>
    /// Ensures a stable instance id exists for save/load parenting without renaming the GO.
    /// </summary>
    public string EnsureRuntimeInstanceId()
    {
        if (!string.IsNullOrEmpty(_runtimeInstanceId))
        {
            data.instance_guid = _runtimeInstanceId;
            return _runtimeInstanceId;
        }

        if (TryGetComponent(out Selectable selectable) && !string.IsNullOrEmpty(selectable.guid))
        {
            _runtimeInstanceId = selectable.guid;
            data.instance_guid = _runtimeInstanceId;
            return _runtimeInstanceId;
        }

        if (!string.IsNullOrEmpty(data.instance_guid))
        {
            ApplyRuntimeInstanceId(data.instance_guid);
            return _runtimeInstanceId;
        }

        string id = Guid.NewGuid().ToString();
        ApplyRuntimeInstanceId(id);
        return _runtimeInstanceId;
    }

    /// <summary>
    /// Applies a saved/runtime instance id without renaming the GameObject.
    /// Assembly roots still rename via GenerateGuidName / load root naming — not here.
    /// </summary>
    public void ApplyRuntimeInstanceId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        _runtimeInstanceId = id;
        data.instance_guid = id;
        if (TryGetComponent(out Selectable selectable))
            selectable.guid = id;
    }

    public string GetRuntimeInstanceId() => _runtimeInstanceId;

    static bool ParentOffersRuntimeInstanceId(TrackedObject parent)
    {
        if (parent.GetComponent<AttachmentPoint>() != null)
            return true;
        if (parent.TryGetComponent(out Selectable s) &&
            (!string.IsNullOrEmpty(s.GUID) || !string.IsNullOrEmpty(s.guid)))
            return true;
        return !string.IsNullOrEmpty(parent.GetRuntimeInstanceId());
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