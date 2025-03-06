using UnityEngine;
using System;

namespace SplenSoft.AssetBundles
{
    [Serializable]
    public class TypeAssembly
    {
        public TypeAssembly() { }

        public TypeAssembly(string typeName, string assemblyName) 
        { 
            TypeName = typeName;
            AssemblyName = assemblyName;
        }

        [field: SerializeField] public string TypeName { get; set; }
        [field: SerializeField] public string AssemblyName { get; set; }
    }
}