using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Security.Permissions;
using Unity.AutoLOD.Utilities;

namespace Unity.AutoLOD.LODCache
{
    public class Cache
    {
        #region Interface
        public static void ClearMemory()
        {
            Instance.ClearMemoryImpl();
        }

        public static void ClearDisk()
        {
            Instance.ClearDiskImpl();
        }

        public static Mesh GetLODMesh(Mesh source, float quality)
        {
            return Instance.GetLODMeshImpl(source, quality);
        }
        
        #endregion

        #region Singleton
        private static Cache m_Instance;

        private static Cache Instance
        {
            get
            {
                if ( m_Instance == null )
                    m_Instance = new Cache();
                return m_Instance;
            }
        }
        #endregion

        private const string k_CachePath = "Assets/AutoLOD/Cache/";
        private Dictionary<string, CacheMeshes> m_CachedMeshes = new Dictionary<string, CacheMeshes>();

        private Cache()
        {

        }

        private void ClearMemoryImpl()
        {
            m_CachedMeshes.Clear();
            Debug.Log("LODCacheMemroy cleared.");
        }

        private void ClearDiskImpl()
        {
            ClearMemoryImpl();
            FileUtils.DeleteDirectory(k_CachePath);
            Debug.Log("LODCache cleared in disk. " + k_CachePath);
        }

        private Mesh GetLODMeshImpl(Mesh source, float quality)
        {
            string guid = "";
            long localID = 0;

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out guid, out localID))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                DateTime time = System.IO.File.GetLastWriteTime(assetPath);

                return GetMesh(source, quality, guid, time.ToFileTimeUtc());
            }
            else
            {
                Debug.Log("Failed get GUID: " + source.name);
                return SimplifyMesh(source, quality, null);
            }
        }

        private Mesh SimplifyMesh(Mesh source, float quality, Action<Mesh> complete)
        {
            var simplifiedMesh = new Mesh();

            var inputMesh = source.ToWorkingMesh();
            var outputMesh = new WorkingMesh();

            var meshSimplifier = (IMeshSimplifier)Activator.CreateInstance(AutoLOD.meshSimplifierType);
            meshSimplifier.Simplify(inputMesh, outputMesh, quality, () =>
            {
                outputMesh.ApplyToMesh(simplifiedMesh);
                simplifiedMesh.RecalculateBounds();
                if (complete != null)
                    complete(simplifiedMesh);
            });

            return simplifiedMesh;
        }

        private Mesh GetMesh(Mesh source, float quality, string guid, long time)
        {
            CacheMeshes meshes = GetCacheMeshes(guid, time);
            string simplifierTypeStr = AutoLOD.meshSimplifierType.AssemblyQualifiedName;

            //Compare three decimal places
            int compareQuality = (int)(quality * 1000.0f + 0.5f);

            for ( int i = 0; i < meshes.Meshes.Count; ++i )
            {
                var mesh = meshes.Meshes[i];

                if (mesh.Mesh == null)
                {
                    meshes.Meshes[i] = meshes.Meshes.Last();
                    meshes.Meshes.RemoveAt(meshes.Meshes.Count - 1);
                    i -= 1;
                    continue;
                }

                int meshQuality = (int) (mesh.Quality * 1000.0f + 0.5f);
                if (mesh.SimplifierType == simplifierTypeStr && meshQuality == compareQuality)
                {
                    return mesh.Mesh;
                }
            }

            //Mesh not found in cache.
            //need create mesh.
            Mesh simplifiedMesh = SimplifyMesh(source, quality, (m) =>
            {
                string path = GetAssetPath(guid);

                AssetDatabase.AddObjectToAsset(m, path);
                AssetDatabase.SaveAssets();
            });

            CacheMesh cacheMesh = new CacheMesh();
            cacheMesh.Mesh = simplifiedMesh;
            cacheMesh.Quality = quality;
            cacheMesh.SimplifierType = simplifierTypeStr;

            meshes.Meshes.Add(cacheMesh);

            EditorUtility.SetDirty(meshes);
            AssetDatabase.SaveAssets();

            return simplifiedMesh;
        }

        private CacheMeshes GetCacheMeshes(string guid, long time)
        {
            if (m_CachedMeshes.ContainsKey(guid) == false)
            {
                var meshes = LoadCacheMeshes(guid);
                if (meshes != null)
                {
                    m_CachedMeshes[guid] = meshes;
                }
                else
                {
                    m_CachedMeshes[guid] = NewCacheMeshes(guid, time);
                }
                
            }

            //Mesh updated. Need to update cache.
            if (m_CachedMeshes[guid].Timestamp != time)
            {
                DeleteCacheMeshes(guid);
                m_CachedMeshes[guid] = NewCacheMeshes(guid, time);
            }

            return m_CachedMeshes[guid];
        }

        private CacheMeshes NewCacheMeshes(string guid, long time)
        {
            CacheMeshes cacheMeshes = CacheMeshes.CreateInstance<CacheMeshes>();
            cacheMeshes.Timestamp = time;
            cacheMeshes.name = guid;

            WriteCacheMeshes(guid, cacheMeshes);
            return cacheMeshes;
        }

        private CacheMeshes LoadCacheMeshes(string guid)
        {
            string path = GetAssetPath(guid);

            if (System.IO.File.Exists(path) == false)
                return null;

            return AssetDatabase.LoadAssetAtPath<CacheMeshes>(path);
        }

        private void DeleteCacheMeshes(string guid)
        {
            string path = GetAssetPath(guid);
            AssetDatabase.DeleteAsset(path);
        }

        private void WriteCacheMeshes(string guid, CacheMeshes cacheMeshes)
        {
            string path = GetAssetPath(guid);

            if (System.IO.Directory.Exists(k_CachePath) == false)
                System.IO.Directory.CreateDirectory(k_CachePath);

            AssetDatabase.CreateAsset(cacheMeshes, path);
            AssetDatabase.SaveAssets();
        }

        private string GetAssetPath(string guid)
        {
            return k_CachePath + guid + ".asset";
        }
    }

}