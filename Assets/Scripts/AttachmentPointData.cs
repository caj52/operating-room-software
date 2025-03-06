using System;
using UnityEngine;

public partial class Selectable
{
    [Serializable]
    public class AttachmentPointData
    {
        [field: SerializeField]
        public string Guid
        { get; set; } = System.Guid.NewGuid().ToString();

        [field: SerializeField]
        public AttachmentPoint AttachmentPoint
        { get; set; }
    }
}