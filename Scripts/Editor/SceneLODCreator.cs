﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.AutoLOD.Utilities;
using UnityEditor;
using UnityEngine;
using Cache = Unity.AutoLOD.LODCache.Cache;
using Mesh = UnityEngine.Mesh;

namespace Unity.AutoLOD
{
    public class SceneLODCreator : ScriptableSingleton<SceneLODCreator>
    {
        enum CoroutineOrder
        {
            BuildTree,          //< Make octree created with LODVolume.
            UpdateMesh,            
            Batch,
            UpdateLOD,
            Finish,
        }
        
        private const string k_HLODRootContainer = "HLODs";
        private const int k_Splits = 2;

        private GameObject m_HLODRootContainer;
        private Dictionary<string, GameObject> m_GroupHLODRootContainer;

        private int currentJobCount = 0;
        private int maxJobCount = 0;
        private int currentProgress = 0;
        private int maxProgress = 0;
        
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

        static List<LODGroup> RemoveExclude(List<LODGroup> groups)
        {
            groups.RemoveAll(r =>
            {
                if (r == null)
                    return false;

                return r.GetComponentInParent<HLODExcluder>() != null;
            });

            return groups; 
        }

        static Dictionary<string, List<LODGroup>> FindAllGroups()
        {
            Dictionary<string, List<LODGroup>> lodGroups = new Dictionary<string, List<LODGroup>>();
            List<LODGroup> allGroups = RemoveExclude(RemoveHLODLayer( FindObjectsOfType<LODGroup>().ToList() ));

            lodGroups.Add("", new List<LODGroup>());

            foreach (var group in allGroups)
            {
                var hlodGroup = group.GetComponentInParent<HLODGroup>();

                if (hlodGroup != null)
                {

                    if (lodGroups.ContainsKey(hlodGroup.GroupName) == false)
                        lodGroups.Add(hlodGroup.GroupName, new List<LODGroup>());

                    lodGroups[hlodGroup.GroupName].Add(group);
                }
                else
                {
                    lodGroups[""].Add(group);
                }
            }

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
            if (boundsSize < Config.VolumeSize)
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

            detailLOD.screenRelativeTransitionHeight = Config.LODRange;
            lod.screenRelativeTransitionHeight = 0.0f;

            var lodGroup = volume.GetComponent<LODGroup>();
            if (!lodGroup)
                lodGroup = volume.gameObject.AddComponent<LODGroup>();

            volume.LodGroup = lodGroup;

            lod.renderers = lodRenderers.ToArray();
            lodGroup.SetLODs(new LOD[] {detailLOD, lod});

            //bounds is cuboid.
            //it has the same size each axis.
            lodGroup.size = volume.Bounds.size.x;
            yield break;
        }

        IEnumerator CreateHLODObject(List<LODGroup> lodGroups, Bounds volumeBounds, LODVolume.LODVolumeGroup volumeGroup, string volumeName)
        {
            List<Renderer> hlodRenderers = new List<Renderer>();
            int depth = GetDepth(volumeBounds);

            var groupOptions = Config.GetGroupConfig(volumeGroup.GroupName);

            foreach (var group in lodGroups)
            {
                var lastLod = group.GetLODs().Last();
                foreach (var lr in lastLod.renderers)
                {
                    float max = Mathf.Max(Mathf.Max(lr.bounds.size.x, lr.bounds.size.y), lr.bounds.size.z);
                    if (max < groupOptions.LODThresholdSize)
                        continue;

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
            int index = 0;
            foreach (var pair in m_GroupHLODRootContainer)
            {
                var groupOptions = Config.GetGroupConfig(pair.Key);
                if (groupOptions.Batcher == null)
                    yield break;

                yield return groupOptions.Batcher.Batch(pair.Value, (progress) =>
                {
                    currentJobCount = (int) (index * 1000 + progress * 1000);
                    maxJobCount = m_GroupHLODRootContainer.Count * 1000;
                });

                index += 1;
            }
        }

        public void Create(Action finishAction)
        {
            MonoBehaviourHelper.maxSharedExecutionTimeMS = 50.0f;

            //Assets should be saved which changed.
            AssetDatabase.SaveAssets();          

            var lodGroups = FindAllGroups();
            if (lodGroups.Count == 0)
                return;

            //find biggest bounds including all groups.
            var lodGroupList = lodGroups.Values.ToList();
            Bounds bounds = CalcBounds(lodGroupList[0]);

            for (int i = 1; i < lodGroupList.Count; ++i)
            {
                bounds.Encapsulate(CalcBounds(lodGroupList[i]));
            }

            var rootVolume = LODVolume.Create();
            rootVolume.SetLODGroups(lodGroups);
            rootVolume.Bounds = bounds;

            m_HLODRootContainer = new GameObject(k_HLODRootContainer);
            m_HLODRootContainer.AddComponent<SceneLODUpdater>();

            m_GroupHLODRootContainer = new Dictionary<string, GameObject>();

            StartCustomCoroutine(BuildOctree(rootVolume), CoroutineOrder.BuildTree);
            StartCustomCoroutine(BuildBatch(), CoroutineOrder.Batch);

            StartCustomCoroutine(EnqueueAction(finishAction), CoroutineOrder.Finish);
            StartCustomCoroutine(EnqueueAction(() =>
            {
                if (rootVolume != null)
                {
                    rootVolume.ResetLODGroup();   

                    if (m_HLODRootContainer != null)
                    {
                        var updater = m_HLODRootContainer.GetComponent<SceneLODUpdater>();
                        updater.Build(rootVolume);
                        
                        DestroyImmediate(rootVolume.gameObject);
                    }
                    
                }
            }), CoroutineOrder.Finish);

            currentJobCount = 0;
            maxJobCount = m_JobContainer.First().Value.Count;
            currentProgress = 0;
            maxProgress = (int)CoroutineOrder.Finish + 1;
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

        public int GetCurrentJobCount()
        {
            return currentJobCount;
        }
        public int GetMaxJobCount()
        {
            return maxJobCount;
        }

        public int GetCurrentProgress()
        {
            return currentProgress;
        }

        public int GetMaxProgress()
        {
            return maxProgress;
        }



        public void Destroy()
        {
            MonoBehaviourHelper.StartCoroutine(ObjectUtils.FindGameObject("HLODs",
                root => { DestroyImmediate(root); }));

            Utilities.FileUtils.DeleteDirectory(Application.dataPath + Path.DirectorySeparatorChar + SceneLOD.GetSceneLODPath());
            AssetDatabase.Refresh();
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

            var sw = new Stopwatch();
            sw.Start();

            //After all jobs of the previous priority are finished, the next priority should be executed.
            var firstItem = m_JobContainer.First();
            var jobList = firstItem.Value;

            for (int i = 0; i < jobList.Count;)
            {
                Job job = jobList[i];

                while (true)
                {
                    if (sw.ElapsedMilliseconds > 50)
                    {
                        sw.Stop();
                        return;
                    }
                    if (job.FunctionStack.Count == 0)
                    {
                        currentJobCount += 1;
                        jobList.RemoveAt(i);
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

                        var enumerator = func.Current as IEnumerator;
                        if (enumerator != null)
                        {
                            job.FunctionStack.Push(enumerator);
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
                currentProgress += 1;

                if (m_JobContainer.Count > 0)
                {
                    currentJobCount = 0;
                    maxJobCount = m_JobContainer.First().Value.Count + 1;
                }
                else
                {
                    //Do not be set zero.
                    //Avoid to divide by zero.
                    currentJobCount = 1;
                    maxJobCount = 1;
                }
            }
        }

        IEnumerator GetLODMesh(string groupName, Renderer renderer, int depth, Action<Mesh> returnCallback)
        {
            var groupOptions = Config.GetGroupConfig(groupName);
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

            if (mesh.triangles.Length < 10)
            {
                if (returnCallback != null)
                    returnCallback(mesh);
                
                yield break;
            }

            //if the mesh has under of max triangle count, a max quality should be one
            //otherwise, it should be a rate of a max triangle.
            int triangleCount = Math.Max(mesh.triangles.Length / 3, groupOptions.LODTriangleMax);
            float maxQuality = (float)groupOptions.LODTriangleMax / (float)triangleCount;
            float minQuality = (float) groupOptions.LODTriangleMin / (float) triangleCount;

            float quality = maxQuality * Mathf.Pow(groupOptions.VolumePolygonRatio, depth + 1);
            quality = Mathf.Min(maxQuality, Mathf.Max(minQuality, quality));

            Mesh simplifiedMesh = Cache.GetLODMesh(mesh, quality);

            //wait for finish baking.
            while (simplifiedMesh.triangles.Length == 0)
            {
                yield return null;
            }

            if (returnCallback != null)
            {
                returnCallback(simplifiedMesh);
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

            while (size > Config.VolumeSize)
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

        private SortedDictionary<int, List<Job> > m_JobContainer = new SortedDictionary<int, List<Job> >();

    }

}