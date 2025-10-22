using System;
using System.Linq;
using UnityEngine;

public partial class Selectable
{
    [Serializable]
    public class ScaleLevel
    {
        [field: SerializeField] public float Size { get; set; }
        [field: SerializeField] public bool Selected { get; set; }
        [field: SerializeField] public bool ModelDefault { get; set; }

        [field: SerializeField, ReadOnly]
        public float ScaleZ { get; set; }
        [field: SerializeField] public Metadata[] metadata;

        public bool TryGetValue(string key, out string value)
        {
            if (metadata == null || metadata.Length == 0)
            {
                value = "";
                return false;
            }
            var found = metadata.FirstOrDefault(x => x.key == key);
            value = found.value;
            return !string.IsNullOrEmpty(found.key) && !string.IsNullOrEmpty(found.value);
        }
    }
}