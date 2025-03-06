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
            value = metadata.SingleOrDefault(x => x.key == key).value;
            return value != "";
        }
    }
}