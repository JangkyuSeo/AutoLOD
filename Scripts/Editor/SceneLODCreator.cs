using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEditor.Experimental.AutoLOD.Utilities;
using UnityEngine;
using UnityEngine.Experimental.AutoLOD;
using Mesh = UnityEngine.Mesh;
using Object = UnityEngine.Object;

namespace UnityEditor.Experimental.AutoLOD
{
    public class SceneLODCreator : ScriptableSingleton<SceneLODCreator>
    {
        /// <summary>
        /// job will be moved to end of queue.
        /// </summary>
        class MoveToEnd : YieldInstruction
        {
            public MoveToEnd()
            {

            }
        }


        //TODO: Move to option this later.
        private const bool k_UseSimplificationHLOD = true;

        private const int k_MaxWorkerCount = 4;
        private const string k_HLODRootContainer = "HLODs";

        private GameObject m_HLODRootContainer;

        public IEnumerator CreateHLODs(LODVolume volume, Action finishAction)
        {
            yield return ObjectUtils.FindGameObject(k_HLODRootContainer, root =>
            {
                if (root)
                    m_HLODRootContainer = root;
            });

            if (!m_HLODRootContainer)
            {
                m_HLODRootContainer = new GameObject(k_HLODRootContainer);
                m_HLODRootContainer.AddComponent<SceneLODUpdater>();
            }

            //m_HLODRootContainer.SetActive(false);

            yield return CreateHLODsReculsive(volume);

            var batcher = (IBatcher)Activator.CreateInstance(LODVolume.batcherType);
            StartCustomCoroutine(batcher.Batch(m_HLODRootContainer), 1);


            yield return UpdateMeshReculsive(volume);
            //StartCustomCoroutine(EnqueueAction(() => { m_HLODRootContainer.SetActive(true); }), 1);
            StartCustomCoroutine(EnqueueAction(finishAction), 3);
        }

        IEnumerator EnqueueAction(Action action)
        {
            if (action != null)
                action();

            yield break;
        }

        IEnumerator CreateHLODsReculsive(LODVolume volume)
        {
            foreach (Transform child in volume.transform)
            {
                var childLODVolume = child.GetComponent<LODVolume>();
                if (childLODVolume)
                    yield return CreateHLODsReculsive(childLODVolume);
            }

            yield return GenerateHLODObject(volume);
        }

        IEnumerator UpdateMeshReculsive(LODVolume volume)
        {
            foreach (Transform child in volume.transform)
            {
                var childLODVolume = child.GetComponent<LODVolume>();
                if (childLODVolume)
                    yield return UpdateMeshReculsive(childLODVolume);
            }

            StartCustomCoroutine(UpdateMesh(volume), 0);
            StartCustomCoroutine(UpdateLODGroup(volume), 2);
        }

        public bool IsCreating()
        {
            return m_JobContainer.Count != 0;
        }

        public void CancelCreating()
        {
            m_JobContainer.Clear();
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

        private void StartCustomCoroutine(IEnumerator func, int priority)
        {
            Job job = new Job();
            job.Priority = priority;
            job.FunctionStack.Push(func);

            if ( m_JobContainer.ContainsKey(priority) == false)
                m_JobContainer[priority] = new List<Job>();

            List<Job> list = m_JobContainer[priority];
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

        IEnumerator GenerateHLODObject(LODVolume volume)
        {
            List<Renderer> hlodRenderers = new List<Renderer>();
            List<int> hlodRendererDepths = new List<int>();

            foreach (var group in volume.LodGroups)
            {
                var lastLod = group.GetLODs().Last();
                int depth = GetDepth(volume, group);
                foreach (var lr in lastLod.renderers)
                {

                    if (lr && lr.GetComponent<MeshFilter>())
                    {
                        hlodRenderers.Add(lr);
                        hlodRendererDepths.Add(depth);
                    }
                }
            }

            CleanupHLOD(volume);

            if (hlodRenderers.Count == 0)
                yield break;

            var hlodLayer = LayerMask.NameToLayer(LODVolume.HLODLayer);
            var hlodRoot = new GameObject("HLOD " + volume.gameObject.name);
            hlodRoot.layer = LayerMask.NameToLayer(LODVolume.HLODLayer);
            hlodRoot.transform.parent = m_HLODRootContainer.transform;

            volume.HLODRoot = hlodRoot;

            var parent = hlodRoot.transform;
            for(int i = 0; i < hlodRenderers.Count; ++i)
            {
                var r = hlodRenderers[i];
                var rendererTransform = r.transform;

                var child = new GameObject(r.name, typeof(MeshFilter), typeof(MeshRenderer), typeof(DepthHolder));
                child.layer = hlodLayer;
                var childTransform = child.transform;
                childTransform.SetPositionAndRotation(rendererTransform.position, rendererTransform.rotation);
                childTransform.localScale = rendererTransform.lossyScale;
                childTransform.SetParent(parent, true);

                var mr = child.GetComponent<MeshRenderer>();
                var mf = child.GetComponent<MeshFilter>();
                var dh = child.GetComponent<DepthHolder>();

                EditorUtility.CopySerialized(r.GetComponent<MeshFilter>(), mf);
                EditorUtility.CopySerialized(r.GetComponent<MeshRenderer>(), mr);
                dh.Depth = hlodRendererDepths[i];
            }


        }

        IEnumerator UpdateMesh(LODVolume volume)
        {
            if ( volume.HLODRoot == null )
                yield break;

            foreach (Transform child in volume.HLODRoot.transform)
            {
                var r = child.GetComponent<Renderer>();
                var mf = child.GetComponent<MeshFilter>();
                var dh = child.GetComponent<DepthHolder>();
                if ( r == null || mf == null|| dh == null)
                    continue;

                yield return GetLODMesh(r, dh.Depth, (mesh) =>
                {
                    mf.sharedMesh = mesh;
                });
            }
        }

        IEnumerator UpdateLODGroup(LODVolume volume)
        {
            if ( volume.HLODRoot == null )
                yield break;
            
            LOD lod = new LOD();
            LOD detailLOD = new LOD();

            detailLOD.screenRelativeTransitionHeight = 0.3f;
            lod.screenRelativeTransitionHeight = 0.0f;

            var lodGroup = volume.GetComponent<LODGroup>();
            if (!lodGroup)
                lodGroup = volume.gameObject.AddComponent<LODGroup>();

            volume.LodGroup = lodGroup;

            lod.renderers = volume.HLODRoot.GetComponentsInChildren<Renderer>(false);
            lodGroup.SetLODs(new LOD[] { detailLOD, lod });
        }

        
        void CleanupHLOD(LODVolume volume)
        {
            var root = volume.HLODRoot;
            if (root) // Clean up old HLOD
            {
                var mf = root.GetComponent<MeshFilter>();
                if (mf)
                    Object.DestroyImmediate(mf.sharedMesh, true); // Clean up file on disk

                Object.DestroyImmediate(root);

                volume.HLODRoot = null;
            }
        }

        /// <summary>
        /// Return depth from leaf by LODGroup.
        /// </summary>
        int GetDepth(LODVolume currentVolume, LODGroup group)
        {
            if (currentVolume.transform.childCount > 0)
            {
                foreach (Transform child in currentVolume.transform)
                {
                    var childVolume = child.GetComponent<LODVolume>();
                    if (childVolume != null && childVolume.LodGroups.Contains(group))
                    {
                        return GetDepth(childVolume, group) + 1;
                    }
                }
            }
            else
            {
                //current volume is leaf node.
                //depth of leaf node is always 0.
                //LODGroup must be in LODVolue.
                return 0;
            }

            //This is weird situation.
            //LODGroup must be in LODVolume.
            Debug.LogError("LODGroup is not in LODVolume.\n\tLODGroup : " + group.name + "\n\tLODVolume : " + currentVolume.name);
            return -1;
        }

        IEnumerator GetLODMesh(Renderer renderer, int depth, Action<Mesh> returnCallback)
        {
            var mf = renderer.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                yield break;
            }

            var mesh = mf.sharedMesh;

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

            if (k_UseSimplificationHLOD == false)
            {
                if (returnCallback != null)
                    returnCallback(mesh);
                
                yield break;
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
                    float quality = Mathf.Pow(0.5f, depth + 1);
                    int expectTriangleCount = (int) (quality * meshes[0].triangles.Length);

                    //It need for avoid crash when simplificate in Simplygon
                    //Mesh has less vertices, it crashed when save prefab.
                    if (expectTriangleCount < 10)
                    {
                        yield return GetLODMesh(renderer, depth -1, returnCallback);
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


        class Job
        {
            public int Priority;
            public Stack<IEnumerator> FunctionStack = new Stack<IEnumerator>();
        };

        private Dictionary<Mesh, List<Mesh>> m_LODMeshes = new Dictionary<Mesh, List<Mesh>>();
        private SortedDictionary<int, List<Job> > m_JobContainer = new SortedDictionary<int, List<Job> >();

    }

}