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
                    if (!m_WhiteTexture)
                    {
                        m_WhiteTexture = Object.Instantiate(Texture2D.whiteTexture);
                    }
                }

                return m_WhiteTexture;
            }
        }

        Texture2D m_WhiteTexture;
        private SimpleBatcherOption option = null;
        private TexturePacker packer = new TexturePacker();

        public SimpleBatcher(string groupName)
        {
            option = new SimpleBatcherOption(groupName);
        }

        Texture2D GetTexture(Material m)
        {
            if (m)
            {
                if ( m.mainTexture != null)
                    return m.mainTexture as Texture2D;

            
            }

            return null;
        }

        public IEnumerator Batch(GameObject hlodRoot, System.Action<float> progress)
        {
            yield return PackTextures(hlodRoot);

            Dictionary<Texture2D, Material> createdMaterials = new Dictionary<Texture2D, Material>();


            for(int childIndex = 0; childIndex < hlodRoot.transform.childCount; ++childIndex)
            {
                var child = hlodRoot.transform.GetChild(childIndex);

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
                if (createdMaterials.ContainsKey(atlas.textureAtlas) == false)
                {
                    if (option.BatchMaterial == null)
                    {
                        material = new Material(Shader.Find("Custom/AutoLOD/SimpleBatcher"));
                    }
                    else
                    {
                        material = new Material(option.BatchMaterial);
                    }

                    material.mainTexture = atlas.textureAtlas;

                    string matName = hlodRoot.name + "_" + createdMaterials.Count;
                    AssetDatabase.CreateAsset(material, "Assets/" + SceneLOD.GetSceneLODPath() + matName + ".mat");

                    createdMaterials.Add(atlas.textureAtlas, material);
                }
                else
                {
                    material = createdMaterials[atlas.textureAtlas];
                }

                meshRenderer.sharedMaterial = material;

                string assetName = hlodRoot.name + "_" + go.name;

                AssetDatabase.CreateAsset(combinedMesh,
                    "Assets/" + SceneLOD.GetSceneLODPath() + assetName + ".asset");
                AssetDatabase.SaveAssets();

                if (progress != null)
                    progress((float) childIndex / (float) hlodRoot.transform.childCount);
                yield return null;
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

        private SimpleBatcherConfig.ConfigData data;

        public int PackTextureSize
        {
            get
            {
                string str = Styles.PackTextureSizeContents[data.PackTextureSelect];
                return int.Parse(str);
            }
        }

        public int LimitTextureSize
        {
            get
            {
                string str = Styles.LimitTextureSizeContents[data.LimitTextureSelect];
                return int.Parse(str);
            }
        }

        public Material BatchMaterial
        {
            get { return data.BatchMaterial; }
        }

        public SimpleBatcherOption(string groupName)
        {
            data = SimpleBatcherConfig.Get(groupName);
        }
        public void OnGUI()
        {
            data.PackTextureSelect = EditorGUILayout.Popup(Styles.PackTextureSize, data.PackTextureSelect, Styles.PackTextureSizeContents);
            data.LimitTextureSelect = EditorGUILayout.Popup(Styles.LimitTextureSize, data.LimitTextureSelect, Styles.LimitTextureSizeContents);
            data.BatchMaterial = EditorGUILayout.ObjectField("Material", data.BatchMaterial, typeof(Material), false) as Material;
        }

        
    }
}
