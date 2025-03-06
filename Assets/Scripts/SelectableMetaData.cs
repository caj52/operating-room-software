using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serialized directly to/from online database. 
/// Pre-filled data (in Editor) will be considered "seed" data
/// </summary>
[Serializable]
public class SelectableMetaData
{
    /// <summary>
    /// True if selectable only appears as part of another "parent" selectable
    /// </summary>
    [field: SerializeField, Tooltip("True if selectable only appears as part of another \"parent\" selectable")]
    public bool IsSubSelectable { get; set; }

    /// <summary>
    /// True if selectable is enabled. If false, the selectable will no longer show up in any object selection menu
    /// </summary>
    [field: SerializeField,
    Tooltip("True if selectable is enabled. If false, the selectable will no longer show up in any object selection menu")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// True if this object does not go on an attachment point and can be placed freely in the world at any time
    /// </summary>
    [field: SerializeField, 
    Tooltip("True if this object does not go on an attachment point and can be placed freely in the world at any time")]
    public bool IsStandalone
    { get; set; }

    [field: SerializeField, HideInInspector] 
    public string ThumbnailBase64 { get; set; }

    [field: SerializeField] 
    public string Name { get; set; }

    [field: SerializeField, HideInInspector] 
    public string SubPartName { get; set; }

    [field: SerializeField]
    public List<string> Categories 
    { get; set; } = new();

    [field: SerializeField]
    public List<string> KeyWords
    { get; set; } = new();

    [field: SerializeField, ReadOnly]
    public List<AttachmentPointGuidMetaData> 
    AttachmentPointGuidMetaData { get; set; } = new();

    public List<PdfData> PdfData { get; set; } = new();
}

[Serializable]
public class AttachmentPointGuidMetaData
{
    [field: SerializeField] 
    public string Guid { get; set; }

    [field: SerializeField] 
    public AttachmentPointMetaData 
    MetaData { get; set; }
}

[Serializable]
public class PdfData
{
    public string Table { get; set; }
    public string Key { get; set; }
    public string Value { get; set; }
}