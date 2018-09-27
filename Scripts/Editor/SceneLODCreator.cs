using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.AutoLOD.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.PlayerLoop;
using Mesh = UnityEngine.Mesh;
using Object = UnityEngine.Object;
using Dbg = UnityEngine.Debug;

namespace Unity.AutoLOD
{
    public class SceneLODCreator : ScriptableSingleton<SceneLODCreator>
    {
        private const string k_OptionStr = "AutoLOD.Options.";

        public class Options : ScriptableObject
        {
            public float VolumeSize = 30.0f;
            public float LODRange = 0.3f;

            public Dictionary<string, GroupOptions> GroupOptions = new Dictionary<string, GroupOptions>();

            public void SaveToEditorPrefs()
            {
                EditorPrefs.SetFloat(k_OptionStr + "VolumeSize", VolumeSize);
                EditorPrefs.SetFloat(k_OptionStr + "LODRange", LODRange);

                foreach (var group in GroupOptions.Values)
                {
                    group.SaveToEditorPrefs();
                }
            }

            public void LoadFromEditorPrefs()
            {
                VolumeSize = EditorPrefs.GetFloat(k_OptionStr + "VolumeSize", 30.0f);
                LODRange = EditorPrefs.GetFloat(k_OptionStr + "LODRange", 0.3f);

                GroupOptions.Clear();
                var groupNames = HLODGroup.FindAllGroups().Keys.ToList();
                foreach (var name in groupNames)
                {
                    GroupOptions group = new GroupOptions(name);
                    group.LoadFromEditorPrefs();
                    GroupOptions.Add(name, group);
                }
            }
        }


        public class GroupOptions
        {
            public bool VolumeSimplification = true;
            public float VolumePolygonRatio = 0.5f;

            public Type BatcherType;
            public IBatcher Batcher;

            private string groupName;

            public GroupOptions(string name)
            {
                groupName = name;
            }

            public void SaveToEditorPrefs()
            {
                EditorPrefs.SetBool(k_OptionStr + groupName + ".VolumeSimplification", VolumeSimplification);
                EditorPrefs.SetFloat(k_OptionStr + groupName + ".VolumePolygonRatio", VolumePolygonRatio);
                if ( BatcherType != null)
                    EditorPrefs.SetString(k_OptionStr + groupName + ".BatcherType", BatcherType.AssemblyQualifiedName);
                
            }

            public void LoadFromEditorPrefs()
            {
                VolumeSimplification = EditorPrefs.GetBool(k_OptionStr + groupName + ".VolumeSimplification", true);
                VolumePolygonRatio = EditorPrefs.GetFloat(k_OptionStr + groupName + ".VolumePolygonRatio", 0.5f);
                string batcherTypeStr = EditorPrefs.GetString(k_OptionStr + groupName + ".BatcherType");
                BatcherType = Type.GetType(batcherTypeStr);
                Batcher = (IBatcher) Activator.CreateInstance(BatcherType, groupName);
            }
        }

        enum CoroutineOrder
        {
            BuildTree,          //< Make octree created with LODVolume.
            UpdateMesh,            
            Batch,
            UpdateLOD,
            Finish,
        }
        /// <summary>
        /// job will be moved to end of queue.
        /// </summary>
        class MoveToEnd : YieldInstruction
        {
            public MoveToEnd()
            {

            }
        }

        private const string k_HLODRootContainer = "HLODs";
        private const int k_Splits = 2;

        private GameObject m_HLODRootContainer;
        private Dictionary<string, GameObject> m_GroupHLODRootContainer;
        private LODVolume m_RootVolume;

        private Options m_Options;

        public Options GetOptions()
        {
            if ( m_Options == null )
                m_Options = Options.CreateInstance<Options>();

            return m_Options;
        }


        static List<LODGroup> RemoveHLODLayer(List<LODGroup> groups)
        {
            int hlodLayerMask = LayerMask.NameToLayer(LODVolume.HLODLayer);

            // Remove any lodgroups that should not be there (e.g. HLODs)
            groups.RemoveAll(r =>
            {
                if (r)
                {
                    if (r.gameObject.layer == hlodLayerMask)
                        return true;

                    LOD lastLOD = r.GetLODs().Last();
                    if (lastLOD.renderers.Length == 0)
                        return true;
                }

                return false;
            });

            return groups;
        }

        static List<LODGroup> FindAllGroupsInHLODGroup(List<HLODGroup> hlodGroups)
        {
            List<LODGroup> lodGroups = new List<LODGroup>();

            foreach (var group in hlodGroups)
            {
                lodGroups.AddRange(group.GetComponentsInChildren<LODGroup>());
            }

            return RemoveHLODLayer(lodGroups);
        }
        static Dictionary<string, List<LODGroup>> FindAllGroups(Dictionary<string, List<HLODGroup>> hlodGroups)
        {
            Dictionary<string, List<LODGroup>> lodGroups = new Dictionary<string, List<LODGroup>>();
            List<LODGroup> allGroups = RemoveHLODLayer( FindObjectsOfType<LODGroup>().ToList() );

            foreach (var pair in hlodGroups)
            {
                var groups = FindAllGroupsInHLODGroup(pair.Value);
                lodGroups.Add(pair.Key, groups);

                allGroups.RemoveAll(g => groups.Contains(g));
            }

            if( lodGroups.ContainsKey("") == false )
                lodGroups.Add("", new List<LODGroup>());

            //add remain groups to default group.
            lodGroups[""].AddRange(allGroups);

            return lodGroups;
        }

            
        static Bounds GetCuboidBounds(Bounds bounds)
        {
            // Expand bounds side lengths to maintain a cube
            var maxSize = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
            var extents = Vector3.one * maxSize * 0.5f;
            bounds.center = bounds.min + extents;
            bounds.extents = extents;

            return bounds;
        }
        static Bounds CalcBounds(List<LODGroup> lodGroups)
        {
            Bounds bounds = new Bounds();
            if ( lodGroups.Count == 0 )
                return bounds;

            bounds = lodGroups[0].GetBounds();
            for (int i = 0; i < lodGroups.Count; ++i)
            {
                bounds.Encapsulate(lodGroups[i].GetBounds());
            }

            return GetCuboidBounds(bounds);
        }

        static bool WithinBounds(LODGroup group, Bounds bounds)
        {
            Bounds groupBounds = group.GetBounds();
            // Use this approach if we are not going to split meshes and simply put the object in one volume or another
            return Mathf.Approximately(bounds.size.magnitude, 0f) || bounds.Contains(groupBounds.center);
        }

        IEnumerator BuildOctree(LODVolume volume)
        {
            var bounds = volume.Bounds;
            float boundsSize = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));

            yield return BuildHLOD(volume);
            //reserve logic.
            //UpdateLODGroup should be run after batch.
            StartCustomCoroutine(UpdateLODGroup(volume), CoroutineOrder.UpdateLOD);

            //Make a child if necessary.
            if (boundsSize < m_Options.VolumeSize)
                yield break;

            //Volume doesn't have any group for a split.
            if ( volume.VolumeGroups.Count == 0 )
                yield break;;
            
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
                        var lodVolume = LODVolume.Create();
                        var lodVolumeTransform = lodVolume.transform;
                        lodVolumeTransform.parent = volume.transform;
                        var center = bounds.min + size * 0.5f + Vector3.Scale(size, new Vector3(i, j, k));
                        lodVolumeTransform.position = center;

                        lodVolume.Bounds = new Bounds(center, size);

                        foreach(var volumeGroup in volume.VolumeGroups)
                        {
                            List<LODGroup> lodGroups = new List<LODGroup>(volumeGroup.LODGroups.Count);

                            foreach (LODGroup group in volumeGroup.LODGroups)
                            {

                                if (WithinBounds(group, lodVolume.Bounds))
                                {
                                    lodGroups.Add(group);
                                }

                            }

                            if (lodGroups.Count > 0)
                            {
                                lodVolume.SetLODGroups(volumeGroup.GroupName, lodGroups);
                            }

                        }

                        volume.AddChild(lodVolume);
                        yield return BuildOctree(lodVolume);
                    }
                }
            }

            
        }
        
        IEnumerator BuildHLOD(LODVolume volume)
        {
            foreach (var volumeGroup in volume.VolumeGroups)
            {
                string groupName = volumeGroup.GroupName;
                if (m_GroupHLODRootContainer.ContainsKey(groupName) == false)
                {
                    var groupRoot = new GameObject(groupName);
                    groupRoot.layer = LayerMask.NameToLayer(LODVolume.HLODLayer);
                    groupRoot.transform.parent = m_HLODRootContainer.transform;
                    m_GroupHLODRootContainer.Add(groupName, groupRoot);
                }

                yield return CreateHLODObject(volumeGroup.LODGroups, volume.Bounds, volumeGroup, volume.gameObject.name);
            }
        }

        IEnumerator UpdateLODGroup(LODVolume volume)
        {
            List<Renderer> lodRenderers = new List<Renderer>();
            foreach (var volumeGroup in volume.VolumeGroups)
            {
                if (volumeGroup.HLODObject == null)
                    continue;

                lodRenderers.AddRange(volumeGroup.HLODObject.GetComponentsInChildren<Renderer>(false));
            }

            LOD lod = new LOD();
            LOD detailLOD = new LOD();

            detailLOD.screenRelativeTransitionHeight = m_Options.LODRange;
            lod.screenRelativeTransitionHeight = 0.0f;

            var lodGroup = volume.GetComponent<LODGroup>();
            if (!lodGroup)
                lodGroup = volume.gameObject.AddComponent<LODGroup>();

            volume.LodGroup = lodGroup;

            lod.renderers = lodRenderers.ToArray();
            lodGroup.SetLODs(new LOD[] {detailLOD, lod});
            yield break;
        }

        IEnumerator CreateHLODObject(List<LODGroup> lodGroups, Bounds volumeBounds, LODVolume.LODVolumeGroup volumeGroup, string volumeName)
        {
            List<Renderer> hlodRenderers = new List<Renderer>();
            int depth = GetDepth(volumeBounds);

            foreach (var group in lodGroups)
            {
                var lastLod = group.GetLODs().Last();
                foreach (var lr in lastLod.renderers)
                {

                    if (lr && lr.GetComponent<MeshFilter>())
                    {
                        hlodRenderers.Add(lr);
                    }
                }
            }

            if (hlodRenderers.Count == 0)
                yield break;

            var hlodLayer = LayerMask.NameToLayer(LODVolume.HLODLayer);
            var hlodRoot = new GameObject("HLOD " + volumeName);
            hlodRoot.layer = LayerMask.NameToLayer(LODVolume.HLODLayer);
            hlodRoot.transform.parent = m_GroupHLODRootContainer[volumeGroup.GroupName].transform;

            volumeGroup.HLODObject = hlodRoot;

            var parent = hlodRoot.transform;
            for (int i = 0; i < hlodRenderers.Count; ++i)
            {
                var r = hlodRenderers[i];
                var rendererTransform = r.transform;

                var child = new GameObject(r.name, typeof(MeshFilter), typeof(MeshRenderer));
                child.layer = hlodLayer;
                var childTransform = child.transform;
                childTransform.SetPositionAndRotation(rendererTransform.position, rendererTransform.rotation);
                childTransform.localScale = rendererTransform.lossyScale;
                childTransform.SetParent(parent, true);

                var mr = child.GetComponent<MeshRenderer>();
                var mf = child.GetComponent<MeshFilter>();

                EditorUtility.CopySerialized(r.GetComponent<MeshFilter>(), mf);
                EditorUtility.CopySerialized(r.GetComponent<MeshRenderer>(), mr);

                //This will be used many time.
                //so, start new coroutine and make it more efficient.
                StartCustomCoroutine(UpdateMesh(volumeGroup.GroupName, child, depth), CoroutineOrder.UpdateMesh);
            }

            

        }

        IEnumerator UpdateMesh(string groupName, GameObject gameObject, int depth)
        {
            var r = gameObject.GetComponent<Renderer>();
            var mf = gameObject.GetComponent<MeshFilter>();
            if (r == null || mf == null)
                yield break;

            yield return GetLODMesh(groupName, r, depth, (mesh) =>
            {
                mf.sharedMesh = mesh;
            });
        }


        IEnumerator BuildBatch()
        {
            foreach (var pair in m_GroupHLODRootContainer)
            {
                var groupOptions = m_Options.GroupOptions[pair.Key];
                if (groupOptions.Batcher == null)
                    yield break;
                yield return groupOptions.Batcher.Batch(pair.Value);
            }
        }

        public void Create(Dictionary<string, List<HLODGroup>> groups, Action finishAction)
        {
            var lodGroups = FindAllGroups(groups);
            if (lodGroups.Count == 0)
                return;

            //find biggest bounds including all groups.
            var lodGroupList = lodGroups.Values.ToList();
            Bounds bounds = CalcBounds(lodGroupList[0]);

            for (int i = 1; i < lodGroupList.Count; ++i)
            {
                bounds.Encapsulate(CalcBounds(lodGroupList[i]));
            }

            m_RootVolume = LODVolume.Create();
            m_RootVolume.SetLODGroups(lodGroups);
            m_RootVolume.Bounds = bounds;

            m_HLODRootContainer = new GameObject(k_HLODRootContainer);
            m_HLODRootContainer.AddComponent<SceneLODUpdater>();

            m_GroupHLODRootContainer = new Dictionary<string, GameObject>();

            StartCustomCoroutine(BuildOctree(m_RootVolume), CoroutineOrder.BuildTree);
            StartCustomCoroutine(BuildBatch(), CoroutineOrder.Batch);

            StartCustomCoroutine(EnqueueAction(finishAction), CoroutineOrder.Finish);
            StartCustomCoroutine(EnqueueAction(() =>
            {
                if ( m_RootVolume != null )
                    m_RootVolume.ResetLODGroup();
            }), CoroutineOrder.Finish);
        }

        public bool IsCreating()
        {
            return m_JobContainer.Count != 0;
        }

        public void CancelCreating()
        {
            m_JobContainer.Clear();
            SimplifierRunner.instance.Cancel();
        }


        void OnEnable()
        {
            m_JobContainer.Clear();
            EditorApplication.update += EditorUpdate;
        }

       
        void OnDisable()
        {
            m_JobContainer.Clear();
            EditorApplication.update -= EditorUpdate;
        }

        private void StartCustomCoroutine(IEnumerator func, CoroutineOrder priority)
        {
            Job job = new Job();
            job.Priority = (int)priority;
            job.FunctionStack.Push(func);

            if ( m_JobContainer.ContainsKey(job.Priority) == false)
                m_JobContainer[job.Priority] = new List<Job>();

            List<Job> list = m_JobContainer[job.Priority];
            list.Add(job);
        }
        private void EditorUpdate()
        {
            if (m_JobContainer.Count == 0)
                return;

            //After all jobs of the previous priority are finished, the next priority should be executed.
            var firstItem = m_JobContainer.First();
            var jobList = firstItem.Value;
            int containerCount = jobList.Count;

            for (int i = 0; i < containerCount;)
            {
                Job job = jobList[i];

                while (true)
                {
                    if (job.FunctionStack.Count == 0)
                    {
                        jobList.RemoveAt(i);
                        containerCount -= 1;
                        break;
                    }

                    IEnumerator func = job.FunctionStack.Peek();
                    if (func.MoveNext())
                    {
                        if (func.Current == null)
                        {
                            i += 1;
                            break;
                        }

                        if (func.Current is IEnumerator)
                        {
                            job.FunctionStack.Push(func.Current as IEnumerator);
                        }
                        else if (func.Current is MoveToEnd)
                        {
                            jobList.RemoveAt(i);
                            jobList.Add(job);
                            containerCount -= 1;
                            break;
                        }
                        
                        
                    }
                    else
                    {
                        job.FunctionStack.Pop();
                    }
                }
	     
            }

            if (jobList.Count == 0)
            {
                m_JobContainer.Remove(firstItem.Key);
            }
        }

        IEnumerator GetLODMesh(string groupName, Renderer renderer, int depth, Action<Mesh> returnCallback)
        {
            var groupOptions = m_Options.GroupOptions[groupName];
            var mf = renderer.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                yield break;
            }

            var mesh = mf.sharedMesh;

            if (groupOptions.VolumeSimplification == false)
            {
                if (returnCallback != null)
                    returnCallback(mesh);
                
                yield break;
            }


            List<Mesh> meshes;
            if (m_LODMeshes.ContainsKey(mesh))
            {
                meshes = m_LODMeshes[mesh];
            }
            else
            {
                meshes = new List<Mesh>();
                m_LODMeshes[mesh] = meshes;

                meshes.Add(mesh);
            }
 
            //where is resize?
            while (meshes.Count <= depth)
            {
                meshes.Add(null);
            }

            if (meshes[depth] == null)
            {
                //make simplification mesh by default mesh.
                if (meshes[0] != null)
                {
                    float quality = Mathf.Pow(groupOptions.VolumePolygonRatio, depth + 1);
                    int expectTriangleCount = (int) (quality * meshes[0].triangles.Length);

                    //It need for avoid crash when simplificate in Simplygon
                    //Mesh has less vertices, it crashed when save prefab.
                    if (expectTriangleCount < 10)
                    {
                        yield return GetLODMesh(groupName, renderer, depth -1, returnCallback);
                        yield break;
                    }


                    var simplifiedMesh = new Mesh();

                    var inputMesh = meshes[0].ToWorkingMesh();
                    var outputMesh = new WorkingMesh();
                    
                    var meshSimplifier = (IMeshSimplifier)Activator.CreateInstance(AutoLOD.meshSimplifierType);

                    bool isDone = false;

                    meshSimplifier.Simplify(inputMesh, outputMesh, quality, () =>
                    {
                        outputMesh.ApplyToMesh(simplifiedMesh);
                        simplifiedMesh.RecalculateBounds();
                        isDone = true;
                    });


                    while (isDone == false)
                        yield return new MoveToEnd();

                    meshes[depth] = simplifiedMesh;
                }
            }

            if (returnCallback != null)
            {
                returnCallback(meshes[depth]);
            }
        }


        /// <summary>
        /// Make IEnumerator with action for using coroutine.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        IEnumerator EnqueueAction(Action action)
        {
            if (action != null)
                action();

            yield break;
        }

        int GetDepth(Bounds bounds)
        {
            float size = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            int depth = 0;

            if (size > m_Options.VolumeSize)
            {
                depth += 1;
                size /= 2.0f;
            }

            return depth;

        }

        class Job
        {
            public int Priority;
            public Stack<IEnumerator> FunctionStack = new Stack<IEnumerator>();
        };

        private Dictionary<Mesh, List<Mesh>> m_LODMeshes = new Dictionary<Mesh, List<Mesh>>();
        private SortedDictionary<int, List<Job> > m_JobContainer = new SortedDictionary<int, List<Job> >();

    }

}