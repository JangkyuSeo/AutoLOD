using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.AutoLOD;
using UnityEditor.Experimental.AutoLOD.Utilities;
using UnityEngine;
using UnityEngine.Experimental.AutoLOD;
using UnityEngine.Rendering;

[RequiresLayer(HLODLayer)]
public class LODVolume : MonoBehaviour
{
    public const string HLODLayer = "HLOD";
    public static Type meshSimplifierType { set; get; }
    public static Type batcherType { set; get; }

    private bool dirty;

    [SerializeField]
    private Bounds bounds;
    [SerializeField]
    private GameObject hlodRoot;
    [SerializeField]
    private List<LODGroup> m_LodGroups = new List<LODGroup>();
    [SerializeField]
    private List<LODVolume> childVolumes = new List<LODVolume>();

    private LODGroup m_LodGroup;

    public GameObject HLODRoot
    {
        set { hlodRoot = value; }
        get { return hlodRoot; }
    }

    public List<LODGroup> LodGroups
    {
        get { return m_LodGroups; }
    }

    public LODGroup LodGroup
    {
        set { m_LodGroup = value;}
        get { return m_LodGroup; }
    }


    const HideFlags k_DefaultHideFlags = HideFlags.None;
    const ushort k_VolumeSplitCount = 32;//ushort.MaxValue;
    const string k_DefaultName = "LODVolumeNode";

    const int k_Splits = 2;

    static int s_VolumesCreated;

    IMeshSimplifier m_MeshSimplifier;

    

    static readonly Color[] k_DepthColors = new Color[]
    {
        Color.red,
        Color.green,
        Color.blue,
        Color.magenta,
        Color.yellow,
        Color.cyan,
        Color.grey,
    };

    void Awake()
    {

    }

    void Start()
    {
        m_LodGroup = GetComponent<LODGroup>();
        if ( m_LodGroup != null )
            m_LodGroup.SetEnabled(false);
    }

    public static LODVolume Create()
    {
        GameObject go = new GameObject(k_DefaultName + s_VolumesCreated++, typeof(LODVolume));
        go.layer = LayerMask.NameToLayer(HLODLayer);
        LODVolume volume = go.GetComponent<LODVolume>();
        return volume;
    }

    public IEnumerator SetLODGruops(List<LODGroup> lodGroups)
    {
        m_LodGroups.Clear();

        if (!this)
            yield break;
        
        if(lodGroups.Count == 0 )
            yield break;

        m_LodGroups = lodGroups;

        //only root node need it.
        if (transform.parent == null)
        {
            //we need update bounds before split
            //split need to bounds.
            UpdateBounds();
        }

        if (m_LodGroups.Count > k_VolumeSplitCount)
        {
            yield return Split();
        }
    }

    public void UpdateBounds()
    {
        if (m_LodGroups.Count == 0)
            return;

        bounds = m_LodGroups[0].GetBounds();

        for (int i = 0; i < m_LodGroups.Count; ++i)
        {
            var lodBounds = m_LodGroups[i].GetBounds();
            bounds.Encapsulate(lodBounds);
        }

        bounds = GetCuboidBounds(bounds);
        transform.position = bounds.center;

    }

    [ContextMenu("Split")]
    void SplitContext()
    {
        MonoBehaviourHelper.StartCoroutine(Split());
    }

    IEnumerator Split()
    {
        Vector3 size = bounds.size;
        size.x /= k_Splits;
        size.y /= k_Splits;
        size.z /= k_Splits;

        for (int i = 0; i < k_Splits; i++)
        {
            for (int j = 0; j < k_Splits; j++)
            {
                for (int k = 0; k < k_Splits; k++)
                {
                    var lodVolume = Create();
                    var lodVolumeTransform = lodVolume.transform;
                    lodVolumeTransform.parent = transform;
                    var center = bounds.min + size * 0.5f + Vector3.Scale(size, new Vector3(i, j, k));
                    lodVolumeTransform.position = center;
                    lodVolume.bounds = new Bounds(center, size);

                    List<LODGroup> groups = new List<LODGroup>();

                    foreach (LODGroup group in m_LodGroups)
                    {
                        if (WithinBounds(group, lodVolume.bounds))
                        {
                            groups.Add(group);
                        }
                    }

                    yield return lodVolume.SetLODGruops(groups);

                    childVolumes.Add(lodVolume);
                }
            }
        }
    }

    
    [ContextMenu("Grow")]
    void GrowContext()
    {
        var targetBounds = bounds;
        targetBounds.center += Vector3.up;
        MonoBehaviourHelper.StartCoroutine(Grow(targetBounds));
    }

    IEnumerator Grow(Bounds targetBounds)
    {
        var direction = Vector3.Normalize(targetBounds.center - bounds.center);
        Vector3 size = bounds.size;
        size.x *= k_Splits;
        size.y *= k_Splits;
        size.z *= k_Splits;

        var corners = new Vector3[]
        {
            bounds.min,
            bounds.min + Vector3.right * bounds.size.x,
            bounds.min + Vector3.forward * bounds.size.z,
            bounds.min + Vector3.up * bounds.size.y,
            bounds.min + Vector3.right * bounds.size.x + Vector3.forward * bounds.size.z,
            bounds.min + Vector3.right * bounds.size.x + Vector3.up * bounds.size.y,
            bounds.min + Vector3.forward * bounds.size.x + Vector3.up * bounds.size.y,
            bounds.min + Vector3.right * bounds.size.x + Vector3.forward * bounds.size.z + Vector3.up * bounds.size.y
        };

        // Determine where the current volume is situated in the new expanded volume
        var best = 0f;
        var expandedVolumeCenter = bounds.min;
        foreach (var c in corners)
        {
            var dot = Vector3.Dot(c, direction);
            if (dot > best)
            {
                best = dot;
                expandedVolumeCenter = c;
            }
            yield return null;
        }

        var expandedVolume = Create();
        var expandedVolumeTransform = expandedVolume.transform;
        expandedVolumeTransform.position = expandedVolumeCenter;
        expandedVolume.bounds = new Bounds(expandedVolumeCenter, size);
        expandedVolume.m_LodGroups = new List<LODGroup>(m_LodGroups);
        var expandedBounds = expandedVolume.bounds;

        transform.parent = expandedVolumeTransform;

        var splitSize = bounds.size;
        var currentCenter = bounds.center;
        for (int i = 0; i < k_Splits; i++)
        {
            for (int j = 0; j < k_Splits; j++)
            {
                for (int k = 0; k < k_Splits; k++)
                {
                    var center = expandedBounds.min + splitSize * 0.5f + Vector3.Scale(splitSize, new Vector3(i, j, k));
                    if (Mathf.Approximately(Vector3.Distance(center, currentCenter), 0f))
                        continue; // Skip the existing LODVolume we are growing from

                    var lodVolume = Create();
                    var lodVolumeTransform = lodVolume.transform;
                    lodVolumeTransform.parent = expandedVolumeTransform;
                    lodVolumeTransform.position = center;
                    lodVolume.bounds = new Bounds(center, splitSize);
                }
            }
        }
    }
    

    //IEnumerator Shrink()
    //{
    //    var populatedChildrenNodes = new List<LODVolume>();
    //    foreach (Transform child in transform)
    //    {
    //        var lodVolume = child.GetComponent<LODVolume>();
    //        var lodGroups = lodVolume.m_LodGroups;
    //        if (lodGroups != null && lodGroups.Count > 0)
    //            populatedChildrenNodes.Add(lodVolume);

    //        yield return null;
    //    }

    //    if (populatedChildrenNodes.Count == 1)
    //    {
    //        var newRootVolume = populatedChildrenNodes[0];
    //        newRootVolume.transform.parent = null;
    //        CleanupHLOD();
    //        DestroyImmediate(gameObject);

    //        yield return newRootVolume.Shrink();
    //    }
    //}

    static Bounds GetCuboidBounds(Bounds bounds)
    {
        // Expand bounds side lengths to maintain a cube
        var maxSize = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
        var extents = Vector3.one * maxSize * 0.5f;
        bounds.center = bounds.min + extents;
        bounds.extents = extents;

        return bounds;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (Settings.ShowVolumeBounds)
        {
            var depth = GetDepth(transform);
            DrawGizmos(Mathf.Max(1f - Mathf.Pow(0.9f, depth), 0.2f), GetDepthColor(depth));
        }
    }


    void OnDrawGizmosSelected()
    {
        if (Selection.activeGameObject == gameObject)
            DrawGizmos(1f, Color.magenta);
    }


    void DrawGizmos(float alpha, Color color)
    {
        color.a = alpha;
        Gizmos.color = color;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }

   
#endif

#if UNITY_EDITOR
    [ContextMenu("GenerateLODs")]
    void GenerateLODsContext()
    {
        GenerateLODs(null);
    }

    void GenerateLODs(Action completedCallback)
    {
        int maxLOD = 1;
        var go = gameObject;

        var hlodLayer = LayerMask.NameToLayer(HLODLayer);

        var lodGroup = go.GetComponent<LODGroup>();
        if (lodGroup)
        {
            var lods = new LOD[maxLOD + 1];
            var lod0 = lodGroup.GetLODs()[0];
            lod0.screenRelativeTransitionHeight = 0.5f;
            lods[0] = lod0;

            var meshes = new List<Mesh>();

            var totalMeshCount = maxLOD * lod0.renderers.Length;
            var runningMeshCount = 0;
            for (int l = 1; l <= maxLOD; l++)
            {
                var lodRenderers = new List<MeshRenderer>();
                foreach (var mr in lod0.renderers)
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    var sharedMesh = mf.sharedMesh;

                    var lodTransform = EditorUtility.CreateGameObjectWithHideFlags(string.Format("{0} LOD{1}", sharedMesh.name, l),
                        k_DefaultHideFlags, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                    lodTransform.gameObject.layer = hlodLayer;
                    lodTransform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                    lodTransform.localScale = mf.transform.lossyScale;
                    lodTransform.SetParent(mf.transform, true);

                    var lodMF = lodTransform.GetComponent<MeshFilter>();
                    var lodRenderer = lodTransform.GetComponent<MeshRenderer>();

                    lodRenderers.Add(lodRenderer);

                    EditorUtility.CopySerialized(mf, lodMF);
                    EditorUtility.CopySerialized(mf.GetComponent<MeshRenderer>(), lodRenderer);

                    var simplifiedMesh = new Mesh();
                    simplifiedMesh.name = sharedMesh.name + string.Format(" LOD{0}", l);
                    lodMF.sharedMesh = simplifiedMesh;
                    meshes.Add(simplifiedMesh);

                    var index = l;
                    var inputMesh = sharedMesh.ToWorkingMesh();
                    var outputMesh = simplifiedMesh.ToWorkingMesh();

                    var meshSimplifier = (IMeshSimplifier)Activator.CreateInstance(meshSimplifierType);



                    meshSimplifier.Simplify(inputMesh, outputMesh, Mathf.Pow(0.5f, l), () =>
                    {
                        Debug.Log("Completed LOD " + index);
                        outputMesh.ApplyToMesh(simplifiedMesh);
                        simplifiedMesh.RecalculateBounds();

                        runningMeshCount++;
                        Debug.Log(runningMeshCount + " vs " + totalMeshCount);
                        if (runningMeshCount == totalMeshCount && completedCallback != null)
                            completedCallback();
                    });
                }

                var lod = lods[l];
                lod.renderers = lodRenderers.ToArray();
                lod.screenRelativeTransitionHeight = l == maxLOD ? 0.01f : Mathf.Pow(0.5f, l + 1);
                lods[l] = lod;
            }

            lodGroup.ForceLOD(0);
            lodGroup.SetLODs(lods.ToArray());
            lodGroup.RecalculateBounds();
            lodGroup.ForceLOD(-1);

            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (prefab)
            {
                var assetPath = AssetDatabase.GetAssetPath(prefab);
                var pathPrefix = Path.GetDirectoryName(assetPath) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(assetPath);
                var lodsAssetPath = pathPrefix + "_lods.asset";
                ObjectUtils.CreateAssetFromObjects(meshes.ToArray(), lodsAssetPath);
            }
        }
    }
#endif

    public static int GetDepth(Transform transform)
    {
        int count = 0;
        Transform parent = transform.parent;
        while (parent)
        {
            count++;
            parent = parent.parent;
        }

        return count;
    }

    public static Color GetDepthColor(int depth)
    {
        return k_DepthColors[depth % k_DepthColors.Length];
    }

    static bool WithinBounds(LODGroup group, Bounds bounds)
    {
        Bounds groupBounds = group.GetBounds();
        // Use this approach if we are not going to split meshes and simply put the object in one volume or another
        return Mathf.Approximately(bounds.size.magnitude, 0f) || bounds.Contains(groupBounds.center);
    }

    //Disable all LODVolumes and enable on all meshes.
    //UpdateLODGroup will be makes currect.
    public void ResetLODGroup()
    {
        if (transform.parent == null)
        {
            Debug.Log("Reset volume.");
        }
        if (m_LodGroup == null)
        {
            m_LodGroup = GetComponent<LODGroup>();
        }

        if (m_LodGroup == null)
        {
            return;
        }

        m_LodGroup.SetEnabled(false);

        if (hlodRoot != null)
        {
            var meshRenderer = hlodRoot.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }
        }
        
        //if this is leaf, all lodgroups should be turn on.
        if (childVolumes.Count == 0)
        {
            foreach (var group in m_LodGroups)
            {
                if ( group != null )
                    group.SetEnabled(true);
            }
        }
        
        foreach (var child in childVolumes)
        {
            child.ResetLODGroup();
        }
    }
    public void UpdateLODGroup(Camera camera, Vector3 cameraPosition, bool parentUsed)
    {
        if (m_LodGroup == null)
        {
            m_LodGroup = GetComponent<LODGroup>();
        }

        //if lodgroup is not exists, there is no mesh.
        if (m_LodGroup == null)
        {
            return;
        }

        //if parent already visibled, don't need to visible to children.
        if (parentUsed == true)
        {
            m_LodGroup.SetEnabled(false);

            if (childVolumes.Count == 0)
            {
                foreach (var group in m_LodGroups)
                {
                    group.SetEnabled(false);
                }
            }
            else
            {
                foreach (var childVolume in childVolumes)
                {
                    childVolume.UpdateLODGroup(camera, cameraPosition, true);
                }
            }

            return;
        }

        int currentLod = m_LodGroup.GetCurrentLOD(camera);

        if (currentLod == 0)
        {
            m_LodGroup.SetEnabled(false);

            //leaf node have to used mesh own.
            if (childVolumes.Count == 0)
            {
                foreach (var group in m_LodGroups)
                {
                    group.SetEnabled(true);
                }
            }
            else
            {
                foreach (var childVolume in childVolumes)
                {
                    childVolume.UpdateLODGroup(camera, cameraPosition, false);
                }
            }


        }
        else if ( currentLod == 1 )
        {
            m_LodGroup.SetEnabled(true);

            //leaf node have to used mesh own.
            if (childVolumes.Count == 0)
            {
                foreach (var group in m_LodGroups)
                {
                    group.SetEnabled(false);
                }
            }
            else
            {
                foreach (var childVolume in childVolumes)
                {
                    childVolume.UpdateLODGroup(camera, cameraPosition, true);
                }
            }

        }
    }
}
