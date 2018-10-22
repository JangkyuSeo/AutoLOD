using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Unity.AutoLOD
{
    public class SceneLODUpdater : MonoBehaviour
    {
        [SerializeField]
        private LODVolume m_RootLODVolume;

        public LODVolume RootLODVolume
        {
            set { m_RootLODVolume = value; }
            get { return m_RootLODVolume; }
        }


        interface IRendererProxy
        {
            void Initialize();
            void SetEnable(bool enable);
        }

        class LODGroupRendererProxy : IRendererProxy
        {
            private LODGroup m_Group;

            public LODGroupRendererProxy(LODGroup group)
            {
                m_Group = group;
                m_Group.SetEnabled(true);
            }

            public void Initialize()
            {
                m_Group.SetEnabled(true);
            }
            public void SetEnable(bool enable)
            {
                m_Group.SetEnabled(enable);
            }
        }

        class VolumeRendererProxy : IRendererProxy
        {
            private SceneLODUpdater m_Updater;
            private int m_Index;
            private bool? m_LastState;
            public VolumeRendererProxy(SceneLODUpdater updater, int index)
            {
                m_Updater = updater;
                m_Index = index;
                
            }
            public void Initialize()
            {
                m_LastState = null;
            }
            public void SetEnable(bool enable)
            {
                if (m_LastState == enable)
                    return;

                if (enable == true)
                {
                    m_Updater.m_ActiveVolumes.Add(m_Index);
                }
                else
                {
                    var renderer = m_Updater.m_Renderers[m_Index];
                    for (int li = 0; li < renderer.LODMeshes.Count; ++li)
                    {
                        renderer.LODMeshes[li].enabled = false;
                    }

                    for (int ri = 0; ri < renderer.Renderers.Count; ++ri)
                    {
                        renderer.Renderers[ri].SetEnable(false);
                    }
                    m_Updater.m_ActiveVolumes.Remove(m_Index);
                    
                }

                m_LastState = enable;
            }
        }

        struct VolumeBounds
        {
            public Vector3 Center;
            public float Size;        //< volume is cuboid. so, every axis is the same size.
        }

        struct VolumeRenderer
        {
            public List<IRendererProxy> Renderers;
            public List<Renderer> LODMeshes;
        }

        //private NativeArray<VolumeBounds> m_Bounds;
        private List<VolumeBounds> m_Bounds;
        private List<VolumeRenderer> m_Renderers;

        private List<int> m_ActiveVolumes = new List<int>();

        void Initialize()
        {
            m_RootLODVolume.ResetLODGroup();
            for (int i = 0; i < m_Renderers.Count; ++i)
            {
                var renderer = m_Renderers[i];
                for (int ri = 0; ri < renderer.Renderers.Count; ++ri)
                {
                    renderer.Renderers[ri].Initialize();
                }

                for (int mi = 0; mi < renderer.LODMeshes.Count; ++mi)
                {
                    renderer.LODMeshes[mi].enabled = false;
                }
            }

            m_ActiveVolumes.Clear();
            m_ActiveVolumes.Add(0);
        }
#region UnityEvents
        void Awake()
        {            
            if (m_RootLODVolume != null)
            {
                Build();
            }
        }

        void OnDestroy()
        {
        }

        IEnumerator Start()
        {
            yield return new WaitForEndOfFrame();
            Initialize();
        }

        void OnEnable()
        {
            Camera.onPreCull += OnPreCull;
            if (m_RootLODVolume != null)
            {
                Initialize();
            }

        }

        void OnDisable()
        {
            Camera.onPreCull -= OnPreCull;
            if (m_RootLODVolume != null)
            {
                m_RootLODVolume.ResetLODGroup();
            }

        }
#endregion

        public void Build()
        {
            if (m_RootLODVolume == null)
            {
                Debug.LogError("SceneLODUpdate build failed. RootLODVolume is null.");
                return;
            }

            //it build by BFS.
            List<VolumeBounds> boundsList = new List<VolumeBounds>();
            List<VolumeRenderer> rendererList = new List<VolumeRenderer>();

            Queue<LODVolume> treeTrevelQueue = new Queue<LODVolume>();
            Queue<int> parentIndexQueue = new Queue<int>();

            treeTrevelQueue.Enqueue(m_RootLODVolume);
            parentIndexQueue.Enqueue(-1);

            while (treeTrevelQueue.Count > 0)
            {
                var current = treeTrevelQueue.Dequeue();
                VolumeBounds bounds;

                int currentIndex = boundsList.Count;
                int parentIndex = parentIndexQueue.Dequeue();

                if (current.VolumeGroups.Count == 0)
                    continue;


                bounds.Center = current.Bounds.center;
                bounds.Size = current.Bounds.size.x;

                boundsList.Add(bounds);

                VolumeRenderer renderer;
                renderer.LODMeshes = new List<Renderer>();
                renderer.Renderers = new List<IRendererProxy>();

                LODGroup lodGroup = current.GetComponent<LODGroup>();
                if (lodGroup != null)
                {
                    LOD[] lods = lodGroup.GetLODs();

                    if (lods.Length >= 2)
                    {
                        for (int ri = 0; ri < lods[1].renderers.Length; ++ri)
                        {
                            renderer.LODMeshes.Add(lods[1].renderers[ri]);
                        }
                    }
                }

                if (current.ChildVolumes.Count == 0)
                {
                    for (int vi = 0; vi < current.VolumeGroups.Count; ++vi)
                    {
                        var volumeGroup = current.VolumeGroups[vi];
                        for (int gi = 0; gi < volumeGroup.LODGroups.Count; ++gi)
                        {
                            renderer.Renderers.Add(new LODGroupRendererProxy(volumeGroup.LODGroups[gi]));
                        }
                    }
                }

                rendererList.Add(renderer);

                //If FirstChildIndex of the parent is empty, this is the first node.
                if (parentIndex != -1)
                {
                    var parentRenderer = rendererList[parentIndex];
                    parentRenderer.Renderers.Add(new VolumeRendererProxy(this, currentIndex));
                }

                for (int i = 0; i < current.ChildVolumes.Count; ++i)
                {
                    treeTrevelQueue.Enqueue(current.ChildVolumes[i]);
                    parentIndexQueue.Enqueue(currentIndex);
                }
            }

            m_Bounds = boundsList;
            m_Renderers = rendererList;

            if ( m_Bounds.Count > 0)
                m_ActiveVolumes.Add(0);
        }

        private void OnPreCull(Camera cam)
        {
            var cameraTransform = cam.transform;
            var cameraPosition = cameraTransform.position;

            float preRelative = 0.0f;
            if (cam.orthographic)
            {
                preRelative = 0.5f / cam.orthographicSize;
            }
            else
            {
                float halfAngle = Mathf.Tan(Mathf.Deg2Rad * cam.fieldOfView * 0.5F);
                preRelative = 0.5f / halfAngle;
            }

            preRelative = preRelative * QualitySettings.lodBias;

            //Items are added or removed in the loop.
            //That's OK because be edited item is always after current index.
            //DO NOT CHANGE this loop to foreach.
            for (int i = 0; i < m_ActiveVolumes.Count; ++i)
            {
                int index = m_ActiveVolumes[i];
                var bounds = m_Bounds[index];
                var renderer = m_Renderers[index];

                float distance = 1.0f;
                if ( cam.orthographic == false)
                    distance = Vector3.Distance(bounds.Center, cameraPosition);

                float relativeHeight = bounds.Size * preRelative / distance;
                bool renderDetail = relativeHeight > 0.3f;

                for (int li = 0; li < renderer.LODMeshes.Count; ++li)
                {
                    renderer.LODMeshes[li].enabled = !renderDetail;
                }
                for (int ri = 0; ri < renderer.Renderers.Count; ++ri)
                {
                    renderer.Renderers[ri].SetEnable(renderDetail);
                }
            }
        }        
    }

}