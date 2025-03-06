using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Parabox.CSG
{
    /// <summary>
    /// A singelton that only exists so other scripts can find a 
    /// default material easily
    /// </summary>
    public class DefaultMaterialReference : MonoBehaviour
    {
        private static DefaultMaterialReference Instance
        { get; set; }

        [field: SerializeField]
        private Material Material { get; set; }

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static Material GetDefaultMaterial()
        {
            return Instance.Material;
        }
    }

}