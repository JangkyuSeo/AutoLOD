using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AutoLOD.LODCache
{
    [Serializable]
    public class CacheMesh
    {
        public float Quality;
        public string SimplifierType;
        public Mesh Mesh;
    }
}