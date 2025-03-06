using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TrackedObject : MonoBehaviour
{
    [Serializable]
    public struct Data
    {
        public string objectName;
        public string instance_guid;
        public string global_guid;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 scale;
        public string parent;
        public string attachedTo;
        public Selectable.ScaleLevel scaleLevel;
        public List<string> materialNames;
        public string keepRelativePositionParentName;
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

    /// <summary>
    /// Used when saving
    /// </summary>
    public Data GetData()
    {
        data.objectName = gameObject.name;
        GetGUIDs();

        data.pos = transform.position;
        data.rot = transform.rotation;
        data.scale = transform.localScale;

        if (gameObject.TryGetComponent(out Selectable s))
        {
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
        return data.pos;
    }

    public Quaternion GetRotation()
    {
        return data.rot;
    }

    public Vector3 GetScale()
    {
        return data.scale;
    }

    public List<string> GetMaterials()
    {
        return data.materialNames;
    }

    public void StoreValues(TrackedObject.Data d)
    {
        if (d.scaleLevel != null)
            data.scaleLevel = d.scaleLevel;

        if (d.materialNames != null)
        {
            data.materialNames = d.materialNames;
        }

        data.pos = d.pos;
        data.rot = d.rot;
        data.scale = d.scale;
        data.attachedTo = d.attachedTo;
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

            data.parent = ConfigurationManager.GetGameObjectPath(gameObject);
        }
    }

    public bool IsDecal()
    {
        if(!string.IsNullOrEmpty(data.attachedTo) || GetComponent<Selectable>().AttachedTo != null)
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