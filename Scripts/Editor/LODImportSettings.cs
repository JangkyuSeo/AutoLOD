using System;

namespace Unity.AutoLOD
{
    [Serializable]
    public class LODImportSettings
    {
        public bool generateOnImport;
        public string meshSimplifier;
        public int maxLODGenerated;
        public int initialLODMaxPolyCount;
    }
}
