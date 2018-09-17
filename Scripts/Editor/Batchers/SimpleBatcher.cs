using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using EditorGUI = UnityEditor.EditorGUI;

namespace Unity.AutoLOD
{
    /// <summary>
    /// A simple batcher that combines textures into an atlas and meshes (non material-preserving)
    /// </summary>
    class SimpleBatcher : IBatcher
    {
        Texture2D whiteTexture
        {
            get
            {
                if (!m_WhiteTexture)
                {
                    var path = "Assets/AutoLOD/Generated/Atlases/white.asset";
                    m_WhiteTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (!m_WhiteTexture)
                    {
                        m_WhiteTexture = Object.Instantiate(Texture2D.whiteTexture);
                        var directory = Path.GetDirectoryName(path);
                        if (!Directory.Exists(directory))
                            Directory.CreateDirectory(directory);

                        AssetDatabase.CreateAsset(m_WhiteTexture, path);
                    }
                }

                return m_WhiteTexture;
            }
        }

        Texture2D m_WhiteTexture;
        private SimpleBatcherOption option = new SimpleBatcherOption();
        private TexturePacker packer = new TexturePacker();

        Texture2D GetTexture(Material m)
        {
            if (m)
            {
                if ( m.mainTexture != null)
                    return m.mainTexture as Texture2D;
                
            }

            return null;
        }

        public IEnumerator Batch(GameObject hlodRoot)
        {
            yield return PackTextures(hlodRoot);

            foreach (Transform child in hlodRoot.transform)
            {
                var go = child.gameObject;
                var renderers = go.GetComponentsInChildren<Renderer>();
                var materials = new HashSet<Material>(renderers.SelectMany(r => r.sharedMaterials));

                TextureAtlas atlas = packer.GetAtlas(go);

                var atlasLookup = new Dictionary<Texture2D, Rect>();
                var atlasTextures = atlas.textures;
                for (int i = 0; i < atlasTextures.Length; i++)
                {
                    atlasLookup[atlasTextures[i]] = atlas.uvs[i];
                }

                MeshFilter[] meshFilters = go.GetComponentsInChildren<MeshFilter>();
                var combine = new List<CombineInstance>();
                for (int i = 0; i < meshFilters.Length; i++)
                {
                    var mf = meshFilters[i];
                    var sharedMesh = mf.sharedMesh;

                    if (!sharedMesh)
                        continue;

                    if (!sharedMesh.isReadable)
                    {
                        var assetPath = AssetDatabase.GetAssetPath(sharedMesh);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                            if (importer)
                            {
                                importer.isReadable = true;
                                importer.SaveAndReimport();
                            }
                        }
                    }

                    var mesh = Object.Instantiate(sharedMesh);

                    var mr = mf.GetComponent<MeshRenderer>();
                    var sharedMaterials = mr.sharedMaterials;
                    var uv = mesh.uv;
                    var colors = mesh.colors;
                    if (colors == null || colors.Length == 0)
                        colors = new Color[uv.Length];
                    var updated = new bool[uv.Length];
                    var triangles = new List<int>();

                    // Some meshes have submeshes that either aren't expected to render or are missing a material, so go ahead and skip
                    var subMeshCount = Mathf.Min(mesh.subMeshCount, sharedMaterials.Length);
                    for (int j = 0; j < subMeshCount; j++)
                    {
                        var sharedMaterial = sharedMaterials[Mathf.Min(j, sharedMaterials.Length - 1)];
                        var mainTexture = whiteTexture;
                        var materialColor = Color.white;

                        if (sharedMaterial)
                        {
                            var texture = GetTexture(sharedMaterial);
                            if (texture)
                                mainTexture = texture;

                            if (sharedMaterial.HasProperty("_Color"))
                                materialColor = sharedMaterial.color;
                        }

                        if (mesh.GetTopology(j) != MeshTopology.Triangles)
                        {
                            Debug.LogWarning("Mesh must have triangles", mf);
                            continue;
                        }

                        triangles.Clear();
                        mesh.GetTriangles(triangles, j);
                        var uvOffset = atlasLookup[mainTexture];
                        foreach (var t in triangles)
                        {
                            if (!updated[t])
                            {
                                var uvCoord = uv[t];
                                if (mainTexture == whiteTexture)
                                {
                                    // Sample at center of white texture to avoid sampling edge colors incorrectly
                                    uvCoord.x = 0.5f;
                                    uvCoord.y = 0.5f;
                                }

                                uvCoord.x = Mathf.Lerp(uvOffset.xMin, uvOffset.xMax, uvCoord.x);
                                uvCoord.y = Mathf.Lerp(uvOffset.yMin, uvOffset.yMax, uvCoord.y);
                                uv[t] = uvCoord;

                                if (mainTexture == whiteTexture)
                                    colors[t] = materialColor;
                                else
                                    colors[t] = Color.white;

                                updated[t] = true;
                            }
                        }
                    }

                    mesh.uv = uv;
                    mesh.uv2 = null;
                    mesh.colors = colors;

                    for (int j = 0; j < subMeshCount; ++j)
                    {
                        var ci = new CombineInstance();
                        ci.mesh = mesh;
                        ci.subMeshIndex = j;
                        ci.transform = mf.transform.localToWorldMatrix;
                        combine.Add(ci);
                    }


                    mf.gameObject.SetActive(false);
                }

                var combinedMesh = new Mesh();
#if UNITY_2017_3_OR_NEWER
                combinedMesh.indexFormat = IndexFormat.UInt32;
#endif
                combinedMesh.CombineMeshes(combine.ToArray(), true, true);
                combinedMesh.RecalculateBounds();
                var meshFilter = go.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = combinedMesh;

                for (int i = 0; i < meshFilters.Length; i++)
                {
                    Object.DestroyImmediate(meshFilters[i].gameObject);
                }

                var meshRenderer = go.AddComponent<MeshRenderer>();
                Material material = null;
                if (option.BatchMaterial == null)
                {
                    material = new Material(Shader.Find("Custom/AutoLOD/SimpleBatcher"));
                }
                else
                {
                    material = new Material(option.BatchMaterial);
                }

                material.mainTexture = atlas.textureAtlas;
                meshRenderer.sharedMaterial = material;
            }
        }

        public IBatcherOption GetBatcherOption()
        {
            return option;
        }

        private IEnumerator PackTextures(GameObject hlodRoot)
        {
            foreach (Transform child in hlodRoot.transform)
            {
                var renderers = child.GetComponentsInChildren<Renderer>();
                var materials = new HashSet<Material>(renderers.SelectMany(r => r.sharedMaterials));

                var textures =
                    new HashSet<Texture2D>(
                        materials
                            .Select(GetTexture)
                            .Where(t => t != null))
                        .ToList();

                textures.Add(whiteTexture);

                packer.AddTextureGroup(child.gameObject, textures.ToArray());
                yield return null;
            }

            yield return packer.Pack(option.PackTextureSize, option.LimitTextureSize);
        }
    }

    class SimpleBatcherOption : IBatcherOption
    {
        const string k_PackTextureSelect = "AutoLOD.SimpleBatch.PackTextureSelect";
        const string k_LimitTextureSelect = "AutoLOD.SimpleBatch.LimitTextureSelect";
        const string k_MaterialGUID = "AutoLOD.SimpleBatch.MaterialGUID";
        class GUIStyles
        {
            public readonly string[] PackTextureSizeContents =
            {
                "128",
                "256",
                "512",
                "1024",
                "2048"
            };

            public readonly string[] LimitTextureSizeContents =
            {
                "32",
                "64",
                "128",
                "256",
                "512"
            };

            public readonly GUIContent PackTextureSize = EditorGUIUtility.TrTextContent("Pack texture size");
            public readonly GUIContent LimitTextureSize = EditorGUIUtility.TrTextContent("Limit texture size");

            public GUIStyles()
            {

            }

        }

        private static GUIStyles s_Styles;
        private static GUIStyles Styles
        {
            get
            {
                if (s_Styles == null)
                    s_Styles = new GUIStyles();
                return s_Styles;
            }
        }

        private int packTextureSelect = 3;  //< default size is 1024
        private int limitTextureSelect = 2; //< default size is 128

        private Material batchMaterial;

        public int PackTextureSize
        {
            get
            {
                string str = Styles.PackTextureSizeContents[packTextureSelect];
                return int.Parse(str);
            }
        }

        public int LimitTextureSize
        {
            get
            {
                string str = Styles.LimitTextureSizeContents[limitTextureSelect];
                return int.Parse(str);
            }
        }

        public Material BatchMaterial
        {
            get { return batchMaterial; }
        }

        public SimpleBatcherOption()
        {
            if (EditorPrefs.HasKey(k_PackTextureSelect))
                packTextureSelect = EditorPrefs.GetInt(k_PackTextureSelect, 3);
            if (EditorPrefs.HasKey(k_LimitTextureSelect))
                limitTextureSelect = EditorPrefs.GetInt(k_LimitTextureSelect, 2);
            if (EditorPrefs.HasKey(k_MaterialGUID))
            {
                do
                {
                    string guid = EditorPrefs.GetString(k_MaterialGUID, null);
                    if (string.IsNullOrEmpty(guid))
                        break;

                    string path  =AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                        break;

                    batchMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
                } while (false);   
            }
        }
        public void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            packTextureSelect = EditorGUILayout.Popup(Styles.PackTextureSize, packTextureSelect, Styles.PackTextureSizeContents);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(k_PackTextureSelect, packTextureSelect);
            }

            EditorGUI.BeginChangeCheck();
            limitTextureSelect = EditorGUILayout.Popup(Styles.LimitTextureSize, limitTextureSelect, Styles.LimitTextureSizeContents);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(k_LimitTextureSelect, limitTextureSelect);
            }

            EditorGUI.BeginChangeCheck();
            batchMaterial = EditorGUILayout.ObjectField("Material", batchMaterial, typeof(Material), false) as Material;
            if (EditorGUI.EndChangeCheck())
            {
                string assetPath = null;
                if ( batchMaterial != null)
                    assetPath = AssetDatabase.GetAssetPath(batchMaterial);

                if (string.IsNullOrEmpty(assetPath))
                {
                    EditorPrefs.DeleteKey(k_MaterialGUID);
                }
                else
                {
                    string guid = AssetDatabase.AssetPathToGUID(assetPath);
                    EditorPrefs.SetString(k_MaterialGUID, guid);
                }
            }
        }
    }
}
