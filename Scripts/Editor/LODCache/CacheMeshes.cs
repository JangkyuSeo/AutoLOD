using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AutoLOD.LODCache
{

    [Serializable]
    public class CacheMeshes : ScriptableObject
    {
        public long Timestamp;
        public List<CacheMesh> Meshes = new List<CacheMesh>();
    }

}