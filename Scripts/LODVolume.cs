using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Unity.AutoLOD;
using Unity.AutoLOD.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = System.Object;

[RequiresLayer(HLODLayer)]
public class LODVolume : MonoBehaviour
{
    public const string HLODLayer = "HLOD";
    public static Type meshSimplifierType { set; get; }
    public static float meshSimplificationRatio { set; get; }

    [Serializable]
    public class LODVolumeGroup
    {
        [SerializeField]
        private string m_GroupName;
        [SerializeField]
        private GameObject m_HLODObject;
        [SerializeField]
        private List<LODGroup> m_LODGroups = new List<LODGroup>();

        public string GroupName
        {
            set { m_GroupName = value; }
            get { return m_GroupName; }
        }

        public GameObject HLODObject
        {
            set { m_HLODObject = value; }
            get { return m_HLODObject; }
        }

        public List<LODGroup> LODGroups
        {
            set { m_LODGroups = value; }
            get { return m_LODGroups; }
        }

    }

    [SerializeField]
    private Bounds bounds;
    [SerializeField]
    private List<LODVolume> childVolumes = new List<LODVolume>();
    [SerializeField]
    private List<LODVolumeGroup> m_VolumeGroups = new List<LODVolumeGroup>();   //< I think dictionary is better. but I used list for showing in inspector.

    private LODGroup m_LodGroup;

    
    public LODGroup LodGroup
    {
        set { m_LodGroup = value;}
        get { return m_LodGroup; }
    }

    public Bounds Bounds
    {
        set { bounds = value; }
        get { return bounds; }
    }

    public List<LODVolumeGroup> VolumeGroups
    {
        get { return m_VolumeGroups; }
    }


    const HideFlags k_DefaultHideFlags = HideFlags.None;
    const string k_DefaultName = "LODVolumeNode";

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

    public void AddChild(LODVolume volume)
    {
        childVolumes.Add(volume);
    }

    public void SetLODGroups(Dictionary<string, List<LODGroup>> groups)
    {
        foreach (var pair in groups)
        {
            SetLODGroups(pair.Key, pair.Value);
        }
    }

    public void SetLODGroups(string groupName, List<LODGroup> groups)
    {
        for (int i = 0; i < m_VolumeGroups.Count; ++i)
        {
            if (m_VolumeGroups[i].GroupName == groupName)
            {
                m_VolumeGroups[i].LODGroups = groups;
                return;
            }
        }

        LODVolumeGroup volumeGroup = new LODVolumeGroup();
        volumeGroup.GroupName = groupName;
        volumeGroup.LODGroups = groups;

        m_VolumeGroups.Add(volumeGroup);
    }

    public bool Contains(LODGroup group)
    {
        foreach (var volumeGroup in m_VolumeGroups)
        {
            if (volumeGroup.LODGroups.Contains(group))
                return true;
        }

        return false;
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



                    meshSimplifier.Simplify(inputMesh, outputMesh, Mathf.Pow(meshSimplificationRatio, l), () =>
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
                lod.screenRelativeTransitionHeight = l == maxLOD ? 0.01f : Mathf.Pow(meshSimplificationRatio, l + 1);
                lods[l] = lod;
            }

            lodGroup.ForceLOD(0);
            lodGroup.SetLODs(lods.ToArray());
            lodGroup.RecalculateBounds();
            lodGroup.ForceLOD(-1);

#if UNITY_2018_2_OR_NEWER
            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
#else
            var prefab = PrefabUtility.GetPrefabParent(go);
#endif
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

        foreach (var volumeGroup in m_VolumeGroups)
        {
            if (volumeGroup.HLODObject != null)
            {
                var meshRenderer = volumeGroup.HLODObject.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = false;
                }
            }

            //if this is a leaf, all lodgroups should be turned on.
            if (childVolumes.Count == 0)
            {
                foreach (var group in volumeGroup.LODGroups)
                {
                    if ( group != null )
                        group.SetEnabled(true);
                }
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
                foreach (var volumeGroup in m_VolumeGroups)
                {
                    foreach (var group in volumeGroup.LODGroups)
                    {
                        group.SetEnabled(false);
                    }
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
                foreach (var volumeGroup in m_VolumeGroups)
                {
                    foreach (var group in volumeGroup.LODGroups)
                    {
                        group.SetEnabled(true);
                    }
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
                foreach (var volumeGroup in m_VolumeGroups)
                {
                    foreach (var group in volumeGroup.LODGroups)
                    {
                        group.SetEnabled(false);
                    }
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
