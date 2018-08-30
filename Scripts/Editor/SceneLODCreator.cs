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

        private const int k_MaxWorkerCount = 4;
        private const string k_HLODRootContainer = "HLODs";

        private GameObject m_HLODRootContainer;

        public IEnumerator CreateHLODs(LODVolume volume)
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

            yield return CreateHLODsReculsive(volume);
        }

        IEnumerator CreateHLODsReculsive(LODVolume volume)
        {
            // Process children first, since we are now combining children HLODs to make parent HLODs
            foreach (Transform child in volume.transform)
            {
                var childLODVolume = child.GetComponent<LODVolume>();
                if (childLODVolume)
                    yield return CreateHLODsReculsive(childLODVolume);
            }

            StartCustomCoroutine(GenerateHLOD(volume));
        }


        void OnEnable()
        {
            m_WorkContainer.Clear();
            EditorApplication.update += EditorUpdate;
        }

       
        void OnDisable()
        {
            m_WorkContainer.Clear();
            EditorApplication.update -= EditorUpdate;
        }

        private void StartCustomCoroutine(IEnumerator func)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(func);

            m_WorkContainer.Add(stack);
        }
        private void EditorUpdate()
        {
            if (m_WorkContainer.Count == 0)
                return;

            int containerCount = m_WorkContainer.Count;

            for (int i = 0; i < containerCount;)
            {
                Stack<IEnumerator> stack = m_WorkContainer[i];

                while (true)
                {
                    if (stack.Count == 0)
                    {
                        m_WorkContainer.RemoveAt(i);
                        containerCount -= 1;
                        break;
                    }

                    IEnumerator func = stack.Peek();
                    if (func.MoveNext())
                    {
                        if (func.Current == null)
                        {
                            i += 1;
                            break;
                        }

                        if (func.Current is IEnumerator)
                        {
                            stack.Push(func.Current as IEnumerator);
                        }
                        else if (func.Current is MoveToEnd)
                        {
                            m_WorkContainer.RemoveAt(i);
                            m_WorkContainer.Add(stack);
                            containerCount -= 1;
                            break;
                        }
                    }
                    else
                    {
                        stack.Pop();
                    }
                }
	     
            }
        }


        IEnumerator GenerateHLOD(LODVolume volume)
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

                yield return GetLODMesh(r, hlodRendererDepths[i], (mesh) =>
                {
                    mf.sharedMesh = mesh;
                });
            }

            LOD lod = new LOD();
            LOD detailLOD = new LOD();

            detailLOD.screenRelativeTransitionHeight = 0.3f;
            lod.screenRelativeTransitionHeight = 0.0f;

            var lodGroup = volume.GetComponent<LODGroup>();
            if (!lodGroup)
                lodGroup = volume.gameObject.AddComponent<LODGroup>();

            volume.LodGroup = lodGroup;


            var batcher = (IBatcher)Activator.CreateInstance(LODVolume.batcherType);
            yield return batcher.Batch(hlodRoot);

            lod.renderers = hlodRoot.GetComponentsInChildren<Renderer>(false);
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

        private Dictionary<Mesh, List<Mesh>> m_LODMeshes = new Dictionary<Mesh, List<Mesh>>();

        private bool m_IsWorking = false;
        private List<BackgroundWorker> m_Workers = new List<BackgroundWorker>();
        private List<Stack<IEnumerator>> m_WorkContainer = new List<Stack<IEnumerator>>();

    }

}